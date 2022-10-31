namespace Spice86.Core.Emulator.Devices.Video;

using Serilog;

using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Devices.ExternalInput;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Video;
using Spice86.Core.Emulator.Video.Modes;
using Spice86.Core.Emulator.VM;
using Spice86.Logging;
using Spice86.Shared;
using Spice86.Shared.Interfaces;

using System;
using System.Linq;
using Spice86.Core.Emulator.Video;
using Spice86.Core.DI;

/// <summary>
/// Implementation of VGA card, currently only supports mode 0x13.<br/>
/// TODO: Make all VideoModes call SetResolution through this, and make them work with Gdb
/// TODO: Every time a VideoMode change occurs, ensure this class is called properly.
/// TODO: Ensure that unused code (such as SwitchTo80x50TextMode) is used.
/// </summary>
public class VgaCard : DefaultIOPortHandler {
    private readonly ILogger _logger;

    // Means the CRT is busy drawing a line, tells the program it should not draw
    private const byte StatusRegisterRetraceInactive = 0;
    // 4th bit is 1 when the CRT finished drawing and is returning to the beginning
    // of the screen (retrace).
    // Programs use this to know if it is safe to write to VRAM.
    // They write to VRAM when this bit is set, but only after waiting for a 0
    // first.
    // This is to be sure to catch the start of the retrace to ensure having the
    // whole duration of the retrace to write to VRAM.
    // More info here: http://atrevida.comprenica.com/atrtut10.html
    private const byte StatusRegisterRetraceActive = 0b1000;

    private readonly IGui? _gui;
    private byte _crtStatusRegister = StatusRegisterRetraceActive;

    private AttributeControllerRegister attributeRegister;
    private CrtControllerRegister crtRegister;
    private GraphicsRegister graphicsRegister;
    private SequencerRegister sequencerRegister;
    private static readonly long HorizontalBlankingTime = HorizontalPeriod / 2;
    private static readonly long HorizontalPeriod = (long)((1000.0 / 60.0) / 480.0 * Pic.StopwatchTicksPerMillisecond);
    private static readonly long RefreshRate = (long)((1000.0 / 60.0) * Pic.StopwatchTicksPerMillisecond);
    private static readonly long VerticalBlankingTime = RefreshRate / 40;
    private bool attributeDataMode;
    private bool defaultPaletteLoading = true;
    private int verticalTextResolution = 16;

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

    /// <summary>
    /// Segment of the VGA static functionality table.
    /// </summary>
    public const ushort StaticFunctionalityTableSegment = 0x0100;
    /// <summary>
    /// Gets a pointer to the emulated video RAM.
    /// </summary>
    public unsafe byte* RawView { get; private set; }


    public byte[] VideoRam { get; private set; }

    public Machine Machine => _machine;

    /// <summary>
    /// Total number of bytes allocated for video RAM.
    /// </summary>
    public const int TotalVramBytes = 1024 * 1024;

    public VgaCard(Machine machine, ILogger logger, IGui? gui, Configuration configuration) : base(machine, configuration) {
        _logger = logger;
        _gui = gui;
        this.VideoRam = new byte[TotalVramBytes];
        unsafe {
            fixed (byte* ramPtr = this.VideoRam) {
                this.RawView = ramPtr;
            }
        }
        VgaDac = new VgaDac(machine);
        Memory memory = machine.Memory;
        memory.SetUInt32(StaticFunctionalityTableSegment, 0, 0x000FFFFF); // supports all video modes
        memory.SetByte(StaticFunctionalityTableSegment, 0x07, 0x07); // supports all scanlines
        this.TextConsole = new TextConsole(this, machine.Memory.Bios);
    }

    public void GetBlockOfDacColorRegisters(int firstRegister, int numberOfColors, uint colorValuesAddress) {
        Rgb[] rgbs = VgaDac.Palette;
        for (int i = 0; i < numberOfColors; i++) {
            int registerToSet = firstRegister + i;
            Rgb rgb = rgbs[registerToSet];
            _memory.SetUint8(colorValuesAddress++, VgaDac.From8bitTo6bitColor(rgb.R));
            _memory.SetUint8(colorValuesAddress++, VgaDac.From8bitTo6bitColor(rgb.G));
            _memory.SetUint8(colorValuesAddress++, VgaDac.From8bitTo6bitColor(rgb.B));
        }
    }

    public byte GetStatusRegisterPort() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("CHECKING RETRACE");
        }
        byte res = _crtStatusRegister;
        // Next time we will be called retrace will be active, and this until the retrace tick
        _crtStatusRegister = StatusRegisterRetraceActive;
        return res;
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
        if (this.CurrentMode is not null) {
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
    /// Sets the current mode to text mode 80x50.
    /// </summary>
    private void SwitchTo80x50TextMode() {
        var mode = new TextMode(80, 50, 8, this._machine.VgaCard);
        this.CurrentMode = mode;
        _machine.OnVideoModeChanged(EventArgs.Empty);
    }

    public VgaDac VgaDac { get; }

    public byte GetVgaReadIndex() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("GET VGA READ INDEX");
        }
        return VgaDac.State == VgaDac.VgaDacWrite ? (byte)0x3 : (byte)0x0;
    }

    public override byte ReadByte(int port) {
        switch (port) {
            case VideoPorts.DacAddressReadMode:
                return (byte)VgaDac.ReadIndex;

            case VideoPorts.DacAddressWriteMode:
                return (byte)VgaDac.WriteIndex;

            case VideoPorts.DacData:
                return VgaDac.ReadColor();

            case VideoPorts.GraphicsControllerAddress:
                return (byte)graphicsRegister;

            case VideoPorts.GraphicsControllerData:
                return Graphics.ReadRegister(graphicsRegister);

            case VideoPorts.SequencerAddress:
                return (byte)sequencerRegister;

            case VideoPorts.SequencerData:
                return Sequencer.ReadRegister(sequencerRegister);

            case VideoPorts.AttributeAddress:
                return (byte)attributeRegister;

            case VideoPorts.AttributeData:
                return AttributeController.ReadRegister(attributeRegister);

            case VideoPorts.CrtControllerAddress:
            case VideoPorts.CrtControllerAddressAlt:
                return (byte)crtRegister;

            case VideoPorts.CrtControllerData:
            case VideoPorts.CrtControllerDataAlt:
                return CrtController.ReadRegister(crtRegister);

            case VideoPorts.InputStatus1Read:
            case VideoPorts.InputStatus1ReadAlt:
                attributeDataMode = false;
                return GetInputStatus1Value();

            default:
                return 0;
        }
    }


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

    public override void WriteByte(int port, byte value) {
        switch (port) {
            case VideoPorts.DacAddressReadMode:
                VgaDac.ReadIndex = value;
                break;

            case VideoPorts.DacAddressWriteMode:
                VgaDac.WriteIndex = value;
                break;

            case VideoPorts.DacData:
                VgaDac.WriteColor(value);
                break;

            case VideoPorts.GraphicsControllerAddress:
                graphicsRegister = (GraphicsRegister)value;
                break;

            case VideoPorts.GraphicsControllerData:
                Graphics.WriteRegister(graphicsRegister, value);
                break;

            case VideoPorts.SequencerAddress:
                sequencerRegister = (SequencerRegister)value;
                break;

            case VideoPorts.SequencerData:
                SequencerMemoryMode previousMode = Sequencer.SequencerMemoryMode;
                Sequencer.WriteRegister(sequencerRegister, value);
                if ((previousMode & SequencerMemoryMode.Chain4) == SequencerMemoryMode.Chain4 && (Sequencer.SequencerMemoryMode & SequencerMemoryMode.Chain4) == 0)
                    EnterModeX();
                break;

            case VideoPorts.AttributeAddress:
                if (!attributeDataMode)
                    attributeRegister = (AttributeControllerRegister)(value & 0x1F);
                else
                    AttributeController.WriteRegister(attributeRegister, value);
                attributeDataMode = !attributeDataMode;
                break;

            case VideoPorts.AttributeData:
                AttributeController.WriteRegister(attributeRegister, value);
                break;

            case VideoPorts.CrtControllerAddress:
            case VideoPorts.CrtControllerAddressAlt:
                crtRegister = (CrtControllerRegister)value;
                break;

            case VideoPorts.CrtControllerData:
            case VideoPorts.CrtControllerDataAlt:
                int previousVerticalEnd = CrtController.VerticalDisplayEnd;
                CrtController.WriteRegister(crtRegister, value);
                if (previousVerticalEnd != CrtController.VerticalDisplayEnd)
                    ChangeVerticalEnd();
                break;
        }
    }

    public override void InitPortHandlers(IOPortDispatcher ioPortDispatcher) {
        ioPortDispatcher.AddIOPortHandler(VideoPorts.SequencerAddress, this);
        ioPortDispatcher.AddIOPortHandler(VideoPorts.SequencerData, this);
        ioPortDispatcher.AddIOPortHandler(VideoPorts.DacStateRead, this);
        ioPortDispatcher.AddIOPortHandler(VideoPorts.DacAddressWriteMode, this);
        ioPortDispatcher.AddIOPortHandler(VideoPorts.DacData, this);
        ioPortDispatcher.AddIOPortHandler(VideoPorts.GraphicsControllerAddress, this);
        ioPortDispatcher.AddIOPortHandler(VideoPorts.InputStatus1ReadAlt, this);
    }

    public byte RgbDataRead() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PALETTE READ");
        }
        return VgaDac.From8bitTo6bitColor(VgaDac.ReadColor());
    }

    public void RgbDataWrite(byte value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("PALETTE WRITE {@Value}", value);
        }
        VgaDac.WriteColor(VgaDac.From6bitColorTo8bit(value));
    }

    public void SetBlockOfDacColorRegisters(int firstRegister, int numberOfColors, uint colorValuesAddress) {
        Rgb[] rgbs = VgaDac.Palette;
        for (int i = 0; i < numberOfColors; i++) {
            int registerToSet = firstRegister + i;
            Rgb rgb = rgbs[registerToSet];
            byte r = VgaDac.From6bitColorTo8bit(_memory.GetUint8(colorValuesAddress++));
            byte g = VgaDac.From6bitColorTo8bit(_memory.GetUint8(colorValuesAddress++));
            byte b = VgaDac.From6bitColorTo8bit(_memory.GetUint8(colorValuesAddress++));
            rgb.R = r;
            rgb.G = g;
            rgb.B = b;
        }
    }

    public void SetVgaReadIndex(int value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET VGA READ INDEX {@Value}", value);
        }
        VgaDac.ReadIndex = value;
        VgaDac.Colour = 0;
        VgaDac.State = VgaDac.VgaDacRead;
    }

    public void SetVgaWriteIndex(int value) {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("SET VGA WRITE INDEX {@Value}", value);
        }
        VgaDac.WriteIndex = value;
        VgaDac.Colour = 0;
        VgaDac.State = VgaDac.VgaDacWrite;
    }

    public VideoMode10h CurrentVideoMode { get; private set; }

    public void SetVideoModeValue(byte mode) {
        if(!Enum.GetValues<VideoMode10h>().Select(static x => (byte)x).Contains(mode)) {
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Error)) {
                _logger.Error("UNSUPPORTED VIDEO MODE {@VideMode}", mode);
            }
        }
        CurrentVideoMode = (VideoMode10h)mode;
        switch (CurrentVideoMode)
        {
            case VideoMode10h.Text40x25x1:
                _gui?.SetResolution(40, 25, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.Text80x25x1:
            case VideoMode10h.MonochromeText80x25x4:
            case VideoMode10h.ColorText80x25x4:
                _gui?.SetResolution(80, 25, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.ColorGraphics320x200x2A:
            case VideoMode10h.ColorGraphics320x200x2B:
            case VideoMode10h.ColorGraphics320x200x4:
                _gui?.SetResolution(320, 200, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.ColorGraphics640x200x4:
                _gui?.SetResolution(640, 200, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.ColorGraphics640x350x4:
                _gui?.SetResolution(640, 350, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.Graphics320x200x8:
                _gui?.SetResolution(320, 200, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.ColorText40x25x4:
                _gui?.SetResolution(40, 25, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.Graphics640x200x1:
                _gui?.SetResolution(640, 200, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.Graphics640x350x1:
                _gui?.SetResolution(640, 350, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.Graphics640x480x1:
            case VideoMode10h.Graphics640x480x4:
                _gui?.SetResolution(640, 480, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            default:
                throw new UnrecoverableException($"Unimplemented video mode {CurrentVideoMode}");
        }
    }

    public void TickRetrace() {
        // Inactive at tick time, but will become active once the code checks for it.
        _crtStatusRegister = StatusRegisterRetraceInactive;
    }

    public void UpdateScreen() {
        _gui?.Draw();
    }
}