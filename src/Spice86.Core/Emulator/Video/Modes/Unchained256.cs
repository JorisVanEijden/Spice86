using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Video;

namespace Spice86.Core.Emulator.Video.Modes;

/// <summary>
/// Provides functionality for planar 256-color VGA modes.
/// </summary>
public class Unchained256 : Planar4
{
    public Unchained256(int width, int height, VgaCard video)
        : base(width, height, 8, 8, VideoModeType.Graphics, video)
    {
    }
}
