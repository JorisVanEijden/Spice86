﻿namespace Ix86.Emulator.ReverseEngineer;

using Ix86.Emulator.Cpu;
using Ix86.Emulator.Machine;

public class MemoryBasedDataStructureWithDsBaseAddress : MemoryBasedDataStructureWithSegmentRegisterBaseAddress
{
    public MemoryBasedDataStructureWithDsBaseAddress(Machine machine) : base(machine, SegmentRegisters.CS_INDEX)
    {

    }
}
