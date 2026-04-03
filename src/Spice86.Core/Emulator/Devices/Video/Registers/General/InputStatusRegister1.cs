namespace Spice86.Core.Emulator.Devices.Video.Registers.General;

using System.Threading;

/// <summary>
///     The address for this read-only register is address hex 03DA or 03BA.
///     Do not write to this register.
/// </summary>
public class InputStatusRegister1 : Register8 {
    private byte _value;

    /// <summary>
    ///     Thread-safe access to the register value. The VGA refresh thread writes this
    ///     register while the CPU thread reads it via port 0x3DA.
    /// </summary>
    public override byte Value {
        get => Volatile.Read(ref _value);
        set => Volatile.Write(ref _value, value);
    }
    /// <summary>
    ///     When the Vertical Retrace field (bit 3) is 1, it indicates a vertical retrace interval. This bit can be programmed,
    ///     through the Vertical Retrace End register, to generate an interrupt at the start of the vertical retrace.
    /// </summary>
    public bool VerticalRetrace {
        get => GetBit(3);
        set => SetBit(3, value);
    }

    /// <summary>
    ///     When the Display Enable field (bit 0) is 1, it indicates a horizontal or vertical retrace interval. This bit is the
    ///     real-time status of the inverted ‘display enable’ signal. In the past, programs have used this status bit to
    ///     restrict screen updates to the inactive display intervals to reduce screen flicker. The video subsystem is
    ///     designed to eliminate this software requirement; screen updates may be made at any time without screen
    ///     degradation.
    /// </summary>
    public bool DisplayDisabled {
        get => GetBit(0);
        set => SetBit(0, value);
    }
}