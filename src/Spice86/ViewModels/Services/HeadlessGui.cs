namespace Spice86.ViewModels.Services;

using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Emulator.Video;
using Spice86.Shared.Interfaces;

/// <inheritdoc cref="IGuiVideoPresentation" />
public sealed class HeadlessGui : IGuiVideoPresentation, IGuiMouseEvents,
    IGuiKeyboardEvents, IDisposable {
    private readonly object _drawingLock = new();

    private bool _disposed;

    private Thread? _drawThread;
    private volatile bool _isAppClosing;
    private bool _isSettingResolution;

    private byte[]? _pixelBuffer;
    private bool _renderingTimerInitialized;

    public HeadlessGui() {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress += OnProcessExit;
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void ShowMouseCursor() {
    }

    public void HideMouseCursor() {
    }

#pragma warning disable CS0067 // Headless GUI never raises these events
    public event EventHandler<KeyboardEventArgs>? KeyUp;
    public event EventHandler<KeyboardEventArgs>? KeyDown;
    public event EventHandler<MouseMoveEventArgs>? MouseMoved;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonDown;
    public event EventHandler<MouseButtonEventArgs>? MouseButtonUp;
    public event EventHandler<UIRenderEventArgs>? RenderScreen;
    public event Action? UserInterfaceInitialized;
#pragma warning restore CS0067

    public int Width { get; private set; }

    public int Height { get; private set; }

    public double MouseX { get; set; }

    public double MouseY { get; set; }

    public void SetResolution(int width, int height) {
        if (width <= 0 || height <= 0) {
            throw new ArgumentOutOfRangeException($"Invalid resolution: {width}x{height}");
        }

        _isSettingResolution = true;
        try {
            if (Width != width || Height != height) {
                Width = width;
                Height = height;
                if (_disposed) {
                    return;
                }

                int bufferSize = width * height * 4;

                lock (_drawingLock) {
                    if (_pixelBuffer == null || _pixelBuffer.Length != bufferSize) {
                        _pixelBuffer = new byte[bufferSize];
                    }

                    Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);
                }
            }
        } finally {
            _isSettingResolution = false;
        }

        InitializeRenderingTimer();
    }

    private void OnProcessExit(object? sender, EventArgs e) {
        _isAppClosing = true;
    }

    private void InitializeRenderingTimer() {
        if (_renderingTimerInitialized) {
            return;
        }

        _renderingTimerInitialized = true;
        _drawThread = new Thread(DrawLoop) {
            Name = "VGA Refresh",
            IsBackground = true
        };
        _drawThread.Start();
    }

    private void DrawLoop() {
        try {
            while (!_disposed && !_isAppClosing) {
                DrawScreen();
            }
        } catch (Exception e) {
            Console.Error.WriteLine($"[HeadlessGui] VGA thread crashed: {e}");
        }
    }

    private unsafe void DrawScreen() {
        byte[]? pixelBuffer = _pixelBuffer;
        if (_disposed || _isSettingResolution || _isAppClosing || pixelBuffer is null || RenderScreen is null) {
            return;
        }

        lock (_drawingLock) {
            fixed (byte* bufferPtr = pixelBuffer) {
                int rowBytes = Width * 4; // 4 bytes per pixel (BGRA)
                int length = rowBytes * Height / 4;

                var uiRenderEventArgs = new UIRenderEventArgs((IntPtr)bufferPtr, length);
                RenderScreen.Invoke(this, uiRenderEventArgs);
            }
        }
    }

    private void Dispose(bool disposing) {
        if (_disposed) {
            return;
        }

        _disposed = true;
        if (!disposing) {
            return;
        }

        // Signal the draw thread to stop (it checks _disposed in its loop)
        // and wait for it to finish
        _drawThread?.Join(TimeSpan.FromMilliseconds(200));

        // Wait for any ongoing draw operation to complete
        lock (_drawingLock) {
            _pixelBuffer = null;
        }
    }
}
