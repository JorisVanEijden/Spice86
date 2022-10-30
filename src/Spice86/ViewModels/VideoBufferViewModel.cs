namespace Spice86.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Spice86.Core;
using Spice86.Core.Emulator.Video.Modes;
using Spice86.Core.Emulator.Video.Rendering;
using Spice86.Core.Emulator.VM;
using Spice86.Shared;
using Spice86.Shared.Interfaces;
using Spice86.Views;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

// TODO: Make all video modes work with Gdb video buffer start offset relative to the current video mode
// TODO: Remove all pointers and pointers arithmetic everywhere, except for the ILockedFramebuffer stuff in this file.
/// <inheritdoc />
public sealed partial class VideoBufferViewModel : ObservableObject, IVideoBufferViewModel, IComparable<VideoBufferViewModel>, IDisposable {
    private bool _disposedValue;

    private Thread? _drawThread;

    private bool _exitDrawThread;

    private readonly ManualResetEvent _manualResetEvent = new(false);

    private readonly Machine? _machine;

    private Presenter? _presenter;

    /// <summary>
    /// For AvaloniaUI Designer
    /// </summary>
    public VideoBufferViewModel() {
        if (Design.IsDesignMode == false) {
            throw new InvalidOperationException("This constructor is not for runtime usage");
        }
        Width = 320;
        Height = 200;
        Address = 1;
        _index = 1;
        Scale = 1;
        _frameRenderTimeWatch = new Stopwatch();
    }

    public VideoBufferViewModel(Machine machine, double scale, int width, int height, uint address, int index, bool isPrimaryDisplay) {
        _machine = machine;
        _isPrimaryDisplay = isPrimaryDisplay;
        Width = width;
        Height = height;
        Address = address;
        _index = index;
        Scale = scale;
        MainWindow.AppClosing += MainWindow_AppClosing;
        _frameRenderTimeWatch = new Stopwatch();
    }

    private void DrawThreadMethod() {
        while (!_exitDrawThread) {
            _drawAction?.Invoke();
            if (!_exitDrawThread) {
                _manualResetEvent.WaitOne();
            }
        }
    }

    private Action? UIUpdateMethod { get; set; }

    internal void SetUIUpdateMethod(Action invalidateImageAction) {
        UIUpdateMethod = invalidateImageAction;
    }

    [RelayCommand]
    public async Task SaveBitmap() {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            SaveFileDialog picker = new SaveFileDialog {
                DefaultExtension = "bmp",
                InitialFileName = "screenshot.bmp",
                Title = "Save Bitmap"
            };
            string? file = await picker.ShowAsync(desktop.MainWindow);
            if (string.IsNullOrWhiteSpace(file) == false) {
                Bitmap?.Save(file);
            }
        }
    }

    private void MainWindow_AppClosing(object? sender, System.ComponentModel.CancelEventArgs e) {
        _appClosing = true;
    }

    public uint Address { get; private set; }

    /// <summary>
    /// TODO : Get current DPI from Avalonia or Skia.
    /// It isn't DesktopScaling or RenderScaling as this returns 1 when Windows Desktop Scaling is set at 100%
    /// DPI: AvaloniaUI, like WPF, renders UI Controls in Device Independant Pixels.<br/>
    /// According to searches online, DPI is tied to a TopLevel control (a Window).<br/>
    /// Right now, the DPI is hardcoded for WriteableBitmap : https://github.com/AvaloniaUI/Avalonia/issues/1292 <br/>
    /// See also : https://github.com/AvaloniaUI/Avalonia/pull/1889 <br/>
    /// Also WriteableBitmap is an IImage implementation and not a UI Control,<br/>
    /// that's why it's used to bind the Source property of the Image control in VideoBufferView.xaml<br/>
    /// </summary>
    [ObservableProperty]
    private WriteableBitmap? _bitmap = new(new PixelSize(320, 200), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);

    private bool _showCursor = true;

    public bool ShowCursor {
        get => _showCursor;
        set {
            SetProperty(ref _showCursor, value);
            if (_showCursor) {
                Cursor?.Dispose();
                Cursor = Cursor.Default;
            } else {
                Cursor?.Dispose();
                Cursor = new Cursor(StandardCursorType.None);
            }
        }
    }

    [ObservableProperty]
    private Cursor? _cursor = Cursor.Default;

    private double _scale = 1;

    public double Scale {
        get => _scale;
        set => SetProperty(ref _scale, Math.Max(value, 1));
    }

    [ObservableProperty]
    private int _height = 320;

    [ObservableProperty]
    private bool _isPrimaryDisplay;

    [ObservableProperty]
    private int _width = 200;

    [ObservableProperty]
    private long _framesRendered = 0;

    private bool _appClosing;

    private readonly int _index;

    public int CompareTo(VideoBufferViewModel? other) {
        if (_index < other?._index) {
            return -1;
        }
        if (_index == other?._index) {
            return 0;
        }
        return 1;
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private readonly Stopwatch _frameRenderTimeWatch;

    private Action? _drawAction;

    private VideoMode10h _lastUsedVideoMode = VideoMode10h.Text80x25x1;

    public unsafe void Draw(byte[] memory, Rgb[] palette, VideoMode10h videoMode) {
        if (_machine is null || _bitmap is null || _machine?.VideoBiosInt10Handler.CurrentMode is null || _appClosing || _disposedValue || UIUpdateMethod is null) {
            return;
        }
        StartDrawThreadIfNeeded();
        _lastUsedVideoMode = videoMode;
        _drawAction = new Action(() => {
            _presenter = GetPresenter(_machine.VideoBiosInt10Handler.CurrentMode);
            if(_presenter is null) {
                return;
            }
            EnsureRenderTarget(_presenter);
            using ILockedFramebuffer buf = _bitmap.Lock();
            _presenter.Update(buf.Address);
            UpdateGui();
        });
        WaitForNextCall();
    }

    private void EnsureRenderTarget(Presenter presenter) {
        if (this._bitmap != null && presenter.TargetWidth == this._bitmap.PixelSize.Width && presenter.TargetHeight == this._bitmap.PixelSize.Height) {
            return;
        }
        this._bitmap?.Dispose();
        this._bitmap = new
            (new(presenter.TargetWidth,
            presenter.TargetHeight),
            new(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);
    }


    private Presenter? GetPresenter(VideoMode? videoMode) {
        if(videoMode is null) {
            return null;
        }

        if (videoMode.VideoModeType == VideoModeType.Text) {
            return new TextPresenter(videoMode, ToNativePixelFormat);
        } else {
            return videoMode.BitsPerPixel switch {
                2 => new GraphicsPresenter2(videoMode, ToNativePixelFormat),
                4 => new GraphicsPresenter4(videoMode, ToNativePixelFormat),
                8 when videoMode.IsPlanar => new GraphicsPresenterX(videoMode, ToNativePixelFormat),
                8 when !videoMode.IsPlanar => new GraphicsPresenter8(videoMode, ToNativePixelFormat),
                16 => new GraphicsPresenter16(videoMode, ToNativePixelFormat),
                _ => null
            };
        }
    }


    private uint ToNativePixelFormat(uint pixel) {
        if (this.Bitmap is null) {
            return pixel;
        }
        using ILockedFramebuffer buf = this.Bitmap.Lock();
        return buf.Format switch {
            PixelFormat.Rgba8888 => ToRgba(pixel),
            PixelFormat.Rgb565 => ToRgba(pixel),
            PixelFormat.Bgra8888 => ToArgb(pixel),
            _ => pixel
        };
    }


    private static uint ToRgba(uint pixel) {
        var color = System.Drawing.Color.FromArgb((int)pixel);
        return (uint)(color.R << 16 | color.G << 8 | color.B) | 0xFF000000;
    }

    private static uint ToBgra(uint pixel) {
        var color = System.Drawing.Color.FromArgb((int)pixel);
        return (uint)(color.B << 16 | color.G << 8 | color.R) | 0xFF000000;
    }

    private static uint ToArgb(uint pixel) {
        var color = System.Drawing.Color.FromArgb((int)pixel);
        return 0xFF000000 | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
    }

    private void UpdateGui() {
        Dispatcher.UIThread.Post(() => {
            UIUpdateMethod?.Invoke();
            FramesRendered++;
        }, DispatcherPriority.Render);
        _frameRenderTimeWatch.Stop();
        LastFrameRenderTimeMs = _frameRenderTimeWatch.ElapsedMilliseconds;
    }

    private unsafe void WaitForNextCall() {
        if (!_exitDrawThread) {
            _manualResetEvent.Set();
            _manualResetEvent.Reset();
        }
    }

    private unsafe void StartDrawThreadIfNeeded() {
        if (_drawThread is null) {
            _drawThread = new Thread(DrawThreadMethod) {
                Name = "UIRenderThread"
            };
            _drawThread.Start();
        }
    }

    [ObservableProperty]
    private long _lastFrameRenderTimeMs;

    public override bool Equals(object? obj) {
        return this == obj || ((obj is VideoBufferViewModel other) && _index == other._index);
    }

    public override int GetHashCode() {
        return _index;
    }

    private void Dispose(bool disposing) {
        if (!_disposedValue) {
            if (disposing) {
                _exitDrawThread = true;
                _manualResetEvent.Set();
                if (_drawThread?.IsAlive == true) {
                    _drawThread.Join();
                }
                _manualResetEvent.Dispose();
                Dispatcher.UIThread.Post(() => {
                    Bitmap?.Dispose();
                    Bitmap = null;
                    Cursor?.Dispose();
                    UIUpdateMethod?.Invoke();
                }, DispatcherPriority.MaxValue);
            }
            _disposedValue = true;
        }
    }
}