using Spice86.Core.Emulator.Video.Modes;
using Spice86.Shared;

namespace Spice86.Core.Emulator.Video.Rendering;
/// <summary>
/// Renders 8-bit graphics to a bitmap.
/// </summary>
public class GraphicsPresenter8 : Presenter
{
    /// <summary>
    /// Initializes a new instance of the GraphicsPresenter8 class.
    /// </summary>
    /// <param name="videoMode">VideoMode instance describing the video mode.</param>
    public unsafe GraphicsPresenter8(VideoMode videoMode) : base(videoMode)
    {
    }

    /// <summary>
    /// Updates the bitmap to match the current state of the video RAM.
    /// </summary>
    protected override unsafe void DrawFrame(IntPtr destination)
    {
        if (IsDisposed) {
            return;
        }
        uint totalPixels = (uint)this.VideoMode.Width * (uint)this.VideoMode.Height;
        ReadOnlySpan<Rgb> palette = this.VideoMode.Palette;
        byte* srcPtr = (byte*)this.VideoMode.VideoRam.ToPointer() + (uint)this.VideoMode.StartOffset;
        uint* destPtr = (uint*)destination.ToPointer();

        int height = this.VideoMode.Height;
        int width = this.VideoMode.Width;
        int offset = 0;
        for (int y = 0; y < height; y++)
        {
            uint* startPtr = destPtr + offset;
            uint* endPtr = destPtr + offset + width;
            for (uint* x = startPtr; x < endPtr; x++)
            {
                byte src = srcPtr[offset];
                Rgb color = palette[src];
                uint nativeColor = ToNativeColorFormat(color);
                * x = nativeColor;
                offset++;
            }
        }
    }
}
