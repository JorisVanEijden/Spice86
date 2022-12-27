using Spice86.Core.Emulator.Devices.ExternalInput;

namespace Spice86.Core.Emulator.InterruptHandlers.Video;

using Serilog;

using Spice86.Core.Emulator.Callback;
using Spice86.Core.Emulator.Devices.Video;
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
            0x04 => new SegmentedAddress(Memory.FontSegment, Memory.Font8x8Offset + (128 * 8)),
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
        switch (_state.AL) {
            case VideoFunctions.Palette_SetSingleRegister:
                SetEgaPaletteRegister(_state.BL, _state.BH);
                break;

            case VideoFunctions.Palette_SetBorderColor:
                // Ignore for now.
                break;

            case VideoFunctions.Palette_SetAllRegisters:
                SetAllEgaPaletteRegisters();
                break;

            case VideoFunctions.Palette_ReadSingleDacRegister:
                // These are commented out because they cause weird issues sometimes.
                //_state.DH = (byte)((_vgaCard.VgaDac.Palette[_state.BL] >> 18) & 0xCF);
                //_state.CH = (byte)((_vgaCard.VgaDac.Palette[_state.BL] >> 10) & 0xCF);
                //_state.CL = (byte)((_vgaCard.VgaDac.Palette[_state.BL] >> 2) & 0xCF);
                break;

            case VideoFunctions.Palette_SetSingleDacRegister:
                _vgaCard.VgaDac.SetColor(_state.BL, _state.DH, _state.CH, _state.CL);
                break;

            case VideoFunctions.Palette_SetDacRegisters:
                SetDacRegisters();
                break;

            case VideoFunctions.Palette_ReadDacRegisters:
                ReadDacRegisters();
                break;

            case VideoFunctions.Palette_ToggleBlink:
                // Blinking is not emulated.
                break;

            case VideoFunctions.Palette_SelectDacColorPage:
                System.Diagnostics.Debug.WriteLine("Select DAC color page");
                break;

            default:
                throw new NotImplementedException(string.Format("Video command 10{0:X2}h not implemented.", _state.AL));
        }
    }

    /// <summary>
    /// Sets DAC color registers to values in emulated RAM.
    /// </summary>
    private void SetDacRegisters() {
        ushort segment = _state.ES;
        uint offset = (ushort)_state.DX;
        int start = _state.BX;
        int count = _state.CX;

        for (int i = start; i < count; i++) {
            byte r = VgaDac.From6bitColorTo8bit(_memory.GetByte(segment, offset));
            byte g = VgaDac.From6bitColorTo8bit(_memory.GetByte(segment, offset + 1u));
            byte b = VgaDac.From6bitColorTo8bit(_memory.GetByte(segment, offset + 2u));

            _vgaCard.VgaDac.SetColor((byte)(start + i), r, g, b);

            offset += 3u;
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

    /// <summary>
    /// TODO: Implement this. (CGA support)
    /// </summary>
    public void SetColorPaletteOrBackgroudColor() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET COLOR PALETTE {@ColorId}, {@ColorValue}", _state.BH, _state.BL);
        }
        switch (_state.BH) {
            case VideoFunctions.Video_SetBackgroundColor:
                break;

            case VideoFunctions.Video_SetPalette:
                System.Diagnostics.Debug.WriteLine("CGA set palette not implemented.");
                break;
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

    public void SetDisplayMode() {
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
        _dispatchTable.Add(VideoFunctions.SetDisplayMode, new Callback(VideoFunctions.SetDisplayMode, SetDisplayMode));
        _dispatchTable.Add(VideoFunctions.SetCursorShape, new Callback(VideoFunctions.SetCursorShape, SetCursorType));
        _dispatchTable.Add(VideoFunctions.SetCursorPosition, new Callback(VideoFunctions.SetCursorPosition, SetCursorPosition));
        _dispatchTable.Add(VideoFunctions.ScrollUpWindow, new Callback(VideoFunctions.ScrollUpWindow, ScrollPageUp));
        _dispatchTable.Add(VideoFunctions.Video, new Callback(VideoFunctions.Video, SetColorPaletteOrBackgroudColor));
        _dispatchTable.Add(VideoFunctions.TeletypeOutput, new Callback(VideoFunctions.TeletypeOutput, WriteTextInTeletypeMode));
        _dispatchTable.Add(VideoFunctions.GetDisplayMode, new Callback(VideoFunctions.GetDisplayMode, GetVideoStatus));
        _dispatchTable.Add(VideoFunctions.Palette, new Callback(VideoFunctions.Palette, GetSetPaletteRegisters));
        _dispatchTable.Add(VideoFunctions.EGA, new Callback(VideoFunctions.EGA, GetSetEGARegisters));
        _dispatchTable.Add(VideoFunctions.GetDisplayCombinationCode, new Callback(VideoFunctions.GetDisplayCombinationCode, GetVideoDisplayCombination));
        _dispatchTable.Add(VideoFunctions.GetFunctionalityInfo, new Callback(VideoFunctions.GetFunctionalityInfo, GetFunctionalityInfo));
        _dispatchTable.Add(VideoFunctions.Font, new Callback(VideoFunctions.Font, GetFontInfoOrSwitchTo8060TextMode));
        _dispatchTable.Add(VideoFunctions.SelectActiveDisplayPage, new Callback(VideoFunctions.SelectActiveDisplayPage, SelectActiveDisplayPage));
        _dispatchTable.Add(VideoFunctions.WriteCharacterAtCursor, new Callback(VideoFunctions.WriteCharacterAtCursor, WriteCharacterAtCursor));
        _dispatchTable.Add(VideoFunctions.WriteCharacterAndAttributeAtCursor, new Callback(VideoFunctions.WriteCharacterAndAttributeAtCursor, WriteCharacterAndAttributeAtCursor));
        _dispatchTable.Add(VideoFunctions.ReadCharacterAndAttributeAtCursor, new Callback(VideoFunctions.ReadCharacterAndAttributeAtCursor, ReadCharacterAndAttributeAtCursor));
    }

    private void GetVideoDisplayCombination() {
        if(_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("GET VIDEO DISPLAY COMBINATION");
        }
        _state.AL = 0x1A;
        _state.BX = 0x0008;
    }

    private void ReadCharacterAndAttributeAtCursor() {
        _state.AX = _vgaCard.TextConsole.GetCharacter(_vgaCard.TextConsole.CursorPosition.X, _vgaCard.TextConsole.CursorPosition.Y);
    }

    private void WriteCharacterAndAttributeAtCursor() {
        _vgaCard.TextConsole.Write(_state.AL, (byte)(_state.BL & 0x0F), (byte)(_state.BL >> 4), false);
    }

    private void WriteCharacterAtCursor() {
        _vgaCard.TextConsole.Write(_state.AL);
    }

    private void SelectActiveDisplayPage() {
        if(this._vgaCard.CurrentMode is null) {
            return;
        }
        this._vgaCard.CurrentMode.ActiveDisplayPage = _state.AL;

    }

    private void GetSetEGARegisters() {
        switch (_state.BL) {
            case VideoFunctions.EGA_GetInfo:
                _state.BX = 0x03; // 256k installed
                _state.CX = 0x09; // EGA switches set
                break;

            case VideoFunctions.EGA_SelectVerticalResolution:
                int verticalTextResolution = 8;
                if (_state.AL == 0) {
                    verticalTextResolution = 8;
                } else if (_state.AL == 1) {
                    verticalTextResolution = 14;
                } else {
                    verticalTextResolution = 16;
                }
                _vgaCard.VerticalTextResolution = verticalTextResolution;
                _state.AL = 0x12; // Success
                break;

            case VideoFunctions.EGA_PaletteLoading:
                _vgaCard.DefaultPaletteLoading = _state.AL == 0;
                break;

            default:
                throw new NotImplementedException($"Video command {VideoFunctions.EGA:X2}, {_state.BL:X2}h not implemented.");
        }
    }

    /// <summary>
    /// TODO: Extend this (Text Mode)
    /// </summary>
    private void GetFontInfoOrSwitchTo8060TextMode() {
        switch (_state.AL) {
            case VideoFunctions.Font_GetFontInfo:
                GetFontInfo();
                break;

            case VideoFunctions.Font_Load8x8:
                _vgaCard.SwitchTo80x50TextMode();
                break;

            case VideoFunctions.Font_Load8x16:
                break;

            default:
                throw new NotImplementedException($"Video command 11{_state.AL:X2}h not implemented.");
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
