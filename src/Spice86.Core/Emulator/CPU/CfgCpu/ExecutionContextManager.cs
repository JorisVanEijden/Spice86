﻿namespace Spice86.Core.Emulator.CPU.CfgCpu;

using Spice86.Core.Emulator.CPU.CfgCpu.Linker;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Memory;

public class ExecutionContextManager {
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly Dictionary<uint, ExecutionContext> _executionContextEntryPoints = new();
    
    public ExecutionContextManager(EmulatorBreakpointsManager emulatorBreakpointsManager) {
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        // Initial context at init
        CurrentExecutionContext = new();
        InitialExecutionContext = CurrentExecutionContext;
    }
    
    public ExecutionContext InitialExecutionContext { get; }

    public ExecutionContext CurrentExecutionContext { get; private set; }

    public void SignalNewExecutionContext(SegmentedAddress entryAddress, SegmentedAddress? expectedReturnAddress) {
        uint physicalEntryAddress = entryAddress.ToPhysical();
        if (!_executionContextEntryPoints.TryGetValue(physicalEntryAddress, out ExecutionContext? executionContext)) {
            executionContext = new ExecutionContext();
            _executionContextEntryPoints.Add(entryAddress.ToPhysical(), executionContext);
        }
        // Reset the execution context so that nodes it last executed are not linked to the new ones that will come
        executionContext.LastExecuted = null;
        executionContext.NodeToExecuteNextAccordingToGraph = null;
        ExecutionContext previousExecutionContext = CurrentExecutionContext;
        CurrentExecutionContext = executionContext;
        if (expectedReturnAddress != null) {
            // breakpoint that deletes itself on reach. Should be triggered when the return address is reached and before it starts execution.
            _emulatorBreakpointsManager.ToggleBreakPoint(new AddressBreakPoint(BreakPointType.EXECUTION, expectedReturnAddress.Value.ToPhysical(), (_) => {
                // Restore previous execution context
                CurrentExecutionContext = previousExecutionContext;
            }, true), true);
        }
    }

}