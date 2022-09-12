using Spice86.Core.Emulator.VM;

namespace Spice86.Core.Emulator.Errors;

/// <summary>
/// Represents an emulated page fault exception.
/// </summary>
[Serializable]
public sealed class PageFaultException : InvalidVMOperationException
{
    private readonly bool _userMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageFaultException"/> class.
    /// </summary>
    /// <param name="faultAddress">Address which caused the page fault.</param>
    /// <param name="cause">Type of operation which cause the page fault.</param>
    public PageFaultException(Machine machine, uint faultAddress, PageFaultCause cause)
        : base(machine, "Page fault")
    {
        this.FaultAddress = faultAddress;
        this.Cause = cause;
    }

    /// <summary>
    /// Gets the address which caused the page fault.
    /// </summary>
    public uint FaultAddress { get; }
    /// <summary>
    /// Gets the type of operation which caused the page fault.
    /// </summary>
    public PageFaultCause Cause { get; }

    /// <summary>
    /// Gets the optional error code for the interrupt.
    /// </summary>
    public int? ErrorCode
    {
        get
        {
            int errorCode = 0;
            if (this.Cause == PageFaultCause.Write)
                errorCode |= (1 << 1);
            //else if(this.Cause == PageFaultCause.InstructionFetch)
            //    errorCode |= (1 << 4);

            if (this._userMode)
                errorCode |= (1 << 2);

            return errorCode;
        }
    }
}
