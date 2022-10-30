using Spice86.Core.Emulator.InterruptHandlers.Video;

namespace Spice86.Core.Emulator.Video.Modes;

/// <summary>
/// Provides functionality for 16-color EGA and VGA video modes.
/// </summary>
public class EgaVga16 : Planar4
{
    public EgaVga16(int width, int height, int fontHeight, VideoBiosInt10Handler video)
        : base(width, height, 4, fontHeight, VideoModeType.Graphics, video)
    {
    }
}
