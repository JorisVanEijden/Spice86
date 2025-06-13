namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Iced.Intel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Models.Debugging;
using Spice86.Shared.Emulator.Memory;

/// <summary>
///     View model for a line in the debugger.
/// </summary>
public partial class DebuggerLineViewModel : ViewModelBase {
    private readonly BreakpointsViewModel? _breakpointsViewModel;

    private readonly Formatter _formatter = new MasmFormatter(new FormatterOptions {
        AddLeadingZeroToHexNumbers = false,
        AlwaysShowSegmentRegister = true,
        HexPrefix = "0x",
        MasmSymbolDisplInBrackets = false
    });

    private readonly Instruction _instruction;
    private readonly List<FormattedTextSegment>? _customFormattedInstruction;

    [ObservableProperty]
    private BreakpointViewModel? _breakpoint;

    [ObservableProperty]
    private bool _isCurrentInstruction;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isGapLine;

    [ObservableProperty]
    private bool _isSegmentStartLine;

    [ObservableProperty]
    private string _gapDescription = string.Empty;

    [ObservableProperty]
    private bool _willJump;

    /// <summary>
    /// Gets the length of the instruction in bytes.
    /// </summary>
    public int InstructionLength => IsGapLine || IsSegmentStartLine ? 0 : _instruction.Length;

    /// <summary>
    /// Creates a regular instruction line for the debugger view.
    /// </summary>
    public DebuggerLineViewModel(EnrichedInstruction instruction, BreakpointsViewModel? breakpointsViewModel = null) {
        _instruction = instruction.Instruction;
        _breakpointsViewModel = breakpointsViewModel;
        ByteString = string.Join(' ', instruction.Bytes.Select(b => b.ToString("X2")));
        Function = instruction.Function;
        SegmentedAddress = instruction.SegmentedAddress;
        Address = SegmentedAddress.Linear;
        NextExecutionAddress = new SegmentedAddress(SegmentedAddress.Segment, (ushort)(SegmentedAddress.Offset + _instruction.Length));
        IsGapLine = _instruction.FlowControl == FlowControl.Return;

        _customFormattedInstruction = instruction.InstructionFormatOverride;

        // We expect there to be at most 1 execution breakpoint per line, so we use SingleOrDefault.
        Breakpoint = instruction.Breakpoints.SingleOrDefault(breakpoint => breakpoint.Type == BreakPointType.CPU_EXECUTION_ADDRESS);

        // Generate the formatted disassembly text
        GenerateFormattedDisassembly();
    }

    /// <summary>
    /// Creates a gap line for the debugger view.
    /// </summary>
    /// <param name="currentLine"></param>
    /// <param name="nextLine"></param>
    public DebuggerLineViewModel(DebuggerLineViewModel currentLine, DebuggerLineViewModel nextLine) {
        _instruction = new Instruction();
        ByteString = string.Empty;
        Function = null;
        SegmentedAddress = currentLine.SegmentedAddress;
        Address = SegmentedAddress.Linear;
        NextExecutionAddress = SegmentedAddress;
        IsGapLine = true;

        // Calculate the gap size based on instruction length
        uint expectedNextAddress = currentLine.Address + (uint)currentLine.InstructionLength;
        long gapSize = nextLine.Address - expectedNextAddress;
        GapDescription = $"--- Gap of {gapSize} (0x{gapSize:X}) bytes ---";

        // Create custom formatted instruction for the gap line
        _customFormattedInstruction = [
            new FormattedTextSegment {
                Text = GapDescription,
                Kind = FormatterTextKind.Text
            }
        ];

        GenerateFormattedDisassembly();
    }

    /// <summary>
    /// Creates a segment start line for the debugger view.
    /// </summary>
    /// <param name="segmentAddress">The segmented address at the start of the segment</param>
    public DebuggerLineViewModel(SegmentedAddress segmentAddress) {
        _instruction = new Instruction();
        ByteString = string.Empty;
        Function = null;
        SegmentedAddress = segmentAddress;
        Address = segmentAddress.Linear;
        NextExecutionAddress = segmentAddress;
        IsSegmentStartLine = true;
        GapDescription = $"--- Start of segment {segmentAddress.Segment:X4} ---";

        // Create custom formatted instruction for the segment start line
        _customFormattedInstruction = [
            new FormattedTextSegment {
                Text = GapDescription,
                Kind = FormatterTextKind.Text
            }
        ];

        // Generate the formatted disassembly text
        GenerateFormattedDisassembly();
    }

    public string ByteString { get; }
    public FunctionInformation? Function { get; }
    public SegmentedAddress SegmentedAddress { get; }

    /// <summary>
    ///     The physical address of this instruction in memory.
    /// </summary>
    public uint Address { get; }

    public bool CanBeSteppedOver => _instruction.IsLoop || _instruction.FlowControl is FlowControl.Call or FlowControl.Interrupt or FlowControl.IndirectCall;

    [ObservableProperty]
    private SegmentedAddress _nextExecutionAddress;

    public string Disassembly => _customFormattedInstruction != null ? string.Join(' ', _customFormattedInstruction.Select(segment => segment.Text)) : _instruction.ToString();

    /// <summary>
    ///     Gets a collection of formatted text segments for the disassembly with syntax highlighting.
    /// </summary>
    public List<FormattedTextSegment> DisassemblySegments { get; private set; } = [];

    /// <summary>
    ///     Generates a formatted representation of the disassembly with syntax highlighting.
    /// </summary>
    private void GenerateFormattedDisassembly() {
        if (_customFormattedInstruction != null) {
            // Use custom formatting for special opcodes
            DisassemblySegments = _customFormattedInstruction;
        } else {
            // Use standard Iced formatting for normal instructions
            var output = new FormattedTextSegmentsOutput();
            _formatter.Format(_instruction, output);
            DisassemblySegments = output.Segments;
        }
    }
    public void ApplyCpuState(State cpuState, IMemory memory) {
        // Use the NextExecutionAddressCalculator to determine the next execution address
        var calculator = new NextExecutionAddressCalculator(_instruction, SegmentedAddress);
        NextExecutionAddress = calculator.Calculate(cpuState, memory);

        // Set the WillJump property based on whether the instruction will branch
        // This is used for UI display purposes
        WillJump = NextExecutionAddress.Linear != SegmentedAddress.Linear + _instruction.Length;
    }

    /// <summary>
    ///     Updates the Breakpoint property with the current value from the BreakpointsViewModel.
    /// </summary>
    public void UpdateBreakpointFromViewModel() {
        if (_breakpointsViewModel != null) {
            Breakpoint = _breakpointsViewModel.GetExecutionBreakPointsAtAddress(Address).FirstOrDefault();
        }
    }

    public override string ToString() {
        if (IsGapLine || IsSegmentStartLine) {
            return GapDescription;
        }

        return $"{SegmentedAddress} {Disassembly} [{ByteString}]";
    }
}