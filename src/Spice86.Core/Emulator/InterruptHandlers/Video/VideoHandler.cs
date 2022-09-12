namespace Spice86.Core.Emulator.InterruptHandlers.Video;

using System;
using System.Collections.Generic;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM;
using System.Runtime.InteropServices;
using Spice86.Core.Emulator.Video;
using Spice86.Core.Emulator.Video.Modes;
using Spice86.Core.Emulator.IOPorts;
using Spice86.Shared;
using Spice86.Core.Emulator.Video.Vesa;
using Spice86.Core.Emulator.Devices.ExternalInput;

/// <summary>
/// Provides emulated video and int 10h functions.
/// </summary>
public sealed class VideoHandler : InterruptHandler, IDisposable
{
    /// <summary>
    /// Total number of bytes allocated for video RAM.
    /// </summary>
    public const int TotalVramBytes = 1024 * 1024;
    /// <summary>
    /// Segment of the VGA static functionality table.
    /// </summary>
    public const ushort StaticFunctionalityTableSegment = 0x0100;

    private static readonly long RefreshRate = (long)((1000.0 / 60.0) * Pic.StopwatchTicksPerMillisecond);
    private static readonly long VerticalBlankingTime = RefreshRate / 40;
    private static readonly long HorizontalPeriod = (long)((1000.0 / 60.0) / 480.0 * Pic.StopwatchTicksPerMillisecond);
    private static readonly long HorizontalBlankingTime = HorizontalPeriod / 2;

    private bool disposed;
    private GraphicsRegister graphicsRegister;
    private SequencerRegister sequencerRegister;
    private AttributeControllerRegister attributeRegister;
    private CrtControllerRegister crtRegister;
    private bool attributeDataMode;
    private bool defaultPaletteLoading = true;
    private int verticalTextResolution = 16;
    private readonly VbeHandler vbe;

    public VideoHandler(Machine machine) : base(machine)
    {
        this.VirtualMachine = machine;
        unsafe
        {
            this.VideoRam = new IntPtr(NativeMemory.AllocZeroed(TotalVramBytes));
        }

        this.vbe = new VbeHandler(this);

        InitializeStaticFunctionalityTable();

        this.TextConsole = new TextConsole(this, machine.Memory.Bios);
        CurrentMode = new Emulator.Video.Modes.TextMode(40, 25, 8, this);
        this.SetDisplayMode(VideoMode10h.ColorText80x25x4);
    }

    ~VideoHandler() => this.Dispose();

    /// <summary>
    /// Gets the current display mode.
    /// </summary>
    public VideoMode CurrentMode { get; private set; }
    /// <summary>
    /// Gets the text-mode display instance.
    /// </summary>
    public TextConsole TextConsole { get; }
    /// <summary>
    /// Gets the VGA DAC.
    /// </summary>
    public Dac Dac { get; } = new Dac();
    /// <summary>
    /// Gets the VGA attribute controller.
    /// </summary>
    public AttributeController AttributeController { get; } = new AttributeController();
    /// <summary>
    /// Gets the VGA graphics controller.
    /// </summary>
    public Graphics Graphics { get; } = new Graphics();
    /// <summary>
    /// Gets the VGA sequencer.
    /// </summary>
    public Sequencer Sequencer { get; } = new Sequencer();
    /// <summary>
    /// Gets the VGA CRT controller.
    /// </summary>
    public CrtController CrtController { get; } = new CrtController();
    /// <summary>
    /// Gets a pointer to the emulated video RAM.
    /// </summary>
    public IntPtr VideoRam { get; }
    /// <summary>
    /// Gets the virtual machine instance which owns the VideoHandler.
    /// </summary>
    public Machine VirtualMachine { get; }

    public override byte Index => 0x10;
    public override void Run()
    {
        switch (VirtualMachine.Cpu.State.AH)
        {
            case 0x4F:
                this.vbe.HandleFunction();
                break;

            case VgaFunctions.GetDisplayMode:
                VirtualMachine.Cpu.State.AH = VirtualMachine.Memory.Bios.ScreenColumns;
                VirtualMachine.Cpu.State.AL = (byte)VirtualMachine.Memory.Bios.VideoMode;
                break;

            case VgaFunctions.ScrollUpWindow:
                //if(vm.Cpu.State.State.AL == 0)
                //    textDisplay.Clear();
                //else
                {
                    byte foreground = (byte)((VirtualMachine.Cpu.State.BX >> 8) & 0x0F);
                    byte background = (byte)((VirtualMachine.Cpu.State.BX >> 12) & 0x0F);
                    TextConsole.ScrollTextUp(VirtualMachine.Cpu.State.CL, VirtualMachine.Cpu.State.CH, VirtualMachine.Cpu.State.DL, VirtualMachine.Cpu.State.DH, VirtualMachine.Cpu.State.AL, foreground, background);
                }
                break;

            case VgaFunctions.EGA:
                switch (VirtualMachine.Cpu.State.BL)
                {
                    case VgaFunctions.EGA_GetInfo:
                        VirtualMachine.Cpu.State.BX = 0x03; // 256k installed
                        VirtualMachine.Cpu.State.CX = 0x09; // EGA switches set
                        break;

                    case VgaFunctions.EGA_SelectVerticalResolution:
                        if (VirtualMachine.Cpu.State.AL == 0)
                            this.verticalTextResolution = 8;
                        else if (VirtualMachine.Cpu.State.AL == 1)
                            this.verticalTextResolution = 14;
                        else
                            this.verticalTextResolution = 16;
                        VirtualMachine.Cpu.State.AL = 0x12; // Success
                        break;

                    case VgaFunctions.EGA_PaletteLoading:
                        this.defaultPaletteLoading = VirtualMachine.Cpu.State.AL == 0;
                        break;

                    default:
                        System.Diagnostics.Debug.WriteLine(string.Format("Video command {0:X2}, BL={1:X2}h not implemented.", VgaFunctions.EGA, VirtualMachine.Cpu.State.BL));
                        break;
                }
                break;

            case VgaFunctions.ReadCharacterAndAttributeAtCursor:
                VirtualMachine.Cpu.State.AX = TextConsole.GetCharacter(TextConsole.CursorPosition.X, TextConsole.CursorPosition.Y);
                break;

            case VgaFunctions.WriteCharacterAndAttributeAtCursor:
                TextConsole.Write(VirtualMachine.Cpu.State.AL, (byte)(VirtualMachine.Cpu.State.BL & 0x0F), (byte)(VirtualMachine.Cpu.State.BL >> 4), false);
                break;

            case VgaFunctions.WriteCharacterAtCursor:
                TextConsole.Write(VirtualMachine.Cpu.State.AL);
                break;

            case VgaFunctions.GetDisplayCombinationCode:
                VirtualMachine.Cpu.State.AL = 0x1A;
                VirtualMachine.Cpu.State.BX = 0x0008;
                break;

            case VgaFunctions.SetDisplayMode:
                SetDisplayMode((VideoMode10h)VirtualMachine.Cpu.State.AL);
                break;

            case VgaFunctions.SetCursorPosition:
                TextConsole.CursorPosition = new Point(VirtualMachine.Cpu.State.DL, VirtualMachine.Cpu.State.DH);
                break;

            case VgaFunctions.SetCursorShape:
                SetCursorShape(VirtualMachine.Cpu.State.CH, VirtualMachine.Cpu.State.CL);
                break;

            case VgaFunctions.SelectActiveDisplayPage:
                this.CurrentMode.ActiveDisplayPage = VirtualMachine.Cpu.State.AL;
                break;

            case VgaFunctions.Palette:
                switch (VirtualMachine.Cpu.State.AL)
                {
                    case VgaFunctions.Palette_SetSingleRegister:
                        SetEgaPaletteRegister(VirtualMachine.Cpu.State.BL, VirtualMachine.Cpu.State.BH);
                        break;

                    case VgaFunctions.Palette_SetBorderColor:
                        // Ignore for now.
                        break;

                    case VgaFunctions.Palette_SetAllRegisters:
                        SetAllEgaPaletteRegisters();
                        break;

                    case VgaFunctions.Palette_ReadSingleDacRegister:
                        // These are commented out because they cause weird issues sometimes.
                        //vm.Cpu.State.State.DH = (byte)((dac.Palette[vm.Cpu.State.State.BL] >> 18) & 0xCF);
                        //vm.Cpu.State.State.CH = (byte)((dac.Palette[vm.Cpu.State.State.BL] >> 10) & 0xCF);
                        //vm.Cpu.State.State.CL = (byte)((dac.Palette[vm.Cpu.State.State.BL] >> 2) & 0xCF);
                        break;

                    case VgaFunctions.Palette_SetSingleDacRegister:
                        Dac.SetColor(VirtualMachine.Cpu.State.BL, VirtualMachine.Cpu.State.DH, VirtualMachine.Cpu.State.CH, VirtualMachine.Cpu.State.CL);
                        break;

                    case VgaFunctions.Palette_SetDacRegisters:
                        SetDacRegisters();
                        break;

                    case VgaFunctions.Palette_ReadDacRegisters:
                        ReadDacRegisters();
                        break;

                    case VgaFunctions.Palette_ToggleBlink:
                        // Blinking is not emulated.
                        break;

                    case VgaFunctions.Palette_SelectDacColorPage:
                        System.Diagnostics.Debug.WriteLine("Select DAC color page");
                        break;

                    default:
                        throw new NotImplementedException(string.Format("Video command 10{0:X2}h not implemented.", VirtualMachine.Cpu.State.AL));
                }
                break;

            case VgaFunctions.GetCursorPosition:
                VirtualMachine.Cpu.State.CH = 14;
                VirtualMachine.Cpu.State.CL = 15;
                VirtualMachine.Cpu.State.DH = (byte)TextConsole.CursorPosition.Y;
                VirtualMachine.Cpu.State.DL = (byte)TextConsole.CursorPosition.X;
                break;

            case VgaFunctions.TeletypeOutput:
                TextConsole.Write(VirtualMachine.Cpu.State.AL);
                break;

            case VgaFunctions.GetFunctionalityInfo:
                GetFunctionalityInfo();
                break;

            case 0xEF:
                VirtualMachine.Cpu.State.DX = 0;
                break;

            case 0xFE:
                break;

            case VgaFunctions.Font:
                switch (VirtualMachine.Cpu.State.AL)
                {
                    case VgaFunctions.Font_GetFontInfo:
                        GetFontInfo();
                        break;

                    case VgaFunctions.Font_Load8x8:
                        SwitchTo80x50TextMode();
                        break;

                    case VgaFunctions.Font_Load8x16:
                        break;

                    default:
                        throw new NotImplementedException($"Video command 11{this.VirtualMachine.Cpu.State.AL:X2}h not implemented.");
                }
                break;

            case VgaFunctions.Video:
                switch (this.VirtualMachine.Cpu.State.BL)
                {
                    case VgaFunctions.Video_SetBackgroundColor:
                        break;

                    case VgaFunctions.Video_SetPalette:
                        System.Diagnostics.Debug.WriteLine("CGA set palette not implemented.");
                        break;
                }
                break;

            default:
                System.Diagnostics.Debug.WriteLine($"Video command {this.VirtualMachine.Cpu.State.AH:X2}h not implemented.");
                break;
        }
    }

    IEnumerable<int> InputPorts
    {
        get
        {
            return new SortedSet<int>
            {
                Ports.AttributeAddress,
                Ports.AttributeData,
                Ports.CrtControllerAddress,
                Ports.CrtControllerAddressAlt,
                Ports.CrtControllerData,
                Ports.CrtControllerDataAlt,
                Ports.DacAddressReadMode,
                Ports.DacAddressWriteMode,
                Ports.DacData,
                Ports.DacStateRead,
                Ports.FeatureControlRead,
                Ports.GraphicsControllerAddress,
                Ports.GraphicsControllerData,
                Ports.InputStatus0Read,
                Ports.InputStatus1Read,
                Ports.InputStatus1ReadAlt,
                Ports.MiscOutputRead,
                Ports.SequencerAddress,
                Ports.SequencerData
            };
        }
    }
    public byte ReadByte(int port)
    {
        switch (port)
        {
            case Ports.DacAddressReadMode:
                return Dac.ReadIndex;

            case Ports.DacAddressWriteMode:
                return Dac.WriteIndex;

            case Ports.DacData:
                return Dac.Read();

            case Ports.GraphicsControllerAddress:
                return (byte)graphicsRegister;

            case Ports.GraphicsControllerData:
                return Graphics.ReadRegister(graphicsRegister);

            case Ports.SequencerAddress:
                return (byte)sequencerRegister;

            case Ports.SequencerData:
                return Sequencer.ReadRegister(sequencerRegister);

            case Ports.AttributeAddress:
                return (byte)attributeRegister;

            case Ports.AttributeData:
                return AttributeController.ReadRegister(attributeRegister);

            case Ports.CrtControllerAddress:
            case Ports.CrtControllerAddressAlt:
                return (byte)crtRegister;

            case Ports.CrtControllerData:
            case Ports.CrtControllerDataAlt:
                return CrtController.ReadRegister(crtRegister);

            case Ports.InputStatus1Read:
            case Ports.InputStatus1ReadAlt:
                attributeDataMode = false;
                return GetInputStatus1Value();

            default:
                return 0;
        }
    }
    public ushort ReadWord(int port) => this.ReadByte(port);

    IEnumerable<int> OutputPorts
    {
        get
        {
            return new SortedSet<int>
            {
                Ports.AttributeAddress,
                Ports.AttributeData,
                Ports.CrtControllerAddress,
                Ports.CrtControllerAddressAlt,
                Ports.CrtControllerData,
                Ports.CrtControllerDataAlt,
                Ports.DacAddressReadMode,
                Ports.DacAddressWriteMode,
                Ports.DacData,
                Ports.FeatureControlWrite,
                Ports.FeatureControlWriteAlt,
                Ports.GraphicsControllerAddress,
                Ports.GraphicsControllerData,
                Ports.MiscOutputWrite,
                Ports.SequencerAddress,
                Ports.SequencerData
            };
        }
    }
    public void WriteByte(int port, byte value)
    {
        switch (port)
        {
            case Ports.DacAddressReadMode:
                Dac.ReadIndex = value;
                break;

            case Ports.DacAddressWriteMode:
                Dac.WriteIndex = value;
                break;

            case Ports.DacData:
                Dac.Write(value);
                break;

            case Ports.GraphicsControllerAddress:
                graphicsRegister = (GraphicsRegister)value;
                break;

            case Ports.GraphicsControllerData:
                Graphics.WriteRegister(graphicsRegister, value);
                break;

            case Ports.SequencerAddress:
                sequencerRegister = (SequencerRegister)value;
                break;

            case Ports.SequencerData:
                SequencerMemoryMode previousMode = Sequencer.SequencerMemoryMode;
                Sequencer.WriteRegister(sequencerRegister, value);
                if ((previousMode & SequencerMemoryMode.Chain4) == SequencerMemoryMode.Chain4 && (Sequencer.SequencerMemoryMode & SequencerMemoryMode.Chain4) == 0)
                    EnterModeX();
                break;

            case Ports.AttributeAddress:
                if (!attributeDataMode)
                    attributeRegister = (AttributeControllerRegister)(value & 0x1F);
                else
                    AttributeController.WriteRegister(attributeRegister, value);
                attributeDataMode = !attributeDataMode;
                break;

            case Ports.AttributeData:
                AttributeController.WriteRegister(attributeRegister, value);
                break;

            case Ports.CrtControllerAddress:
            case Ports.CrtControllerAddressAlt:
                crtRegister = (CrtControllerRegister)value;
                break;

            case Ports.CrtControllerData:
            case Ports.CrtControllerDataAlt:
                int previousVerticalEnd = CrtController.VerticalDisplayEnd;
                CrtController.WriteRegister(crtRegister, value);
                if (previousVerticalEnd != CrtController.VerticalDisplayEnd)
                    ChangeVerticalEnd();
                break;
        }
    }

    /// <summary>
    /// Reads a byte from video RAM.
    /// </summary>
    /// <param name="offset">Offset of byte to read.</param>
    /// <returns>Byte read from video RAM.</returns>
    public byte GetVramByte(uint offset) => this.CurrentMode.GetVramByte(offset);
    /// <summary>
    /// Sets a byte in video RAM to a specified value.
    /// </summary>
    /// <param name="offset">Offset of byte to set.</param>
    /// <param name="value">Value to write.</param>
    public void SetVramByte(uint offset, byte value) => this.CurrentMode.SetVramByte(offset, value);
    /// <summary>
    /// Reads a word from video RAM.
    /// </summary>
    /// <param name="offset">Offset of word to read.</param>
    /// <returns>Word read from video RAM.</returns>
    public ushort GetVramWord(uint offset) => this.CurrentMode.GetVramWord(offset);
    /// <summary>
    /// Sets a word in video RAM to a specified value.
    /// </summary>
    /// <param name="offset">Offset of word to set.</param>
    /// <param name="value">Value to write.</param>
    public void SetVramWord(uint offset, ushort value) => this.CurrentMode.SetVramWord(offset, value);
    /// <summary>
    /// Reads a doubleword from video RAM.
    /// </summary>
    /// <param name="offset">Offset of doubleword to read.</param>
    /// <returns>Doubleword read from video RAM.</returns>
    public uint GetVramDWord(uint offset) => this.CurrentMode.GetVramDWord(offset);
    /// <summary>
    /// Sets a doubleword in video RAM to a specified value.
    /// </summary>
    /// <param name="offset">Offset of doubleword to set.</param>
    /// <param name="value">Value to write.</param>
    public void SetVramDWord(uint offset, uint value) => this.CurrentMode.SetVramDWord(offset, value);
    /// <summary>
    /// Initializes a new display mode.
    /// </summary>
    /// <param name="videoMode">New display mode.</param>
    public void SetDisplayMode(VideoMode10h videoMode)
    {
        _machine.Memory.Bios.VideoMode = videoMode;
        VideoMode mode;

        switch (videoMode)
        {
            case VideoMode10h.ColorText40x25x4:
                mode = new Emulator.Video.Modes.TextMode(40, 25, 8, this);
                break;

            case VideoMode10h.ColorText80x25x4:
            case VideoMode10h.MonochromeText80x25x4:
                mode = new Emulator.Video.Modes.TextMode(80, 25, this.verticalTextResolution, this);
                break;

            case VideoMode10h.ColorGraphics320x200x2A:
            case VideoMode10h.ColorGraphics320x200x2B:
                mode = new Emulator.Video.Modes.CgaMode4(this);
                break;

            case VideoMode10h.ColorGraphics320x200x4:
                mode = new Emulator.Video.Modes.EgaVga16(320, 200, 8, this);
                break;

            case VideoMode10h.ColorGraphics640x200x4:
                mode = new Emulator.Video.Modes.EgaVga16(640, 400, 8, this);
                break;

            case VideoMode10h.ColorGraphics640x350x4:
                mode = new Emulator.Video.Modes.EgaVga16(640, 350, 8, this);
                break;

            case VideoMode10h.Graphics640x480x4:
                mode = new Emulator.Video.Modes.EgaVga16(640, 480, 16, this);
                break;

            case VideoMode10h.Graphics320x200x8:
                this.Sequencer.SequencerMemoryMode = SequencerMemoryMode.Chain4;
                mode = new Emulator.Video.Modes.Vga256(320, 200, this);
                break;

            default:
                throw new NotSupportedException();
        }

        this.SetDisplayMode(mode);
    }
    /// <summary>
    /// Initializes a new display mode.
    /// </summary>
    /// <param name="mode">New display mode.</param>
    public void SetDisplayMode(VideoMode mode)
    {
        this.CurrentMode = mode;
        mode.InitializeMode(this);
        Graphics.WriteRegister(GraphicsRegister.ColorDontCare, 0x0F);

        if (this.defaultPaletteLoading)
            Dac.Reset();

        VirtualMachine.OnVideoModeChanged(EventArgs.Empty);
    }

    void IDisposable.Dispose()
    {
        this.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Sets the current mode to unchained mode 13h.
    /// </summary>
    private void EnterModeX()
    {
        var mode = new Emulator.Video.Modes.Unchained256(320, 200, this);
        CrtController.Offset = 320 / 8;
        this.CurrentMode = mode;
        VirtualMachine.OnVideoModeChanged(EventArgs.Empty);
    }
    /// <summary>
    /// Changes the current video mode to match the new value of the vertical end register.
    /// </summary>
    private void ChangeVerticalEnd()
    {
        // this is a hack
        int newEnd = this.CrtController.VerticalDisplayEnd | ((this.CrtController.Overflow & (1 << 1)) << 7) | ((this.CrtController.Overflow & (1 << 6)) << 3);
        if (this.CurrentMode is Emulator.Video.Modes.Unchained256)
        {
            newEnd /= 2;
        }
        else
        {
            newEnd = newEnd switch
            {
                223 => 480,
                184 => 440,
                _ => newEnd * 2
            };
        }

        this.CurrentMode.Height = newEnd;
        VirtualMachine.OnVideoModeChanged(EventArgs.Empty);
    }
    /// <summary>
    /// Sets the current mode to text mode 80x50.
    /// </summary>
    private void SwitchTo80x50TextMode()
    {
        var mode = new Emulator.Video.Modes.TextMode(80, 50, 8, this);
        this.CurrentMode = mode;
        VirtualMachine.OnVideoModeChanged(EventArgs.Empty);
    }
    /// <summary>
    /// Sets DAC color registers to values in emulated RAM.
    /// </summary>
    private void SetDacRegisters()
    {
        ushort segment = VirtualMachine.Cpu.State.ES;
        uint offset = (ushort)VirtualMachine.Cpu.State.DX;
        int start = VirtualMachine.Cpu.State.BX;
        int count = VirtualMachine.Cpu.State.CX;

        for (int i = start; i < count; i++)
        {
            byte r = VirtualMachine.Memory.GetByte(segment, offset);
            byte g = VirtualMachine.Memory.GetByte(segment, offset + 1u);
            byte b = VirtualMachine.Memory.GetByte(segment, offset + 2u);

            Dac.SetColor((byte)(start + i), r, g, b);

            offset += 3u;
        }
    }
    /// <summary>
    /// Reads DAC color registers to emulated RAM.
    /// </summary>
    private void ReadDacRegisters()
    {
        ushort segment = VirtualMachine.Cpu.State.ES;
        uint offset = (ushort)VirtualMachine.Cpu.State.DX;
        int start = VirtualMachine.Cpu.State.BX;
        int count = VirtualMachine.Cpu.State.CX;

        for (int i = start; i < count; i++)
        {
            uint r = (Dac.Palette[start + i] >> 18) & 0xCFu;
            uint g = (Dac.Palette[start + i] >> 10) & 0xCFu;
            uint b = (Dac.Palette[start + i] >> 2) & 0xCFu;

            VirtualMachine.Memory.SetByte(segment, offset, (byte)r);
            VirtualMachine.Memory.SetByte(segment, offset + 1u, (byte)g);
            VirtualMachine.Memory.SetByte(segment, offset + 2u, (byte)b);

            offset += 3u;
        }
    }
    /// <summary>
    /// Sets all of the EGA color palette registers to values in emulated RAM.
    /// </summary>
    private void SetAllEgaPaletteRegisters()
    {
        ushort segment = VirtualMachine.Cpu.State.ES;
        uint offset = (ushort)VirtualMachine.Cpu.State.DX;

        for (uint i = 0; i < 16u; i++)
            SetEgaPaletteRegister((int)i, VirtualMachine.Memory.GetByte(segment, offset + i));
    }
    /// <summary>
    /// Gets a specific EGA color palette register.
    /// </summary>
    /// <param name="index">Index of color to set.</param>
    /// <param name="color">New value of the color.</param>
    private void SetEgaPaletteRegister(int index, byte color)
    {
        if (VirtualMachine.Memory.Bios.VideoMode == VideoMode10h.ColorGraphics320x200x4)
            AttributeController.InternalPalette[index & 0x0F] = (byte)(color & 0x0F);
        else
            AttributeController.InternalPalette[index & 0x0F] = color;
    }
    /// <summary>
    /// Gets information about BIOS fonts.
    /// </summary>
    private void GetFontInfo()
    {
        SegmentedAddress address = this.VirtualMachine.Cpu.State.BH switch
        {
            0x00 => this.VirtualMachine.Memory.GetRealModeInterruptAddress(0x1F),
            0x01 => this.VirtualMachine.Memory.GetRealModeInterruptAddress(0x43),
            0x02 or 0x05 => new SegmentedAddress(Memory.FontSegment, Memory.Font8x14Offset),
            0x03 => new SegmentedAddress(Memory.FontSegment, Memory.Font8x8Offset),
            0x04 => new SegmentedAddress(Memory.FontSegment, Memory.Font8x8Offset + 128 * 8),
            _ => new SegmentedAddress(Memory.FontSegment, Memory.Font8x16Offset),
        };

        this.VirtualMachine.Cpu.State.ES = address.Segment;
        this.VirtualMachine.Cpu.State.BP = address.Offset;
        this.VirtualMachine.Cpu.State.CX = (ushort)this.CurrentMode.FontHeight;
        this.VirtualMachine.Cpu.State.DL = this.VirtualMachine.Memory.Bios.ScreenRows;
    }
    /// <summary>
    /// Changes the appearance of the text-mode cursor.
    /// </summary>
    /// <param name="topOptions">Top scan line and options.</param>
    /// <param name="bottom">Bottom scan line.</param>
    private void SetCursorShape(int topOptions, int bottom)
    {
        int mode = (topOptions >> 4) & 3;
        VirtualMachine.IsCursorVisible = mode != 2;
    }
    /// <summary>
    /// Writes values to the static functionality table in emulated memory.
    /// </summary>
    private void InitializeStaticFunctionalityTable()
    {
        Memory memory = VirtualMachine.Memory;
        memory.SetUInt32(StaticFunctionalityTableSegment, 0, 0x000FFFFF); // supports all video modes
        memory.SetByte(StaticFunctionalityTableSegment, 0x07, 0x07); // supports all scanlines
    }
    /// <summary>
    /// Writes a table of information about the current video mode.
    /// </summary>
    private void GetFunctionalityInfo()
    {
        ushort segment = VirtualMachine.Cpu.State.ES;
        ushort offset = VirtualMachine.Cpu.State.DI;

        Memory memory = VirtualMachine.Memory;
        Bios bios = memory.Bios;

        Point cursorPos = TextConsole.CursorPosition;

        memory.SetUInt32(segment, offset, StaticFunctionalityTableSegment << 16); // SFT address
        memory.SetByte(segment, offset + 0x04u, (byte)bios.VideoMode); // video mode
        memory.SetUInt16(segment, offset + 0x05u, bios.ScreenColumns); // columns
        memory.SetUInt32(segment, offset + 0x07u, 0); // regen buffer
        for (uint i = 0; i < 8; i++)
        {
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
        VirtualMachine.Cpu.State.AL = 0x1B;
    }

    private void Dispose()
    {
        if (!this.disposed)
        {
            unsafe
            {
                if (this.VideoRam != IntPtr.Zero)
                    NativeMemory.Free(this.VideoRam.ToPointer());
            }

            this.disposed = true;
        }
    }

    /// <summary>
    /// Returns the current value of the input status 1 register.
    /// </summary>
    /// <returns>Current value of the input status 1 register.</returns>
    private static byte GetInputStatus1Value()
    {
        uint value = DualPic.IsInRealtimeInterval(VerticalBlankingTime, RefreshRate) ? 0x09u : 0x00u;
        if (DualPic.IsInRealtimeInterval(HorizontalBlankingTime, HorizontalPeriod))
            value |= 0x01u;

        return (byte)value;
    }
}
