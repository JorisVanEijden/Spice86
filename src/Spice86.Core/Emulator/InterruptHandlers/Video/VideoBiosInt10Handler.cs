namespace Spice86.Core.Emulator.InterruptHandlers.Video;

using Serilog;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Video;
using Spice86.Core.Emulator.Video.Modes;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Utils;
using Spice86.Logging;
using Spice86.Shared;

using System.Runtime.InteropServices;

/// <summary>
/// TODO: Make unused / missing code used.
/// TODO: Remove pointers (or at least use a private fixed pointer for VideoMemory, like in Memory).
/// TODO: Keep this overridable, don't put anything in Run method.
/// </summary>
public class VideoBiosInt10Handler : InterruptHandler {
    public const int BiosVideoMode = 0x49;
    public static readonly uint BIOS_VIDEO_MODE_ADDRESS = MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataAreaSegment, BiosVideoMode);
    public static readonly uint CRT_IO_PORT_ADDRESS_IN_RAM = MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataAreaSegment, MemoryMap.BiosDataAreaOffsetCrtIoPort);
    private readonly ILogger _logger;
    private readonly byte _currentDisplayPage = 0;
    private readonly byte _numberOfScreenColumns = 80;
    private static readonly long HorizontalBlankingTime = HorizontalPeriod / 2;
    private static readonly long HorizontalPeriod = (long)((1000.0 / 60.0) / 480.0 * Pic.StopwatchTicksPerMillisecond);
    private static readonly long RefreshRate = (long)((1000.0 / 60.0) * Pic.StopwatchTicksPerMillisecond);
    private static readonly long VerticalBlankingTime = RefreshRate / 40;
    private readonly VgaCard _vgaCard;

    /// <summary>
    /// Total number of bytes allocated for video RAM.
    /// </summary>
    public const int TotalVramBytes = 1024 * 1024;

    /// <summary>
    /// Segment of the VGA static functionality table.
    /// </summary>
    public const ushort StaticFunctionalityTableSegment = 0x0100;

    public byte[] VideoRam { get; private set; }

    public VideoBiosInt10Handler(Machine machine, ILogger logger, VgaCard vgaCard) : base(machine) {
        _logger = logger;
        _vgaCard = vgaCard;
        this.VideoRam = new byte[TotalVramBytes];
        unsafe {
            fixed (byte* ramPtr = this.VideoRam) {
                this.RawView = ramPtr;
            }
        }
        FillDispatchTable();
        Memory memory = machine.Memory;
        memory.SetUInt32(StaticFunctionalityTableSegment, 0, 0x000FFFFF); // supports all video modes
        memory.SetByte(StaticFunctionalityTableSegment, 0x07, 0x07); // supports all scanlines
        this.TextConsole = new TextConsole(this, machine.Memory.Bios);

    }

    /// <summary>
    /// Gets a pointer to the emulated video RAM.
    /// </summary>
    public unsafe byte* RawView { get; private set; }
    public Machine Machine => _machine;

    public void GetBlockOfDacColorRegisters() {
        ushort firstRegisterToGet = _state.BX;
        ushort numberOfColorsToGet = _state.CX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("GET BLOCKS OF DAC COLOR REGISTERS. First register is {@FirstRegisterToGet}, getting {@NumberOfColorsToGet} colors, values are to be stored at address {@EsDx}", ConvertUtils.ToHex(firstRegisterToGet), numberOfColorsToGet, ConvertUtils.ToSegmentedAddressRepresentation(_state.ES, _state.DX));
        }

        uint colorValuesAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DX);
        _vgaCard.GetBlockOfDacColorRegisters(firstRegisterToGet, numberOfColorsToGet, colorValuesAddress);
    }

    /// <summary>
    /// Gets the VGA attribute controller.
    /// </summary>
    public AttributeController AttributeController { get; } = new AttributeController();

    /// <summary>
    /// Gets the VGA CRT controller.
    /// </summary>
    public CrtController CrtController { get; } = new CrtController();

    /// <summary>
    /// Gets the VGA graphics controller.
    /// </summary>
    public Graphics Graphics { get; } = new Graphics();

    /// <summary>
    /// Gets the VGA sequencer.
    /// </summary>
    public Sequencer Sequencer { get; } = new Sequencer();

    /// <summary>
    /// Gets the text-mode display instance.
    /// </summary>
    public TextConsole TextConsole { get; }

    public override byte Index => 0x10;

    public void GetSetPaletteRegisters() {
        byte op = _state.AL;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("GET/SET PALETTE REGISTERS {@Operation}", ConvertUtils.ToHex8(op));
        }

        if (op == 0x12) {
            SetBlockOfDacColorRegisters();
        } else if (op == 0x17) {
            GetBlockOfDacColorRegisters();
        } else {
            throw new UnhandledOperationException(_machine, $"Unhandled operation for get/set palette registers op={ConvertUtils.ToHex8(op)}");
        }
    }

    public byte VideoModeValue {
        get {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("GET VIDEO MODE");
            }
            return _memory.GetUint8(BIOS_VIDEO_MODE_ADDRESS);
        }
    }

    public void GetVideoStatus() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Debug)) {
            _logger.Debug("GET VIDEO STATUS");
        }
        _state.AH = _numberOfScreenColumns;
        _state.AL = VideoModeValue;
        _state.BH = _currentDisplayPage;
    }

    public void InitRam() {
        SetVideoModeValue((byte)VideoMode10h.Text80x25x1);
        _memory.SetUint16(CRT_IO_PORT_ADDRESS_IN_RAM, VideoPorts.CrtControllerAddressAlt);
    }

    public override void Run() {
        byte operation = _state.AH;
        Run(operation);
    }

    public void ScrollPageUp() {
        byte scrollAmount = _state.AL;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SCROLL PAGE UP BY AMOUNT {@ScrollAmount}", ConvertUtils.ToHex8(scrollAmount));
        }
    }

    public void SetBlockOfDacColorRegisters() {
        ushort firstRegisterToSet = _state.BX;
        ushort numberOfColorsToSet = _state.CX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET BLOCKS OF DAC COLOR REGISTERS. First register is {@FirstRegisterToSet}, setting {@NumberOfColorsToSet} colors, values are from address {@EsDx}", ConvertUtils.ToHex(firstRegisterToSet), numberOfColorsToSet, ConvertUtils.ToSegmentedAddressRepresentation(_state.ES, _state.DX));
        }

        uint colorValuesAddress = MemoryUtils.ToPhysicalAddress(_state.ES, _state.DX);
        _vgaCard.SetBlockOfDacColorRegisters(firstRegisterToSet, numberOfColorsToSet, colorValuesAddress);
    }

    public void SetColorPalette() {
        byte colorId = _state.BH;
        byte colorValue = _state.BL;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET COLOR PALETTE {@ColorId}, {@ColorValue}", colorId, colorValue);
        }
    }

    public void SetCursorPosition() {
        byte cursorPositionRow = _state.DH;
        byte cursorPositionColumn = _state.DL;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET CURSOR POSITION, {@Row}, {@Column}", ConvertUtils.ToHex8(cursorPositionRow), ConvertUtils.ToHex8(cursorPositionColumn));
        }
    }

    public void SetCursorType() {
        ushort cursorStartEnd = _state.CX;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET CURSOR TYPE, SCAN LINE START END IS {@CursorStartEnd}", ConvertUtils.ToHex(cursorStartEnd));
        }
    }

    public void SetVideoMode() {
        byte videoMode = _state.AL;
        SetVideoModeValue(videoMode);
    }

    public void SetVideoModeValue(byte mode) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET VIDEO MODE {@VideoMode}", ConvertUtils.ToHex8(mode));
        }
        _memory.SetUint8(BIOS_VIDEO_MODE_ADDRESS, mode);
        _vgaCard.SetVideoModeValue(mode);
    }

    public void WriteTextInTeletypeMode() {
        byte chr = _state.AL;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Write Text in Teletype Mode ascii code {@AsciiCode}, chr {@Character}", ConvertUtils.ToHex(chr), ConvertUtils.ToChar(chr));
        }
        Console.Out.Write(ConvertUtils.ToChar(chr));
    }

    private void FillDispatchTable() {
        //TODO: Replace values by Functions enums (VgaFunctions, EgaFunctions...) from VideoController.HandleInterrupt
        //TODO: Move CtrController and other controllers to the VgaCard
        //TODO: Extended VgaCard to implement all ReasdByte/WriteByte code for Input and Output ports
        _dispatchTable.Add(0x00, new Callback(0x00, SetVideoMode));
        _dispatchTable.Add(0x01, new Callback(0x01, SetCursorType));
        _dispatchTable.Add(0x02, new Callback(0x02, SetCursorPosition));
        _dispatchTable.Add(0x06, new Callback(0x06, ScrollPageUp));
        _dispatchTable.Add(0x0B, new Callback(0x0B, SetColorPalette));
        _dispatchTable.Add(0x0E, new Callback(0x0E, WriteTextInTeletypeMode));
        _dispatchTable.Add(0x0F, new Callback(0x0F, GetVideoStatus));
        _dispatchTable.Add(0x10, new Callback(0x10, GetSetPaletteRegisters));
        _dispatchTable.Add(0x12, new Callback(0x12, GiveVideoSubsystemConfigurationInCpuState));
        _dispatchTable.Add(0x1A, new Callback(0x1A, VideoDisplayCombination));
    }

    private void VideoDisplayCombination() {
        byte op = _state.AL;
        switch (op) {
            case 0:
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("GET VIDEO DISPLAY COMBINATION");
                }
                // VGA with analog color display
                _state.BX = 0x08;
                break;
            case 1:
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("SET VIDEO DISPLAY COMBINATION");
                }
                throw new UnhandledOperationException(_machine, "Unimplemented");
            default:
                throw new UnhandledOperationException(_machine,
                    $"Unhandled operation for videoDisplayCombination op={ConvertUtils.ToHex8(op)}");
        }
        _state.AL = 0x1A;
        _state.AH = 0x00;
    }

    private void GiveVideoSubsystemConfigurationInCpuState() {
        byte op = _state.BL;
        switch (op) {
            case 0x0:
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("UNKNOWN!");
                }
                break;
            case 0x10:
                if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                    _logger.Information("GET VIDEO CONFIGURATION INFORMATION");
                }
                // color
                _state.BH = 0;
                // 64k of vram
                _state.BL = 0;
                // From dosbox source code ...
                _state.CH = 0;
                _state.CL = 0x09;
                break;
            default:
                throw new UnhandledOperationException(_machine,
                    $"Unhandled operation for videoSubsystemConfiguration op={ConvertUtils.ToHex8(op)}");
        }
    }

    /// <summary>
    /// Gets the current display mode.
    /// </summary>
    public VideoMode? CurrentMode { get; private set; }

    /// <summary>
    /// Reads a byte from video RAM.
    /// </summary>
    /// <param name="offset">Offset of byte to read.</param>
    /// <returns>Byte read from video RAM.</returns>
    public byte GetVramByte(uint offset) => this.CurrentMode?.GetVramByte(offset) ?? 0;
    /// <summary>
    /// Sets a byte in video RAM to a specified value.
    /// </summary>
    /// <param name="offset">Offset of byte to set.</param>
    /// <param name="value">Value to write.</param>
    public void SetVramByte(uint offset, byte value) => this.CurrentMode?.SetVramByte(offset, value);
    /// <summary>
    /// Reads a word from video RAM.
    /// </summary>
    /// <param name="offset">Offset of word to read.</param>
    /// <returns>Word read from video RAM.</returns>
    public ushort GetVramWord(uint offset) => this.CurrentMode?.GetVramWord(offset) ?? 0;
    /// <summary>
    /// Sets a word in video RAM to a specified value.
    /// </summary>
    /// <param name="offset">Offset of word to set.</param>
    /// <param name="value">Value to write.</param>
    public void SetVramWord(uint offset, ushort value) => this.CurrentMode?.SetVramWord(offset, value);
    /// <summary>
    /// Reads a doubleword from video RAM.
    /// </summary>
    /// <param name="offset">Offset of doubleword to read.</param>
    /// <returns>Doubleword read from video RAM.</returns>
    public uint GetVramDWord(uint offset) => this.CurrentMode?.GetVramDWord(offset) ?? 0;
    /// <summary>
    /// Sets a doubleword in video RAM to a specified value.
    /// </summary>
    /// <param name="offset">Offset of doubleword to set.</param>
    /// <param name="value">Value to write.</param>
    public void SetVramDWord(uint offset, uint value) => this.CurrentMode?.SetVramDWord(offset, value);

    /// <summary>
    /// Returns the current value of the input status 1 register.
    /// </summary>
    /// <returns>Current value of the input status 1 register.</returns>
    private static byte GetInputStatus1Value() {
        uint value = DualPic.IsInRealtimeInterval(VerticalBlankingTime, RefreshRate) ? 0x09u : 0x00u;
        if (DualPic.IsInRealtimeInterval(HorizontalBlankingTime, HorizontalPeriod)) {
            value |= 0x01u;
        }

        return (byte)value;
    }

    /// <summary>
    /// Changes the current video mode to match the new value of the vertical end register.
    /// </summary>
    private void ChangeVerticalEnd() {
        // this is a hack
        int newEnd = this.CrtController.VerticalDisplayEnd | ((this.CrtController.Overflow & (1 << 1)) << 7) | ((this.CrtController.Overflow & (1 << 6)) << 3);
        if (this.CurrentMode is Unchained256) {
            newEnd /= 2;
        } else {
            newEnd = newEnd switch {
                223 => 480,
                184 => 440,
                _ => newEnd * 2
            };
        }
        if(this.CurrentMode is not null) {
            this.CurrentMode.Height = newEnd;
        }
        _machine.OnVideoModeChanged(EventArgs.Empty);
    }

    /// <summary>
    /// Sets the current mode to unchained mode 13h.
    /// </summary>
    private void EnterModeX() {
        var mode = new Unchained256(320, 200, this);
        CrtController.Offset = 320 / 8;
        this.CurrentMode = mode;
        _machine.OnVideoModeChanged(EventArgs.Empty);
    }

    /// <summary>
    /// Gets information about BIOS fonts.
    /// </summary>
    private void GetFontInfo() {
        if(CurrentMode is null) {
            return;
        }
        SegmentedAddress address = this._machine.Cpu.State.BH switch {
            0x00 => this._machine.Memory.GetRealModeInterruptAddress(0x1F),
            0x01 => this._machine.Memory.GetRealModeInterruptAddress(0x43),
            0x02 or 0x05 => new SegmentedAddress(Memory.FontSegment, Memory.Font8x14Offset),
            0x03 => new SegmentedAddress(Memory.FontSegment, Memory.Font8x8Offset),
            0x04 => new SegmentedAddress(Memory.FontSegment, Memory.Font8x8Offset + 128 * 8),
            _ => new SegmentedAddress(Memory.FontSegment, Memory.Font8x16Offset),
        };

        this._machine.Cpu.State.ES = address.Segment;
        this._machine.Cpu.State.BP = address.Offset;
        this._machine.Cpu.State.CX = (ushort)this.CurrentMode.FontHeight;
        this._machine.Cpu.State.DL = this._machine.Memory.Bios.ScreenRows;
    }

    /// <summary>
    /// Writes a table of information about the current video mode.
    /// </summary>
    private void GetFunctionalityInfo() {
        if (CurrentMode is null) {
            return;
        }
        ushort segment = _machine.Cpu.State.ES;
        ushort offset = _machine.Cpu.State.DI;

        Memory memory = _machine.Memory;
        Bios bios = memory.Bios;

        Point cursorPos = TextConsole.CursorPosition;

        memory.SetUInt32(segment, offset, StaticFunctionalityTableSegment << 16); // SFT address
        memory.SetByte(segment, offset + 0x04u, (byte)bios.VideoMode); // video mode
        memory.SetUInt16(segment, offset + 0x05u, bios.ScreenColumns); // columns
        memory.SetUInt32(segment, offset + 0x07u, 0); // regen buffer
        for (uint i = 0; i < 8; i++) {
            memory.SetByte(segment, offset + 0x0Bu + i * 2u, (byte)cursorPos.X); // text cursor x
            memory.SetByte(segment, offset + 0x0Cu + i * 2u, (byte)cursorPos.Y); // text cursor y
        }

        memory.SetUInt16(segment, offset + 0x1Bu, 0); // cursor type
        memory.SetByte(segment, offset + 0x1Du, (byte)this.CurrentMode.ActiveDisplayPage); // active display page
        memory.SetUInt16(segment, offset + 0x1Eu, bios.CrtControllerBaseAddress); // CRTC base address
        memory.SetByte(segment, offset + 0x20u, 0); // current value of port 3x8h
        memory.SetByte(segment, offset + 0x21u, 0); // current value of port 3x9h
        memory.SetByte(segment, offset + 0x22u, bios.ScreenRows); // screen rows
        memory.SetUInt16(segment, offset + 0x23u, (ushort)this.CurrentMode.FontHeight); // bytes per character
        memory.SetByte(segment, offset + 0x25u, (byte)bios.VideoMode); // active display combination code
        memory.SetByte(segment, offset + 0x26u, (byte)bios.VideoMode); // alternate display combination code
        memory.SetUInt16(segment, offset + 0x27u, (ushort)(this.CurrentMode.BitsPerPixel * 8)); // number of colors supported in current mode
        memory.SetByte(segment, offset + 0x29u, 4); // number of pages
        memory.SetByte(segment, offset + 0x2Au, 0); // number of active scanlines

        // Indicate success.
        _machine.Cpu.State.AL = 0x1B;
    }

    /// <summary>
    /// Writes values to the static functionality table in emulated memory.
    /// </summary>
    private void InitializeStaticFunctionalityTable() {
        Memory memory = _machine.Memory;
        memory.SetUInt32(StaticFunctionalityTableSegment, 0, 0x000FFFFF); // supports all video modes
        memory.SetByte(StaticFunctionalityTableSegment, 0x07, 0x07); // supports all scanlines
    }

    /// <summary>
    /// Reads DAC color registers to emulated RAM.
    /// </summary>
    private void ReadDacRegisters() {
        ushort segment = _machine.Cpu.State.ES;
        uint offset = (ushort)_machine.Cpu.State.DX;
        int start = _machine.Cpu.State.BX;
        int count = _machine.Cpu.State.CX;

        for (int i = start; i < count; i++) {
            uint r = (_vgaCard.VgaDac.Palette[start + i] >> 18) & 0xCFu;
            uint g = (_vgaCard.VgaDac.Palette[start + i] >> 10) & 0xCFu;
            uint b = (_vgaCard.VgaDac.Palette[start + i] >> 2) & 0xCFu;

            _machine.Memory.SetByte(segment, offset, (byte)r);
            _machine.Memory.SetByte(segment, offset + 1u, (byte)g);
            _machine.Memory.SetByte(segment, offset + 2u, (byte)b);

            offset += 3u;
        }
    }

    /// <summary>
    /// Sets all of the EGA color palette registers to values in emulated RAM.
    /// </summary>
    private void SetAllEgaPaletteRegisters() {
        ushort segment = _machine.Cpu.State.ES;
        uint offset = (ushort)_machine.Cpu.State.DX;

        for (uint i = 0; i < 16u; i++) {
            SetEgaPaletteRegister((int)i, _machine.Memory.GetByte(segment, offset + i));
        }
    }

    /// <summary>
    /// Changes the appearance of the text-mode cursor.
    /// </summary>
    /// <param name="topOptions">Top scan line and options.</param>
    /// <param name="bottom">Bottom scan line.</param>
    private void SetCursorShape(int topOptions, int bottom) {
        int mode = (topOptions >> 4) & 3;
        _machine.IsCursorVisible = mode != 2;
    }

    /// <summary>
    /// Sets DAC color registers to values in emulated RAM.
    /// </summary>
    private void SetDacRegisters() {
        ushort segment = _machine.Cpu.State.ES;
        uint offset = (ushort)_machine.Cpu.State.DX;
        int start = _machine.Cpu.State.BX;
        int count = _machine.Cpu.State.CX;

        for (int i = start; i < count; i++) {
            byte r = _machine.Memory.GetByte(segment, offset);
            byte g = _machine.Memory.GetByte(segment, offset + 1u);
            byte b = _machine.Memory.GetByte(segment, offset + 2u);

            _machine.VgaCard.VgaDac.SetColor((byte)(start + i), r, g, b);

            offset += 3u;
        }
    }

    /// <summary>
    /// Gets a specific EGA color palette register.
    /// </summary>
    /// <param name="index">Index of color to set.</param>
    /// <param name="color">New value of the color.</param>
    private void SetEgaPaletteRegister(int index, byte color) {
        if (_machine.Memory.Bios.VideoMode == VideoMode10h.ColorGraphics320x200x4) {
            AttributeController.InternalPalette[index & 0x0F] = (byte)(color & 0x0F);
        } else {
            AttributeController.InternalPalette[index & 0x0F] = color;
        }
    }

    /// <summary>
    /// Sets the current mode to text mode 80x50.
    /// </summary>
    private void SwitchTo80x50TextMode() {
        var mode = new TextMode(80, 50, 8, this);
        this.CurrentMode = mode;
        _machine.OnVideoModeChanged(EventArgs.Empty);
    }
}
