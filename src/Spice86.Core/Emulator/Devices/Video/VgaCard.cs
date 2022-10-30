namespace Spice86.Core.Emulator.Devices.Video;

using Serilog;
using Spice86.Logging;
using Spice86.Shared.Interfaces;
using Spice86.Shared;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Core.Emulator;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.Errors;
using System.Linq;
using Spice86.Core.Emulator.Video;
using Spice86.Core.DI;

/// <summary>
/// Implementation of VGA card, currently only supports mode 0x13.<br/>
/// TODO: Merge VgaFunctions related code in Aeon's VideoHandler into this, once VideoHandler code is merged into VideoBiosInt10Handler.
/// Complex plan...
/// TODO: Make all VideoModes call SetResolution through this, and make them work with Gdb
/// TODO: Every time a VideoMode change occurs, this must be called.
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
    private bool defaultPaletteLoading = true;
    private GraphicsRegister graphicsRegister;
    private SequencerRegister sequencerRegister;
    private int verticalTextResolution = 16;


    public VgaCard(Machine machine, ILogger logger, IGui? gui, Configuration configuration) : base(machine, configuration) {
        _logger = logger;
        _gui = gui;
        VgaDac = new VgaDac(machine);
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

    public VgaDac VgaDac { get; }

    public byte GetVgaReadIndex() {
        if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
            _logger.Information("GET VGA READ INDEX");
        }
        return VgaDac.State == VgaDac.VgaDacWrite ? (byte)0x3 : (byte)0x0;
    }

    public override byte ReadByte(int port) {
        if (port == VideoPorts.DacStateRead) {
            return GetVgaReadIndex();
        } else if (port == VideoPorts.InputStatus1ReadAlt) {
            return GetStatusRegisterPort();
        } else if (port == VideoPorts.DacData) {
            return RgbDataRead();
        }

        return base.ReadByte(port);
    }

    public override void WriteByte(int port, byte value) {
        if (port == VideoPorts.DacStateRead) {
            SetVgaReadIndex(value);
        } else if (port == VideoPorts.DacAddressWriteMode) {
            SetVgaWriteIndex(value);
        } else if (port == VideoPorts.DacData) {
            RgbDataWrite(value);
        } else if (port == VideoPorts.InputStatus1ReadAlt) {
            bool vsync = (value & 0b100) != 1;
            if (_logger.IsEnabled(Serilog.Events.LogEventLevel.Information)) {
                _logger.Information("Vsync value set to {@VSync} (this is not implemented)", vsync);
            }
        } else {
            base.WriteByte(port, value);
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
                _gui?.SetResolution(80, 25, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.MonochromeText80x25x4:
                _gui?.SetResolution(80, 25, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.ColorText80x25x4:
                _gui?.SetResolution(80, 25, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.ColorGraphics320x200x2A:
                _gui?.SetResolution(320, 200, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
            case VideoMode10h.ColorGraphics320x200x2B:
                _gui?.SetResolution(320, 200, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
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
                _gui?.SetResolution(640, 480, MemoryUtils.ToPhysicalAddress(MemoryMap.GraphicVideoMemorySegment, 0));
                break;
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
        _gui?.Draw(_memory.Ram, VgaDac.Palette, CurrentVideoMode);
    }
}