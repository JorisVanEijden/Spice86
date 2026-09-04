using System.Collections.Generic;

namespace Spice86.Core.Emulator.Mcp.Response;

using Spice86.Core.Emulator.InterruptHandlers.VGA.Records;

internal sealed record VideoDetailedStateResponse {
    public required int BiosVideoMode { get; init; }

    public required VgaMode Mode { get; init; }

    public required CursorPosition Cursor { get; init; }

    public required int ScreenColumns { get; init; }

    public required int ScreenRows { get; init; }

    public required int RendererWidth { get; init; }

    public required int RendererHeight { get; init; }

    public required int BufferSize { get; init; }

    /// <summary>
    ///     CRT Controller registers by index (0x00-0x18), or <c>null</c> if the register file is not
    ///     available. Reading these through the index/data port pair returns 0
    ///     (see TASK-316), so this is the only way to see what the CRTC is programmed to.
    /// </summary>
    public Dictionary<string, int>? CrtController { get; init; }

    /// <summary>Sequencer registers by index (0x00-0x04).</summary>
    public Dictionary<string, int>? Sequencer { get; init; }

    /// <summary>Graphics Controller registers by index (0x00-0x08).</summary>
    public Dictionary<string, int>? GraphicsController { get; init; }

    /// <summary>Attribute Controller registers by index (0x00-0x14).</summary>
    public Dictionary<string, int>? AttributeController { get; init; }
}