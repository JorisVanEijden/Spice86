﻿namespace Spice86.Shared.Interfaces;

using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Emulator.Mouse;
using Spice86.Shared.Emulator.Video;

/// <summary>
/// GUI of the emulator.<br/>
/// Displays the content of the video ram (when the emulator requests it) <br/>
/// Communicates keyboard and mouse events to the emulator <br/>
/// This is the MainWindowViewModel.
/// </summary>
public interface IGui {
    /// <summary>
    /// Shows the UI mouse cursor
    /// </summary>
    void ShowMouseCursor();

    /// <summary>
    /// Hides the UI mouse cursor
    /// </summary>
    void HideMouseCursor();

    /// <summary>
    /// Indicates whether a keyboard key is up.
    /// </summary>
    public event EventHandler<KeyboardEventArgs>? KeyUp;

    /// <summary>
    /// Indicates whether a keyboard key is down.
    /// </summary>
    public event EventHandler<KeyboardEventArgs>? KeyDown;

    /// <summary>
    /// X coordinates of the mouse cursor, in pixels.
    /// </summary>
    double MouseX { get; set; }

    /// <summary>
    /// Y coordinates of the mouse cursor, in pixels.
    /// </summary>
    double MouseY { get; set; }

    /// <summary>
    /// Width of the video source for the GUI to display.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Height of the video source for the GUI to display.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// On video mode change: Set Resolution of the video source for the GUI to display
    /// </summary>
    /// <param name="videoWidth">The width in pixels</param>
    /// <param name="videoHeight">The height in pixels</param>
    void SetResolution(int videoWidth, int videoHeight);

    /// <summary>
    /// Invoked when the GUI asks the VideoCard to render the screen contents in the WriteableBitmap's buffer pointer.
    /// </summary>
    event EventHandler<UIRenderEventArgs>? RenderScreen;

    /// <summary>
    /// Indicate that the mouse has moved.
    /// </summary>
    event EventHandler<MouseMoveEventArgs>? MouseMoved;

    /// <summary>
    /// Indicate that a mouse button has been pressed.
    /// </summary>
    event EventHandler<MouseButtonEventArgs>? MouseButtonDown;

    /// <summary>
    /// Indicate that a mouse button has been released.
    /// </summary>
    event EventHandler<MouseButtonEventArgs>? MouseButtonUp;
}