namespace Spice86.Core.Emulator.InterruptHandlers.Video;

using Serilog;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.InterruptHandlers;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Video;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Utils;
using Spice86.Logging;
using Spice86.Shared;

/// <summary>
/// TODO: Make unused / missing code used.
/// TODO: Remove pointers (or at least use a private fixed pointer for VideoMemory, like in Memory).
/// TODO: Keep this overridable, don't put anything in Run method.
/// </summary>
public class VideoBiosInt10Handler : InterruptHandler, IDisposable {
    public const int BiosVideoMode = 0x49;
    public static readonly uint BIOS_VIDEO_MODE_ADDRESS = MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataAreaSegment, BiosVideoMode);
    public static readonly uint CRT_IO_PORT_ADDRESS_IN_RAM = MemoryUtils.ToPhysicalAddress(MemoryMap.BiosDataAreaSegment, MemoryMap.BiosDataAreaOffsetCrtIoPort);
    private readonly VgaCard _vgaCard;
    private readonly ILogger _logger;
    private readonly byte _currentDisplayPage = 0;
    private bool _disposed;
    private byte _numberOfScreenColumns = 80;
    private static readonly long HorizontalBlankingTime = HorizontalPeriod / 2;
    private static readonly long HorizontalPeriod = (long)((1000.0 / 60.0) / 480.0 * Pic.StopwatchTicksPerMillisecond);
    private static readonly long RefreshRate = (long)((1000.0 / 60.0) * Pic.StopwatchTicksPerMillisecond);
    private static readonly long VerticalBlankingTime = RefreshRate / 40;

    public VideoBiosInt10Handler(Machine machine, ILogger logger, VgaCard vgaCard) : base(machine) {
        _logger = logger;
        _vgaCard = vgaCard;
        FillDispatchTable();
        InitializeStaticFunctionalityTable();
    }

    public bool IsDisposed => _disposed;

    /// <summary>
    /// Writes values to the static functionality table in emulated memory.
    /// </summary>
    private void InitializeStaticFunctionalityTable() {
        Memory memory = _memory;
        memory.SetUInt32(VgaCard.StaticFunctionalityTableSegment, 0, 0x000FFFFF); // supports all video modes
        memory.SetByte(VgaCard.StaticFunctionalityTableSegment, 0x07, 0x07); // supports all scanlines
    }


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
    /// Gets information about BIOS fonts.
    /// </summary>
    private void GetFontInfo() {
        if (_vgaCard.CurrentMode is null) {
            return;
        }
        SegmentedAddress address = _state.BH switch {
            0x00 => _memory.GetRealModeInterruptAddress(0x1F),
            0x01 => _memory.GetRealModeInterruptAddress(0x43),
            0x02 or 0x05 => new SegmentedAddress(Memory.FontSegment, Memory.Font8x14Offset),
            0x03 => new SegmentedAddress(Memory.FontSegment, Memory.Font8x8Offset),
            0x04 => new SegmentedAddress(Memory.FontSegment, Memory.Font8x8Offset + 128 * 8),
            _ => new SegmentedAddress(Memory.FontSegment, Memory.Font8x16Offset),
        };

        _state.ES = address.Segment;
        _state.BP = address.Offset;
        _state.CX = (ushort)_vgaCard.CurrentMode.FontHeight;
        _state.DL = _memory.Bios.ScreenRows;
    }

    /// <summary>
    /// Writes a table of information about the current video mode.
    /// </summary>
    private void GetFunctionalityInfo() {
        if (_vgaCard.CurrentMode is null) {
            return;
        }
        ushort segment = _state.ES;
        ushort offset = _state.DI;

        Memory memory = _memory;
        Bios bios = memory.Bios;

        Point cursorPos = _vgaCard.TextConsole.CursorPosition;

        memory.SetUInt32(segment, offset, VgaCard.StaticFunctionalityTableSegment << 16); // SFT address
        memory.SetByte(segment, offset + 0x04u, (byte)bios.VideoMode); // video mode
        memory.SetUInt16(segment, offset + 0x05u, bios.ScreenColumns); // columns
        memory.SetUInt32(segment, offset + 0x07u, 0); // regen buffer
        for (uint i = 0; i < 8; i++) {
            memory.SetByte(segment, offset + 0x0Bu + i * 2u, (byte)cursorPos.X); // text cursor x
            memory.SetByte(segment, offset + 0x0Cu + i * 2u, (byte)cursorPos.Y); // text cursor y
        }

        memory.SetUInt16(segment, offset + 0x1Bu, 0); // cursor type
        memory.SetByte(segment, offset + 0x1Du, (byte)_vgaCard.CurrentMode.ActiveDisplayPage); // active display page
        memory.SetUInt16(segment, offset + 0x1Eu, bios.CrtControllerBaseAddress); // CRTC base address
        memory.SetByte(segment, offset + 0x20u, 0); // current value of port 3x8h
        memory.SetByte(segment, offset + 0x21u, 0); // current value of port 3x9h
        memory.SetByte(segment, offset + 0x22u, bios.ScreenRows); // screen rows
        memory.SetUInt16(segment, offset + 0x23u, (ushort)_vgaCard.CurrentMode.FontHeight); // bytes per character
        memory.SetByte(segment, offset + 0x25u, (byte)bios.VideoMode); // active display combination code
        memory.SetByte(segment, offset + 0x26u, (byte)bios.VideoMode); // alternate display combination code
        memory.SetUInt16(segment, offset + 0x27u, (ushort)(_vgaCard.CurrentMode.BitsPerPixel * 8)); // number of colors supported in current mode
        memory.SetByte(segment, offset + 0x29u, 4); // number of pages
        memory.SetByte(segment, offset + 0x2Au, 0); // number of active scanlines

        // Indicate success.
        _state.AL = 0x1B;
    }

    /// <summary>
    /// Reads DAC color registers to emulated RAM.
    /// </summary>
    private void ReadDacRegisters() {
        ushort segment = _state.ES;
        uint offset = (ushort)_state.DX;
        int start = _state.BX;
        int count = _state.CX;

        for (int i = start; i < count; i++) {
            uint r = (_vgaCard.VgaDac.Palette[start + i] >> 18) & 0xCFu;
            uint g = (_vgaCard.VgaDac.Palette[start + i] >> 10) & 0xCFu;
            uint b = (_vgaCard.VgaDac.Palette[start + i] >> 2) & 0xCFu;

            _memory.SetByte(segment, offset, (byte)r);
            _memory.SetByte(segment, offset + 1u, (byte)g);
            _memory.SetByte(segment, offset + 2u, (byte)b);

            offset += 3u;
        }
    }

    /// <summary>
    /// Sets all of the EGA color palette registers to values in emulated RAM.
    /// </summary>
    private void SetAllEgaPaletteRegisters() {
        ushort segment = _state.ES;
        uint offset = _state.DX;

        for (uint i = 0; i < 16u; i++) {
            SetEgaPaletteRegister((int)i,  _memory.GetByte(segment, offset + i));
        }
    }

    /// <summary>
    /// Gets a specific EGA color palette register.
    /// </summary>
    /// <param name="index">Index of color to set.</param>
    /// <param name="color">New value of the color.</param>
    private void SetEgaPaletteRegister(int index, byte color) {
        if (_memory.Bios.VideoMode == VideoMode10h.ColorGraphics320x200x4) {
            _vgaCard.AttributeController.InternalPalette[index & 0x0F] = (byte)(color & 0x0F);
        } else {
            _vgaCard.AttributeController.InternalPalette[index & 0x0F] = color;
        }
    }


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
        _state.AH = _memory.Bios.ScreenColumns;
        _state.AL = (byte)_memory.Bios.VideoMode;
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
        byte foreground = (byte)((_state.BX >> 8) & 0x0F);
        byte background = (byte)((_state.BX >> 12) & 0x0F);
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SCROLL PAGE UP BY AMOUNT {@ScrollAmount}", ConvertUtils.ToHex8(scrollAmount));
        }
        _vgaCard.TextConsole.ScrollTextUp(_state.CL, _state.CH, _state.DL, _state.DH, _state.AL, foreground, background);
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
        _machine.OnVideoModeChanged(EventArgs.Empty);
    }

    public void WriteTextInTeletypeMode() {
        byte chr = _state.AL;
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("Write Text in Teletype Mode ascii code {@AsciiCode}, chr {@Character}", ConvertUtils.ToHex(chr), ConvertUtils.ToChar(chr));
        }
        Console.Out.Write(ConvertUtils.ToChar(chr));
    }

    private void FillDispatchTable() {
        //TODO: Replace and extend values by Functions enums (VgaFunctions, EgaFunctions...) from VideoController.HandleInterrupt
        _dispatchTable.Add(VgaFunctions.SetDisplayMode, new Callback(VgaFunctions.SetDisplayMode, SetVideoMode));
        _dispatchTable.Add(VgaFunctions.SetCursorShape, new Callback(VgaFunctions.SetCursorShape, SetCursorType));
        _dispatchTable.Add(VgaFunctions.SetCursorPosition, new Callback(VgaFunctions.SetCursorPosition, SetCursorPosition));
        _dispatchTable.Add(VgaFunctions.ScrollUpWindow, new Callback(VgaFunctions.ScrollUpWindow, ScrollPageUp));
        _dispatchTable.Add(VgaFunctions.Video, new Callback(VgaFunctions.Video, SetColorPalette));
        _dispatchTable.Add(VgaFunctions.TeletypeOutput, new Callback(VgaFunctions.TeletypeOutput, WriteTextInTeletypeMode));
        _dispatchTable.Add(VgaFunctions.GetDisplayMode, new Callback(VgaFunctions.GetDisplayMode, GetVideoStatus));
        _dispatchTable.Add(VgaFunctions.Palette_SetSingleDacRegister, new Callback(VgaFunctions.Palette_SetSingleDacRegister, GetSetPaletteRegisters));
        // FIXME: Or SetDacRegisters from Aeon code...
        _dispatchTable.Add(VgaFunctions.Palette_SetDacRegisters, new Callback(VgaFunctions.Palette_SetDacRegisters, VideoSubsystemConfiguration));
        _dispatchTable.Add(VgaFunctions.Palette_ReadDacRegisters, new Callback(VgaFunctions.Palette_ReadDacRegisters, ReadDacRegisters));
        _dispatchTable.Add(VgaFunctions.GetDisplayCombinationCode, new Callback(VgaFunctions.GetDisplayCombinationCode, VideoDisplayCombination));
        // TODO: Fix this, this throws an exception because the key is the same as an existing entry...
        //_dispatchTable.Add(VgaFunctions.Palette_SetAllRegisters, new Callback(VgaFunctions.Palette_SetAllRegisters, SetAllEgaPaletteRegisters));
        _dispatchTable.Add(VgaFunctions.GetFunctionalityInfo, new Callback(VgaFunctions.GetFunctionalityInfo, GetFunctionalityInfo));
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
                _vgaCard.SetVideoModeValue(_state.AL);
                break;
            default:
                throw new UnhandledOperationException(_machine,
                    $"Unhandled operation for videoDisplayCombination op={ConvertUtils.ToHex8(op)}");
        }
        _state.AL = 0x1A;
        _state.AH = 0x00;
    }

    private void VideoSubsystemConfiguration() {
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

    public void Dispose() {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing) {
        if(!_disposed) {
            if (disposing) {
                _vgaCard.Dispose();
            }
            _disposed = true;
        }
    }
}
