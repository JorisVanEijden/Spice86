namespace Spice86.Core.Emulator.Errors;

/// <summary>
/// Specifies the type of operation which caused a page fault.
/// </summary>
public enum PageFaultCause
{
    /// <summary>
    /// An invalid address was read.
    /// </summary>
    Read,
    /// <summary>
    /// An invalid address was written to.
    /// </summary>
    Write,
    /// <summary>
    /// An instruction was fetched from an invalid address.
    /// </summary>
    InstructionFetch
}
