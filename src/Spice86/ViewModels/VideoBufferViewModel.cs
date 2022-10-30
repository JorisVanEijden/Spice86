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

using Spice86.Shared;
using Spice86.Shared.Interfaces;
using Spice86.Views;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

/// <inheritdoc />
public sealed partial class VideoBufferViewModel : ObservableObject, IVideoBufferViewModel, IComparable<VideoBufferViewModel>, IDisposable {
    private bool _disposedValue;

    private Thread? _drawThread;

    private bool _exitDrawThread;

    private readonly ManualResetEvent _manualResetEvent = new(false);

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

    public VideoBufferViewModel(double scale, int width, int height, uint address, int index, bool isPrimaryDisplay) {
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
        if (_appClosing || _disposedValue || UIUpdateMethod is null) {
            return;
        }
        StartDrawThreadIfNeeded();
        if (_drawAction is null || _lastUsedVideoMode != videoMode) {
            _lastUsedVideoMode = videoMode;
            _drawAction = new Action(() => {
                switch (videoMode) {
                    case VideoMode10h.ColorGraphics640x350x4:
                        DrawVga640x350x4(memory, palette);
                        break;
                    case VideoMode10h.Graphics320x200x8:
                        DrawVga320x200x8(memory, palette);
                        break;
                    default:
                        break;
                }
                UpdateGui();
            });
        }
        WaitForNextCall();
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

    // TODO: Inject graphics presenters for all the supported video mdoes
    // TODO: extend the caller to recognize those other video modes
    // TODO: use a fixed pointer over the memory param (temporary for code import)
    // TODO: Make them all work with Gdb video buffer start offset relative to the current video mode
    // TODO: Remove all pointers and pointers arithmetic everywhere, except for the ILockedFramebuffer stuff in this file.
    private unsafe void DrawVga640x350x4(byte[] memory, Rgb[] palette) {
        if (Bitmap is null) {
            return;
        }
        _frameRenderTimeWatch.Restart();
        ILockedFramebuffer pixels = Bitmap.Lock();
        const int width = 640;
        const int height = 350;

        uint* firstPixelAddress = (uint*)pixels.Address;
        const int stride = 80;
        const int horizontalPan = 0;
        const int startOffset = 0;

        int safeWidth = Math.Min(stride, width / 8);
        const int bitPan = horizontalPan % 8;

        Span<byte> src = new Span<byte>(memory);
        uint* destPtr = firstPixelAddress;

        Span<Rgb> paletteSpan = new Span<Rgb>(palette);
        fixed (byte* srcPtr = src) {
            fixed (Rgb* paletteMap = paletteSpan) {
                const int destStart = 0;

                for (int split = 0; split < 2; split++) {
                    for (int y = 0; y < height; y++) {
                        int srcPos = ((stride * y) + startOffset + (horizontalPan / 8)) & 0xFFFF;
                        int destPos = (width * y) + destStart;

                        for (int i = bitPan; i < 8; i++) {
                            destPtr[destPos++] = palette[paletteMap[UnpackIndex(srcPtr[srcPos], 7 - i)]];
                        }

                        srcPos++;

                        for (int xb = 1; xb < safeWidth; xb++) {
                            // vram is stored as:
                            // [p1byte] [p2byte] [p3byte] [p4byte]
                            // to build index for nibble one:
                            // p1[0] p2[0] p3[0] p4[0]

                            uint p = srcPtr[srcPos & 0xFFFF];
                            int palIndex = UnpackIndex(p, 0);
                            destPtr[destPos + 7] = palette[paletteMap[palIndex]];

                            palIndex = UnpackIndex(p, 1);
                            destPtr[destPos + 6] = palette[paletteMap[palIndex]];

                            palIndex = UnpackIndex(p, 2);
                            destPtr[destPos + 5] = palette[paletteMap[palIndex]];

                            palIndex = UnpackIndex(p, 3);
                            destPtr[destPos + 4] = palette[paletteMap[palIndex]];

                            palIndex = UnpackIndex(p, 4);
                            destPtr[destPos + 3] = palette[paletteMap[palIndex]];

                            palIndex = UnpackIndex(p, 5);
                            destPtr[destPos + 2] = palette[paletteMap[palIndex]];

                            palIndex = UnpackIndex(p, 6);
                            destPtr[destPos + 1] = palette[paletteMap[palIndex]];

                            palIndex = UnpackIndex(p, 7);
                            destPtr[destPos] = palette[paletteMap[palIndex]];

                            destPos += 8;
                            srcPos++;
                        }

                        srcPos &= 0xFFFF;

                        for (int i = 0; i < bitPan; i++) {
                            destPtr[destPos++] = palette[paletteMap[UnpackIndex(srcPtr[srcPos], 7 - i)]];
                        }
                    }

                    // if (height < this.VideoMode.Height)
                    // {
                    //     startOffset = 0;
                    //     height = this.VideoMode.Height - this.VideoMode.LineCompare - 1;
                    //     destStart = this.VideoMode.LineCompare * width;
                    // }
                    // else
                    // {
                    //     break;
                    // }
                }
            }
        }
    }

    private static int UnpackIndex(uint value, int index) {
        if (System.Runtime.Intrinsics.X86.Bmi2.IsSupported) {
            return (int)System.Runtime.Intrinsics.X86.Bmi2.ParallelBitExtract(value, 0x01010101u << index);
        } else {
            return (int)(((value & (1u << index)) >> index) | ((value & (0x100u << index)) >> (7 + index)) | ((value & (0x10000u << index)) >> (14 + index)) | ((value & (0x1000000u << index)) >> (21 + index)));
        }
    }

    private unsafe void DrawVga320x200x8(byte[] memory, Rgb[] palette) {
        if (Bitmap is null) {
            return;
        }
        _frameRenderTimeWatch.Restart();
        ILockedFramebuffer pixels = Bitmap.Lock();
        uint* firstPixelAddress = (uint*)pixels.Address;
        int rowBytes = Width;
        uint memoryAddress = Address;
        uint* currentRow = firstPixelAddress;
        for (int row = 0; row < Height; row++) {
            uint* startOfLine = currentRow;
            uint* endOfLine = currentRow + Width;
            for (uint* column = startOfLine; column < endOfLine; column++) {
                byte colorIndex = memory[memoryAddress];
                Rgb pixel = palette[colorIndex];
                uint argb = pixel.ToArgb();
                if (pixels.Format == PixelFormat.Rgba8888) {
                    argb = pixel.ToRgba();
                }
                *column = argb;
                memoryAddress++;
            }
            currentRow += rowBytes;
        }
    }

    private void UpdateGui() {
        Dispatcher.UIThread.Post(() => {
            UIUpdateMethod?.Invoke();
            FramesRendered++;
        }, DispatcherPriority.Render);
        _frameRenderTimeWatch.Stop();
        LastFrameRenderTimeMs = _frameRenderTimeWatch.ElapsedMilliseconds;
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