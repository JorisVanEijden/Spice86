namespace Spice86.ViewModels;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

using Iced.Intel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Infrastructure;
using Spice86.MemoryWrappers;
using Spice86.Messages;
using Spice86.Models.Debugging;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

using System.Globalization;

public partial class DisassemblyViewModel : ViewModelWithErrorDialog {
    private readonly IMemory _memory;
    private readonly State _state;
    private readonly IMessenger _messenger;
    private readonly IPauseHandler _pauseHandler;
    private readonly IInstructionExecutor _cpu;
    private readonly BreakpointsViewModel _breakpointsViewModel;
    private readonly EmulatorBreakpointsManager _emulatorBreakpointsManager;
    private readonly IDictionary<uint, FunctionInformation> _functionsInformation;

    public DisassemblyViewModel(
        IInstructionExecutor cpu, IMemory memory, State state,
        IDictionary<uint, FunctionInformation> functionsInformation,
        BreakpointsViewModel breakpointsViewModel, EmulatorBreakpointsManager emulatorBreakpointsManager,
        IPauseHandler pauseHandler, IUIDispatcher uiDispatcher,
        IMessenger messenger, ITextClipboard textClipboard, bool canCloseTab = false)
        : base(uiDispatcher, textClipboard) {
        _cpu = cpu;
        _functionsInformation = functionsInformation;
        Functions = new(functionsInformation
            .Select(x => new FunctionInfo() {
                Name = x.Value.Name,
                Address = x.Key,
        }).OrderBy(x => x.Address));
        AreFunctionInformationProvided = functionsInformation.Count > 0;
        _emulatorBreakpointsManager = emulatorBreakpointsManager;
        _breakpointsViewModel = breakpointsViewModel;
        _messenger = messenger;
        _memory = memory;
        _state = state;
        _pauseHandler = pauseHandler;
        IsPaused = pauseHandler.IsPaused;
        pauseHandler.Pausing += OnPausing;
        pauseHandler.Resumed += () => _uiDispatcher.Post(() => IsPaused = false);
        CanCloseTab = canCloseTab;
    }

    [ObservableProperty]
    private bool _areFunctionInformationProvided;

    private FunctionInfo? _selectedFunction;

    public FunctionInfo? SelectedFunction {
        get => _selectedFunction;
        set {
            _selectedFunction = value;
            OnPropertyChanged(nameof(SelectedFunction));
            if (value is not null) {
                uint address = value.Address;
                GoToAddress(address);
            }
        }
    }

    [ObservableProperty]
    private AvaloniaList<FunctionInfo> _functions = new();

    private void OnPausing() {
        _uiDispatcher.Post(() => {
            IsPaused = true;
            if(Instructions.Count == 0 && GoToCsIpCommand.CanExecute(null)) {
                GoToCsIpCommand.Execute(null);
            }
        });
    }

    [ObservableProperty]
    private string _header = "Disassembly View";

    [ObservableProperty]
    private AvaloniaList<CpuInstructionInfo> _instructions = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateDisassemblyCommand))]
    [NotifyCanExecuteChangedFor(nameof(GoToCsIpCommand))]
    [NotifyCanExecuteChangedFor(nameof(NewDisassemblyViewCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyLineCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepIntoCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepOverCommand))]
    private bool _isPaused;

    [ObservableProperty]
    private bool _creatingExecutionBreakpoint;

    [ObservableProperty]
    private string? _breakpointAddress;

    [RelayCommand]
    private void BeginCreateExecutionBreakpoint() {
        CreatingExecutionBreakpoint = true;
        BreakpointAddress = MemoryUtils.ToPhysicalAddress(_state.CS, _state.IP).ToString(CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void CancelCreateExecutionBreakpoint() {
        CreatingExecutionBreakpoint = false;
    }

    [RelayCommand]
    private void ConfirmCreateExecutionBreakpoint() {
        CreatingExecutionBreakpoint = false;
        if (!string.IsNullOrWhiteSpace(BreakpointAddress) &&
            TryParseMemoryAddress(BreakpointAddress, out ulong? breakpointAddressValue)) {
            AddressBreakPoint addressBreakPoint = new(BreakPointType.EXECUTION,
                (long)breakpointAddressValue, (breakpoint) => {
                    RequestPause(breakpoint);
                    UpdateDisassemblyInternal();
                }, false);
            _breakpointsViewModel.AddAddressBreakpoint(addressBreakPoint);
        }
    }

    [ObservableProperty]
    private int _numberOfInstructionsShown = 50;

    [ObservableProperty]
    private bool _isUsingLinearAddressing = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateDisassemblyCommand))]
    private SegmentedAddress? _segmentedStartAddress;

    private uint? _startAddress;

    public uint? StartAddress {
        get => _startAddress;
        set {
            SetProperty(ref _startAddress, value);
            UpdateDisassemblyCommand.NotifyCanExecuteChanged();
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseTabCommand))]
    private bool _canCloseTab;

    private void UpdateHeader(uint? address) {
        Header = address is null ? "" : $"0x{address:X}";
    }

    [RelayCommand(CanExecute = nameof(CanCloseTab))]
    private void CloseTab() => _messenger.Send(new RemoveViewModelMessage<DisassemblyViewModel>(this));

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepOver() {
        if(SelectedInstruction is null) {
            return;
        }
        var nextInstructionAddressInListing = SelectedInstruction.Address + SelectedInstruction.Length;
        var addressBreakpoint = new AddressBreakPoint(
            BreakPointType.EXECUTION,
            nextInstructionAddressInListing,
            (breakpoint) => {
                RequestPause(breakpoint);
                _uiDispatcher.Post(GoToCsIp);
            },
            isRemovedOnTrigger: true);
        _emulatorBreakpointsManager.ToggleBreakPoint(addressBreakpoint, on: true);
        _pauseHandler.Resume();
    }


    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void StepInto() {
        _cpu.ExecuteNext();
        if (!Instructions.GetRange(0, 15).Any(x => x.Address == _state.IpPhysicalAddress)) {
            GoToCsIp();
        } else {
            UpdateDisassemblyInternal();
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void NewDisassemblyView() {
        DisassemblyViewModel disassemblyViewModel = new(
            _cpu, _memory, _state, _functionsInformation, 
            _breakpointsViewModel, _emulatorBreakpointsManager, _pauseHandler, _uiDispatcher, _messenger,
            _textClipboard, canCloseTab: true) {
            IsPaused = IsPaused
        };
        _messenger.Send(new AddViewModelMessage<DisassemblyViewModel>(disassemblyViewModel));
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void GoToCsIp() {
        SegmentedStartAddress = new(_state.CS, _state.IP);
        GoToAddress(_state.IpPhysicalAddress);
    }

    private void GoToAddress(uint address) {
        StartAddress = address;
        UpdateDisassembly();
        SelectedInstruction = Instructions.FirstOrDefault();
    }

    private uint? GetStartAddress() {
        return IsUsingLinearAddressing switch {
            true => StartAddress,
            false => SegmentedStartAddress is null
                ? null
                : MemoryUtils.ToPhysicalAddress(SegmentedStartAddress.Value.Segment,
                    SegmentedStartAddress.Value.Offset),
        };
    }

    private bool CanExecuteUpdateDisassembly() {
        return IsPaused && GetStartAddress() is not null;
    }

    [RelayCommand(CanExecute = nameof(CanExecuteUpdateDisassembly))]
    private void UpdateDisassembly() {
        uint? startAddress = GetStartAddress();
        if (startAddress is null) {
            return;
        }
        Instructions = new(DecodeCurrentWindowOfInstructions(startAddress.Value));
        SelectedInstruction = Instructions.FirstOrDefault();
        UpdateHeader(SelectedInstruction?.Address);
    }

    private List<CpuInstructionInfo> DecodeCurrentWindowOfInstructions(uint startAddress) {
        return
            DecodeInstructions(
                _state,
                _memory,
                startAddress,
                NumberOfInstructionsShown);
    }

    private CpuInstructionInfo? _selectedInstruction;

    public CpuInstructionInfo? SelectedInstruction {
        get => _selectedInstruction;
        set {
            if (value is not null) {
                _selectedFunction = Functions.
                    FirstOrDefault(x => x.Address == value.Address);
                OnPropertyChanged(nameof(SelectedFunction));
            }
            _selectedInstruction = value;
            OnPropertyChanged(nameof(SelectedInstruction));
        }
    }

    [RelayCommand(CanExecute = nameof(IsPaused))]
    private async Task CopyLine() {
        if (SelectedInstruction is not null) {
            await _textClipboard.SetTextAsync(SelectedInstruction.StringRepresentation);
        }
    }

    private List<CpuInstructionInfo> DecodeInstructions(State state, IMemory memory, uint startAddress,
        int numberOfInstructionsShown) {
        CodeReader codeReader = CreateCodeReader(memory, out CodeMemoryStream emulatedMemoryStream);
        using CodeMemoryStream codeMemoryStream = emulatedMemoryStream;
        Decoder decoder = InitializeDecoder(codeReader, startAddress);
        int byteOffset = 0;
        codeMemoryStream.Position = startAddress;
        var instructions = new List<CpuInstructionInfo>();
        while (instructions.Count < numberOfInstructionsShown) {
            long instructionAddress = codeMemoryStream.Position;
            decoder.Decode(out Instruction instruction);
            CpuInstructionInfo instructionInfo = new() {
                Instruction = instruction,
                Address = (uint)instructionAddress,
                FunctionName = Functions.FirstOrDefault(x => x.Address == instructionAddress)?.Name,
                AddressInformation = $"{instructionAddress} (0x{state.CS:x4}:{(ushort)(state.IP + byteOffset):X4})",
                Length = instruction.Length,
                IP16 = instruction.IP16,
                IP32 = instruction.IP32,
                MemorySegment = instruction.MemorySegment,
                SegmentPrefix = instruction.SegmentPrefix,
                IsStackInstruction = instruction.IsStackInstruction,
                IsIPRelativeMemoryOperand = instruction.IsIPRelativeMemoryOperand,
                IPRelativeMemoryAddress = instruction.IPRelativeMemoryAddress,
                FlowControl = instruction.FlowControl,
                Bytes = $"{Convert.ToHexString(memory.GetData((uint)instructionAddress, (uint)instruction.Length))} ({instruction.Length})"
            };
            instructionInfo.SegmentedAddress = new(state.CS, (ushort)(state.IP + byteOffset));
            instructionInfo.HasBreakpoint = _breakpointsViewModel.HasUserExecutionBreakpoint(instructionInfo);
            instructionInfo.StringRepresentation =
                $"{instructionInfo.Address:X4} ({instructionInfo.SegmentedAddress}): {instruction} ({instructionInfo.Bytes})";
            if (instructionAddress == state.IpPhysicalAddress) {
                instructionInfo.IsCsIp = true;
            }

            instructions.Add(instructionInfo);
            byteOffset += instruction.Length;
        }

        return instructions;
    }
    
    private void RequestPause(BreakPoint breakPoint) {
        string message = $"{breakPoint.BreakPointType} breakpoint was reached.";
        _pauseHandler.RequestPause(message);
        _uiDispatcher.Post(() => {
            _messenger.Send(new StatusMessage(DateTime.Now, this, message));
        });
    }

    private void UpdateDisassemblyInternal() {
        _uiDispatcher.Post(() => {
            if (UpdateDisassemblyCommand.CanExecute(null)) {
                UpdateDisassemblyCommand.Execute(null);
            }
        });
    }

    [RelayCommand]
    private void MoveCsIpHere() {
        if (SelectedInstruction is null) {
            return;
        }
        _state.CS = SelectedInstruction.SegmentedAddress.Segment;
        _state.IP = SelectedInstruction.SegmentedAddress.Offset;
        UpdateDisassemblyInternal();
    }

    private bool RemoveExecutionBreakpointHereCanExecute() =>
        SelectedInstruction is not null && _breakpointsViewModel.HasUserExecutionBreakpoint(SelectedInstruction);

    [RelayCommand(CanExecute = nameof(RemoveExecutionBreakpointHereCanExecute))]
    private void RemoveExecutionBreakpointHere() {
        if (SelectedInstruction is null) {
            return;
        }
        _breakpointsViewModel.RemoveUserExecutionBreakpoint(SelectedInstruction);
        SelectedInstruction.HasBreakpoint = _breakpointsViewModel.HasUserExecutionBreakpoint(SelectedInstruction);
    }

    private bool CreateExecutionBreakpointHereCanExecute() =>
        SelectedInstruction is not null && !_breakpointsViewModel.HasUserExecutionBreakpoint(SelectedInstruction);
    
    [RelayCommand(CanExecute = nameof(CreateExecutionBreakpointHereCanExecute))]
    private void CreateExecutionBreakpointHere() {
        if (SelectedInstruction is null) {
            return;
        }
        AddressBreakPoint breakPoint = new(
            BreakPointType.EXECUTION,
            SelectedInstruction.Address,
            (breakpoint) => {
                RequestPause(breakpoint);
                UpdateDisassemblyInternal();
                },
            isRemovedOnTrigger: false);
        _breakpointsViewModel.AddAddressBreakpoint(breakPoint);
        SelectedInstruction.HasBreakpoint = _breakpointsViewModel.HasUserExecutionBreakpoint(SelectedInstruction);
    }

    private static Decoder InitializeDecoder(CodeReader codeReader, uint currentIp) {
        Decoder decoder = Decoder.Create(16, codeReader, currentIp,
            DecoderOptions.Loadall286 | DecoderOptions.Loadall386);
        return decoder;
    }

    private static CodeReader CreateCodeReader(IMemory memory, out CodeMemoryStream codeMemoryStream) {
        codeMemoryStream = new CodeMemoryStream(memory);
        CodeReader codeReader = new StreamCodeReader(codeMemoryStream);
        return codeReader;
    }
}
