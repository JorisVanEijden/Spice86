namespace Spice86.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using Iced.Intel;

using Spice86.Core.Emulator.Function;
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

    private readonly Instruction _info;
    private readonly List<FormattedTextSegment>? _customFormattedInstruction;

    [ObservableProperty]
    private BreakpointViewModel? _breakpoint;

    [ObservableProperty]
    private bool _isCurrentInstruction;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool? _willJump;

    [ObservableProperty]
    private bool _isGapLine;

    [ObservableProperty]
    private bool _isSegmentStartLine;

    [ObservableProperty]
    private string _gapDescription = string.Empty;

    /// <summary>
    /// Creates a regular instruction line for the debugger view.
    /// </summary>
    public DebuggerLineViewModel(EnrichedInstruction instruction, BreakpointsViewModel? breakpointsViewModel = null) {
        _info = instruction.Instruction;
        _breakpointsViewModel = breakpointsViewModel;
        ByteString = string.Join(' ', instruction.Bytes.Select(b => b.ToString("X2")));
        Function = instruction.Function;
        SegmentedAddress = instruction.SegmentedAddress;
        Address = SegmentedAddress.Linear;
        NextAddress = (uint)(Address + _info.Length);
        _customFormattedInstruction = instruction.FormattedInstruction;

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
        _info = new Instruction();
        ByteString = string.Empty;
        Function = null;
        SegmentedAddress = currentLine.SegmentedAddress;
        Address = SegmentedAddress.Linear;
        NextAddress = Address;
        IsGapLine = true;

        // Calculate the gap size
        long gapSize = nextLine.Address - currentLine.Address;
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
        _info = new Instruction();
        ByteString = string.Empty;
        Function = null;
        SegmentedAddress = segmentAddress;
        Address = segmentAddress.Linear;
        NextAddress = Address;
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

    public bool ContinuesToNextInstruction => _info.FlowControl == FlowControl.Next;
    public bool CanBeSteppedOver => _info.FlowControl is FlowControl.Call or FlowControl.IndirectCall or FlowControl.Interrupt;
    public uint NextAddress { get; private set; }

    public string Disassembly => _customFormattedInstruction != null ? string.Join(' ', _customFormattedInstruction.Select(segment => segment.Text)) : _info.ToString();

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
            _formatter.Format(_info, output);
            DisassemblySegments = output.Segments;
        }
    }

    /// <summary>
    ///     Gets the current breakpoint for this line from the BreakpointsViewModel.
    /// </summary>
    /// <returns>The breakpoint if one exists for this address, otherwise null.</returns>
    public BreakpointViewModel? GetBreakpointFromViewModel() {
        // Find a breakpoint in the BreakpointsViewModel that matches this line's address
        return _breakpointsViewModel?.Breakpoints.FirstOrDefault(bp => bp.Type == BreakPointType.CPU_EXECUTION_ADDRESS && (uint)bp.Address == Address);
    }

    /// <summary>
    ///     Updates the Breakpoint property with the current value from the BreakpointsViewModel.
    /// </summary>
    public void UpdateBreakpointFromViewModel() {
        if (_breakpointsViewModel != null) {
            Breakpoint = GetBreakpointFromViewModel();
        }
    }

    public override string ToString() {
        if (IsGapLine || IsSegmentStartLine) {
            return GapDescription;
        }

        return $"{SegmentedAddress} {Disassembly} [{ByteString}]";
    }
}