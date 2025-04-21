namespace Spice86.ViewModels;

using Iced.Intel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Calculates the next execution address for an instruction based on its flow control type and CPU state.
/// </summary>
public class NextExecutionAddressCalculator {
    private readonly Instruction _instruction;
    private readonly SegmentedAddress _currentAddress;

    /// <summary>
    /// Initializes a new instance of the <see cref="NextExecutionAddressCalculator"/> class.
    /// </summary>
    /// <param name="instruction">The instruction to calculate the next execution address for.</param>
    /// <param name="currentAddress">The current segmented address of the instruction.</param>
    public NextExecutionAddressCalculator(Instruction instruction, SegmentedAddress currentAddress) {
        _instruction = instruction;
        _currentAddress = currentAddress;
    }

    /// <summary>
    /// Calculates the next execution address based on the instruction's flow control type and CPU state.
    /// </summary>
    /// <param name="cpuState">The current CPU state.</param>
    /// <param name="memory">The memory to read from for indirect jumps/calls.</param>
    /// <returns>The next execution address.</returns>
    public SegmentedAddress Calculate(State cpuState, IMemory memory) {
        // Default next address is the linear address following this instruction
        SegmentedAddress nextAddress = _currentAddress + (ushort)_instruction.Length;

        return _instruction.FlowControl switch {
            FlowControl.ConditionalBranch => HandleConditionalBranch(cpuState, nextAddress),
            FlowControl.UnconditionalBranch => HandleUnconditionalBranch(cpuState, memory),
            FlowControl.Call => HandleCall(cpuState),
            FlowControl.IndirectCall => HandleIndirectCall(cpuState, memory),
            FlowControl.Return => HandleReturn(cpuState, memory),
            FlowControl.IndirectBranch => HandleIndirectBranch(cpuState, memory),
            FlowControl.Interrupt => HandleInterrupt(cpuState, memory, nextAddress),
            FlowControl.Next => nextAddress,
            FlowControl.Exception => nextAddress,
            _ => throw new ArgumentOutOfRangeException($"Unhandled FlowControl {_instruction.FlowControl}")
        };
    }

    private SegmentedAddress HandleConditionalBranch(State cpuState, SegmentedAddress defaultNextAddress) {
        bool willJump = _instruction.ConditionCode switch {
            ConditionCode.None => _instruction.IsLoop && cpuState.CX != 0,
            ConditionCode.o => cpuState.OverflowFlag,
            ConditionCode.no => !cpuState.OverflowFlag,
            ConditionCode.b => cpuState.CarryFlag,
            ConditionCode.ae => !cpuState.CarryFlag,
            ConditionCode.e => cpuState.ZeroFlag,
            ConditionCode.ne => !cpuState.ZeroFlag,
            ConditionCode.be => cpuState.CarryFlag || cpuState.ZeroFlag,
            ConditionCode.a => !cpuState.CarryFlag && !cpuState.ZeroFlag,
            ConditionCode.s => cpuState.SignFlag,
            ConditionCode.ns => !cpuState.SignFlag,
            ConditionCode.p => cpuState.ParityFlag,
            ConditionCode.np => !cpuState.ParityFlag,
            ConditionCode.l => cpuState.SignFlag != cpuState.OverflowFlag,
            ConditionCode.ge => cpuState.SignFlag == cpuState.OverflowFlag,
            ConditionCode.le => cpuState.ZeroFlag || cpuState.SignFlag != cpuState.OverflowFlag,
            ConditionCode.g => !cpuState.ZeroFlag && cpuState.SignFlag == cpuState.OverflowFlag,
            _ => throw new ArgumentOutOfRangeException()
        };

        if (willJump) {
            // All conditional branches in x86 are near branches (within the current segment)
            return GetNearBranchTarget(cpuState);
        }

        // If the condition is false, the next instruction is the one after this one
        return defaultNextAddress;
    }

    private SegmentedAddress HandleUnconditionalBranch(State cpuState, IMemory memory) {
        if (_instruction.IsJmpShortOrNear) {
            return GetNearBranchTarget(cpuState);
        }
        if (_instruction.IsJmpFar) {
            return GetFarBranchTarget();
        }

        return _instruction.Op0Kind switch {
            OpKind.Register => GetRegisterBasedBranchTarget(cpuState),
            OpKind.Memory => GetMemoryBasedBranchTarget(cpuState, memory),
            _ => throw new InvalidOperationException("Unable to determine target")
        };
    }

    private SegmentedAddress GetMemoryBasedBranchTarget(State cpuState, IMemory memory) {
        // Memory-based indirect jump - read the target address from memory
        ushort segment = _instruction.SegmentPrefix == Register.None ? cpuState.DS : GetSegmentValue(cpuState, _instruction.SegmentPrefix);

        // Read the target offset from memory at the effective address
        ushort effectiveOffset = ushort.CreateChecked(CalculateEffectiveOffset(cpuState, ref segment));

        // Check if this is a far pointer (32-bit) or near pointer (16-bit)
        bool isFarPointer = _instruction.IsCallFar || _instruction.IsCallFarIndirect || _instruction.IsJmpFar || _instruction.IsJmpFarIndirect || _instruction.MemorySize == MemorySize.UInt32;

        if (isFarPointer) {
            // For far pointers, read both offset and segment (32-bit total)
            // First read the offset (first 16 bits)
            ushort offset = memory.UInt16[segment, effectiveOffset];
            // Then read the segment (next 16 bits)
            ushort targetSegment = memory.UInt16[segment, (ushort)(effectiveOffset + 2)];

            return new SegmentedAddress(targetSegment, offset);
        } else {
            // For near pointers, read only the offset (16-bit)
            ushort offset = memory.UInt16[segment, effectiveOffset];

            // For near jumps, the segment remains the same (CS)
            return new SegmentedAddress(cpuState.CS, offset);
        }
    }

    private SegmentedAddress GetRegisterBasedBranchTarget(State cpuState) {
        // For register-based jumps (e.g., JMP AX), we can determine the target
        // from the CPU state
        ushort segment = cpuState.CS;
        ushort offset = ushort.CreateChecked(GetRegisterValue(cpuState, _instruction.Op0Register));

        return new SegmentedAddress(segment, offset);
    }

    private SegmentedAddress GetFarBranchTarget() {
        ushort segment = _instruction.FarBranchSelector;
        ushort offset;
        // Check if this is a 16-bit or 32-bit far branch
        if (_instruction.Op0Kind == OpKind.FarBranch16) {
            offset = _instruction.FarBranch16;
        } else if (_instruction.Op0Kind == OpKind.FarBranch32) {
            offset = ushort.CreateChecked(_instruction.FarBranch32);
        } else {
            // If we can't determine the type, we can't calculate the target address
            throw new InvalidOperationException($"Unable to determine type of far branch for '{_instruction}'");
        }

        return new SegmentedAddress(segment, offset);
    }

    private SegmentedAddress GetNearBranchTarget(State cpuState) {
        ushort segment = cpuState.CS;
        ushort offset = ushort.CreateChecked(_instruction.NearBranchTarget);

        return new SegmentedAddress(segment, offset);
    }

    private SegmentedAddress HandleCall(State cpuState) {
        if (_instruction.IsCallNear) {
            return GetNearBranchTarget(cpuState);
        }
        if (_instruction.IsCallFar) {
            return GetFarBranchTarget();
        }

        throw new InvalidOperationException("Unable to determine call type");
    }

    private SegmentedAddress HandleIndirectCall(State cpuState, IMemory memory) {
        return _instruction.Op0Kind switch {
            OpKind.Register => GetRegisterBasedBranchTarget(cpuState),
            OpKind.Memory => GetMemoryBasedBranchTarget(cpuState, memory),
            _ => throw new InvalidOperationException("Unable to determine target")
        };
    }

    private SegmentedAddress HandleReturn(State cpuState, IMemory memory) {
        // Read the return offset from the stack (16-bit value)
        ushort offset = memory.UInt16[cpuState.StackPhysicalAddress];

        // For near returns, the segment stays the same
        ushort segment = cpuState.CS;

        // For far returns, we also need to read the segment value
        if (_instruction.Mnemonic == Mnemonic.Retf) {
            segment = memory.UInt16[cpuState.StackPhysicalAddress + 2];
        }

        return new SegmentedAddress(segment, offset);
    }

    private SegmentedAddress HandleIndirectBranch(State cpuState, IMemory memory) {
        return _instruction.Op0Kind switch {
            OpKind.Register => GetRegisterBasedBranchTarget(cpuState),
            OpKind.Memory => GetMemoryBasedBranchTarget(cpuState, memory),
            _ => throw new InvalidOperationException("Unable to determine target")
        };
    }

    private SegmentedAddress HandleInterrupt(State cpuState, IMemory memory, SegmentedAddress defaultNextAddress) {
        try {
            // Get the interrupt number
            byte interruptNumber;

            if (_instruction.Mnemonic == Mnemonic.Int) {
                // For INT instruction, the interrupt number is in the immediate operand
                interruptNumber = _instruction.Immediate8;
            } else if (_instruction.Mnemonic == Mnemonic.Int3) {
                // INT3 is a special case - it's interrupt 3
                interruptNumber = 3;
            } else if (_instruction.Mnemonic == Mnemonic.Into) {
                // INTO is a special case - it's interrupt 4 if the overflow flag is set
                if (cpuState.OverflowFlag) {
                    interruptNumber = 4;
                } else {
                    // If overflow flag is not set, INTO doesn't trigger an interrupt
                    // So the next instruction is the one after this one (already set by default)
                    return defaultNextAddress;
                }
            } else {
                throw new InvalidOperationException($"Unsupported interrupt instruction: {_instruction.Mnemonic}");
            }

            // Calculate the address of the interrupt vector in the IVT
            uint ivtAddress = (uint)(interruptNumber * 4);

            // Read the interrupt handler address from the IVT
            ushort offset = (ushort)(memory[ivtAddress] | (memory[ivtAddress + 1] << 8));
            ushort segment = (ushort)(memory[ivtAddress + 2] | (memory[ivtAddress + 3] << 8));

            return new SegmentedAddress(segment, offset);
        } catch (Exception ex) {
            throw new InvalidOperationException($"Failed to determine target for interrupt instruction: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Helper method to get the segment value based on the segment register
    /// </summary>
    private static ushort GetSegmentValue(State cpuState, Register segmentRegister) {
        return segmentRegister switch {
            Register.ES => cpuState.ES,
            Register.CS => cpuState.CS,
            Register.SS => cpuState.SS,
            Register.DS => cpuState.DS,
            Register.FS => cpuState.FS,
            Register.GS => cpuState.GS,
            _ => cpuState.DS // Default to DS if unknown
        };
    }

    /// <summary>
    /// Helper method to get the register value based on the register name
    /// </summary>
    private static uint GetRegisterValue(State cpuState, Register register) {
        return register switch {
            Register.AX => cpuState.AX,
            Register.BX => cpuState.BX,
            Register.CX => cpuState.CX,
            Register.DX => cpuState.DX,
            Register.SI => cpuState.SI,
            Register.DI => cpuState.DI,
            Register.BP => cpuState.BP,
            Register.SP => cpuState.SP,
            Register.AL => cpuState.AL,
            Register.CL => cpuState.CL,
            Register.DL => cpuState.DL,
            Register.BL => cpuState.BL,
            Register.AH => cpuState.AH,
            Register.CH => cpuState.CH,
            Register.DH => cpuState.DH,
            Register.BH => cpuState.BH,
            Register.EAX => cpuState.EAX,
            Register.ECX => cpuState.ECX,
            Register.EDX => cpuState.EDX,
            Register.EBX => cpuState.EBX,
            Register.ESP => cpuState.ESP,
            Register.EBP => cpuState.EBP,
            Register.ESI => cpuState.ESI,
            Register.EDI => cpuState.EDI,
            Register.ES => cpuState.ES,
            Register.CS => cpuState.CS,
            Register.SS => cpuState.SS,
            Register.DS => cpuState.DS,
            Register.FS => cpuState.FS,
            Register.GS => cpuState.GS,
            Register.EIP => cpuState.IP,
            _ => throw new ArgumentOutOfRangeException(nameof(register), "Unsupported register")
        };
    }

    /// <summary>
    /// Helper method to calculate the effective address for memory operands
    /// </summary>
    private uint CalculateEffectiveOffset(State cpuState, ref ushort segment) {
        uint offset = 0;

        // Handle base register
        switch (_instruction.MemoryBase) {
            case Register.None:
                break;

            case Register.BX:
                offset = cpuState.BX;

                break;
            case Register.BP:
                offset = cpuState.BP;
                // BP defaults to SS segment unless overridden
                if (_instruction.SegmentPrefix == Register.None) {
                    segment = cpuState.SS;
                }

                break;
            case Register.SI:
                offset = cpuState.SI;

                break;
            case Register.DI:
                offset = cpuState.DI;

                break;
            // Handle 32-bit registers too
            case Register.EAX:
                offset = cpuState.EAX;

                break;
            case Register.EBX:
                offset = cpuState.EBX;

                break;
            case Register.ECX:
                offset = cpuState.ECX;

                break;
            case Register.EDX:
                offset = cpuState.EDX;

                break;
            case Register.ESP:
                offset = cpuState.ESP;
                // ESP defaults to SS segment unless overridden
                if (_instruction.SegmentPrefix == Register.None) {
                    segment = cpuState.SS;
                }

                break;
            case Register.EBP:
                offset = cpuState.EBP;
                // EBP defaults to SS segment unless overridden
                if (_instruction.SegmentPrefix == Register.None) {
                    segment = cpuState.SS;
                }

                break;
            case Register.ESI:
                offset = cpuState.ESI;

                break;
            case Register.EDI:
                offset = cpuState.EDI;

                break;
            default:
                throw new InvalidOperationException($"Unsupported base register: {_instruction.MemoryBase}");
        }

        // Add index register if present
        switch (_instruction.MemoryIndex) {
            case Register.None:
                break;
            case Register.SI:
                offset += cpuState.SI;

                break;
            case Register.DI:
                offset += cpuState.DI;

                break;
            // Handle 32-bit registers too
            case Register.EAX:
                offset += cpuState.EAX;

                break;
            case Register.EBX:
                offset += cpuState.EBX;

                break;
            case Register.ECX:
                offset += cpuState.ECX;

                break;
            case Register.EDX:
                offset += cpuState.EDX;

                break;
            case Register.EBP:
                offset += cpuState.EBP;

                break;
            case Register.ESI:
                offset += cpuState.ESI;

                break;
            case Register.EDI:
                offset += cpuState.EDI;

                break;
            default:
                throw new InvalidOperationException($"Unsupported index register: {_instruction.MemoryIndex}");
        }

        // Add displacement
        return offset + _instruction.MemoryDisplacement32;
    }
}