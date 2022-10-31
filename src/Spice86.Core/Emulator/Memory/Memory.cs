namespace Spice86.Core.Emulator.Memory;

using Spice86.Core;
using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.Errors;
using Spice86.Core.Emulator.Fonts;
using Spice86.Core.Emulator.InterruptHandlers.Video;
using Spice86.Core.Emulator.VM;
using Spice86.Core.Emulator.VM.Breakpoint;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

/// <summary> Addressable memory of the machine. </summary>
public class Memory {
    /// <summary>
    /// Starting physical address of video RAM.
    /// </summary>
    private const int VramAddress = 0xA000 << 4;

    /// <summary>
    /// The highest address which is mapped to <see cref="VgaCard"/>.
    /// </summary>
    /// <remarks>
    /// Video RAM mapping is technically up to 0xBFFF0 normally.
    /// </remarks>
    private const int VramUpperBound = 0xBFFF << 4;

    /// <summary>
    /// Segment where font data is stored.
    /// </summary>
    internal const ushort FontSegment = 0xC000;
    /// <summary>
    /// Offset into the font segment where the 8x8 font is found.
    /// </summary>
    internal const ushort Font8x8Offset = 0x0100;
    /// <summary>
    /// Offset into the font segment where the 8x14 font is found.
    /// </summary>
    internal const ushort Font8x14Offset = 0x0900;
    /// <summary>
    /// Offset into the font segment where the 8x16 font is found.
    /// </summary>
    internal const ushort Font8x16Offset = 0x1700;
    /// <summary>
    /// Size of conventional memory in bytes.
    /// </summary>
    internal const uint ConvMemorySize = 1024 * 1024;

    private readonly BreakPointHolder _readBreakPoints = new();

    private readonly BreakPointHolder _writeBreakPoints = new();

    private readonly MetaAllocator _metaAllocator = new();

    // For breakpoints to access what is getting written
    public byte CurrentlyWritingByte { get; private set; } = 0;

    private uint addressMask = 0x000FFFFFu;

    /// <summary>
    /// Gets or sets the emulated video device.
    /// </summary>
    internal VideoBiosInt10Handler? Video { get; set; }

    internal Bios Bios { get; }


    private readonly Machine _machine;

    public Memory(Machine machine, uint memorySize) {
        if (memorySize < ConvMemorySize) {
            throw new ArgumentException("Memory size must be at least 1 MB.");
        }
        
        this.MemorySize = (int)memorySize;
        unsafe {
            fixed(byte* ramPtr = this.Ram)
            {
                this.RawView = ramPtr;
            }
        }
        this.MemorySize = (int)memorySize * 10000;
        
        // Reserve room for the real-mode interrupt table.
        this.Reserve(0x0000, 256 * 4);

        // Reserve VGA video RAM window.
        this.Reserve(0xA000, VramUpperBound - VramAddress + 16u);

        Bios = new Bios(this);
        _machine = machine;
        Ram = new byte[memorySize * 1024];
        UInt8 = new(this);
        UInt16 = new(this);
        UInt32 = new(this);
        
        //Reserve base memory
        uint length = this._metaAllocator.GetLargestFreeBlockSize();
        ushort segment = this._metaAllocator.Allocate(0x0000, (int)length);
        this.BaseMemory = new ReservedBlock(segment, length);
    }

    /// <summary>
    /// Reserves a block of conventional memory.
    /// </summary>
    /// <param name="minimumSegment">Minimum segment of requested memory block.</param>
    /// <param name="length">Size of memory block in bytes.</param>
    /// <returns>Information about the reserved block of memory.</returns>
    public ReservedBlock Reserve(ushort minimumSegment, uint length) {
        ushort allocation = _metaAllocator.Allocate(minimumSegment, (int)length);
        return new(allocation, length);
    }

    public Span<byte> GetSpan(int address, int length) {
        MonitorRangeReadAccess((uint)address, (uint)(address + length));
        return Ram.AsSpan(address, length);
    }

    public byte[] GetData(uint address, uint length) {
        MonitorRangeReadAccess(address, address + length);
        byte[] res = new byte[length];
        Array.Copy(Ram, address, res, 0, length);
        return res;
    }

    public byte[] Ram { get; private set; } = Array.Empty<byte>();

    public int Size => Ram.Length;

    public UInt8Indexer UInt8 { get; }
    public UInt16Indexer UInt16 { get; }
    public UInt32Indexer UInt32 { get; }

    public ushort GetUint16(uint address) {
        ushort res = MemoryUtils.GetUint16(Ram, address);
        MonitorReadAccess(address);
        MonitorReadAccess(address + 1);
        return res;
    }

    public uint GetUint32(uint address) {
        uint res = MemoryUtils.GetUint32(Ram, address);
        MonitorReadAccess(address);
        MonitorReadAccess(address + 1);
        MonitorReadAccess(address + 2);
        MonitorReadAccess(address + 3);
        return res;
    }

    public byte GetUint8(uint addr) {
        byte res = MemoryUtils.GetUint8(Ram, addr);
        MonitorReadAccess(addr);
        return res;
    }

    public void LoadData(uint address, byte[] data) {
        LoadData(address, data, data.Length);
    }

    public void LoadData(uint address, byte[] data, int length) {
        MonitorRangeWriteAccess(address, (uint)(address + length));
        Array.Copy(data, 0, Ram, address, length);
    }

    public void MemCopy(uint sourceAddress, uint destinationAddress, uint length) {
        MonitorRangeReadAccess(sourceAddress, sourceAddress + length);
        MonitorRangeWriteAccess(destinationAddress, destinationAddress + length);
        Array.Copy(Ram, sourceAddress, Ram, destinationAddress, length);
    }

    public void Memset(uint address, byte value, uint length) {
        MonitorRangeWriteAccess(address, address + length);
        Array.Fill(Ram, value, (int)address, (int)length);
    }

    public uint? SearchValue(uint address, int len, IList<byte> value) {
        int end = (int)(address + len);
        if (end >= Ram.Length) {
            end = Ram.Length;
        }

        for (long i = address; i < end; i++) {
            long endValue = value.Count;
            if (endValue + i >= Ram.Length) {
                endValue = Ram.Length - i;
            }

            int j = 0;
            while (j < endValue && Ram[i + j] == value[j]) {
                j++;
            }

            if (j == endValue) {
                return (uint)i;
            }
        }

        return null;
    }

    /// <summary>
    /// Pointer to the start of the emulated physical memory.
    /// </summary>
    internal unsafe byte* RawView { get; private set; }

    public Span<byte> GetSpan(uint segment, uint offset, int length) {
        unsafe {
            uint fullAddress = GetRealModePhysicalAddress(segment, offset);
            if (fullAddress is >= VramAddress and < VramUpperBound) {
                throw new ArgumentException("Not supported for video RAM mapped addresses.");
            }
            return new Span<byte>(RawView + fullAddress, length);
        }
    }

    private uint GetRealModePhysicalAddress(uint segment, uint offset) => ((segment << 4) + offset) & this.addressMask;

    /// <summary>
    /// Reads a byte from emulated memory.
    /// </summary>
    /// <param name="segment">Segment of byte to read.</param>
    /// <param name="offset">Offset of byte to read.</param>
    /// <returns>Byte at the specified segment and offset.</returns>
    public byte GetByte(uint segment, uint offset) => this.RealModeRead<byte>(segment, offset);
    /// <summary>
    /// Reads a byte from emulated memory.
    /// </summary>
    /// <param name="address">Physical address of byte to read.</param>
    /// <returns>Byte at the specified address.</returns>
    
    public byte GetByte(uint address)
    {
        return this.PhysicalRead<byte>(address);
    }

    /// <summary>
    /// Writes a byte to emulated memory.
    /// </summary>
    /// <param name="segment">Segment of byte to write.</param>
    /// <param name="offset">Offset of byte to write.</param>
    /// <param name="value">Value to write to the specified segment and offset.</param>
    public void SetByte(uint segment, uint offset, byte value) => this.RealModeWrite(segment, offset, value);
    /// <summary>
    /// Writes a byte to emulated memory.
    /// </summary>
    /// <param name="address">Physical address of byte to write.</param>
    /// <param name="value">Value to write to the specified address.</param>
    
    public void SetByte(uint address, byte value)
    {
        this.PhysicalWrite(address, value);
    }

    /// <summary>
    /// Reads an unsigned 16-bit integer from emulated memory.
    /// </summary>
    /// <param name="segment">Segment of unsigned 16-bit integer to read.</param>
    /// <param name="offset">Offset of unsigned 16-bit integer to read.</param>
    /// <returns>Unsigned 16-bit integer at the specified segment and offset.</returns>
    
    public ushort GetUInt16(uint segment, uint offset) => this.RealModeRead<ushort>(segment, offset);
    /// <summary>
    /// Reads an unsigned 16-bit integer from emulated memory.
    /// </summary>
    /// <param name="address">Physical address of unsigned 16-bit integer to read.</param>
    /// <returns>Unsigned 16-bit integer at the specified address.</returns>
    
    public ushort GetUInt16(uint address)
    {
        return this.PhysicalRead<ushort>(address);
    }

    /// <summary>
    /// Writes an unsigned 16-bit integer to emulated memory.
    /// </summary>
    /// <param name="segment">Segment of unsigned 16-bit integer to write.</param>
    /// <param name="offset">Offset of unsigned 16-bit integer to write.</param>
    /// <param name="value">Value to write to the specified segment and offset.</param>
    public void SetUInt16(uint segment, uint offset, ushort value) => this.RealModeWrite(segment, offset, value);
    /// <summary>
    /// Writes an unsigned 16-bit integer to emulated memory.
    /// </summary>
    /// <param name="address">Physical address of unsigned 16-bit integer to write.</param>
    /// <param name="value">Value to write to the specified address.</param>
    
    public void SetUInt16(uint address, ushort value)
    {
        this.PhysicalWrite(address, value);
    }

    /// <summary>
    /// Reads an unsigned 32-bit integer from emulated memory.
    /// </summary>
    /// <param name="segment">Segment of unsigned 32-bit integer to read.</param>
    /// <param name="offset">Offset of unsigned 32-bit integer to read.</param>
    /// <returns>Unsigned 32-bit integer at the specified segment and offset.</returns>
    public uint GetUInt32(uint segment, uint offset) => this.RealModeRead<uint>(segment, offset);
    /// <summary>
    /// Reads an unsigned 32-bit integer from emulated memory.
    /// </summary>
    /// <param name="address">Physical address of unsigned 32-bit integer to read.</param>
    /// <returns>Unsigned 32-bit integer at the specified address.</returns>
    
    public uint GetUInt32(uint address)
    {
        return this.PhysicalRead<uint>(address);
    }

    /// <summary>
    /// Writes an unsigned 32-bit integer to emulated memory.
    /// </summary>
    /// <param name="segment">Segment of unsigned 32-bit integer to write.</param>
    /// <param name="offset">Offset of unsigned 32-bit integer to write.</param>
    /// <param name="value">Value to write to the specified segment and offset.</param>
    public void SetUInt32(uint segment, uint offset, uint value) => this.RealModeWrite(segment, offset, value);

    /// <summary>
    /// Writes an unsigned 32-bit integer to emulated memory.
    /// </summary>
    /// <param name="address">Physical address of unsigned 32-bit integer to write.</param>
    /// <param name="value">Value to write to the specified address.</param>
    
    public void SetUInt32(uint address, uint value)
    {
        this.PhysicalWrite(address, value);
    }

    /// <summary>
    /// Reads an unsigned 64-bit integer from emulated memory.
    /// </summary>
    /// <param name="segment">Segment of unsigned 64-bit integer to read.</param>
    /// <param name="offset">Offset of unsigned 64-bit integer to read.</param>
    /// <returns>Unsigned 64-bit integer at the specified segment and offset.</returns>
    public ulong GetUInt64(uint segment, uint offset) => RealModeRead<ulong>(segment, offset);
    /// <summary>
    /// Reads an unsigned 64-bit integer from emulated memory.
    /// </summary>
    /// <param name="address">Physical address of unsigned 64-bit integer to read.</param>
    /// <returns>Unsigned 64-bit integer at the specified address.</returns>
    
    public ulong GetUInt64(uint address)
    {
        return this.PhysicalRead<ulong>(address);
    }

    /// <summary>
    /// Writes an unsigned 64-bit integer to emulated memory.
    /// </summary>
    /// <param name="segment">Segment of unsigned 64-bit integer to write.</param>
    /// <param name="offset">Offset of unsigned 64-bit integer to write.</param>
    /// <param name="value">Value to write to the specified segment and offset.</param>
    public void SetUInt64(uint segment, uint offset, ulong value) => this.RealModeWrite(segment, offset, value);
    /// <summary>
    /// Writes an unsigned 64-bit integer to emulated memory.
    /// </summary>
    /// <param name="address">Physical address of unsigned 64-bit integer to write.</param>
    /// <param name="value">Value to write to the specified address.</param>
    
    public void SetUInt64(uint address, ulong value)
    {
        this.PhysicalWrite(address, value);
    }

    /// <summary>
    /// Returns a System.Single value read from an address in emulated memory.
    /// </summary>
    /// <param name="address">Address of value to read.</param>
    /// <returns>32-bit System.Single value read from the specified address.</returns>
    
    public float GetReal32(uint address)
    {
        return this.PhysicalRead<float>(address);
    }

    /// <summary>
    /// Writes a System.Single value to an address in emulated memory.
    /// </summary>
    /// <param name="address">Address where value will be written.</param>
    /// <param name="value">32-bit System.Single value to write at the specified address.</param>
    
    public void SetReal32(uint address, float value)
    {
        this.PhysicalWrite(address, value);
    }

    /// <summary>
    /// Returns a System.Double value read from an address in emulated memory.
    /// </summary>
    /// <param name="address">Address of value to read.</param>
    /// <returns>64-bit System.Double value read from the specified address.</returns>
    
    public double GetReal64(uint address)
    {
        return this.PhysicalRead<double>(address);
    }

    /// <summary>
    /// Writes a System.Double value to an address in emulated memory.
    /// </summary>
    /// <param name="address">Address where value will be written.</param>
    /// <param name="value">64-bit System.Double value to write at the specified address.</param>
    
    public void SetReal64(uint address, double value)
    {
        this.PhysicalWrite(address, value);
    }

    /// <summary>
    /// Returns a Real10 value read from an address in emulated memory.
    /// </summary>
    /// <param name="address">Address of value to read.</param>
    /// <returns>80-bit Real10 value read from the specified address.</returns>
    public Real10 GetReal80(uint address)
    {
        return this.PhysicalRead<Real10>(address);
    }
    /// <summary>
    /// Writes a Real10 value to an address in emulated memory.
    /// </summary>
    /// <param name="address">Address where value will be written.</param>
    /// <param name="value">80-bit Real10 value to write at the specified address.</param>
    public void SetReal80(uint address, Real10 value)
    {
        this.PhysicalWrite(address, value);
    }


    /// <summary>
    /// Gets a pointer to a location in the emulated memory.
    /// </summary>
    /// <param name="segment">Segment of pointer.</param>
    /// <param name="offset">Offset of pointer.</param>
    /// <returns>Pointer to the emulated location at segment:offset.</returns>
    public IntPtr GetPointer(uint segment, uint offset)
    {
        unsafe
        {
            return new IntPtr(RawView + GetRealModePhysicalAddress(segment, offset));
        }
    }
    /// <summary>
    /// Gets a pointer to a location in the emulated memory.
    /// </summary>
    /// <param name="address">Address of pointer.</param>
    /// <returns>Pointer to the specified address.</returns>
    public IntPtr GetPointer(int address)
    {
        address &= (int)addressMask;

        unsafe
        {
            return new IntPtr(RawView + address);
        }
    }

    /// <summary>
    /// Reads an ANSI string from emulated memory with a specified length.
    /// </summary>
    /// <param name="segment">Segment of string to read.</param>
    /// <param name="offset">Offset of string to read.</param>
    /// <param name="length">Length of the string in bytes.</param>
    /// <returns>String read from the specified segment and offset.</returns>
    public string GetString(uint segment, uint offset, int length)
    {
        IntPtr ptr = GetPointer(segment, offset);
        return Marshal.PtrToStringAnsi(ptr, length);
    }
    /// <summary>
    /// Reads an ANSI string from emulated memory with a maximum length and end sentinel character.
    /// </summary>
    /// <param name="segment">Segment of string to read.</param>
    /// <param name="offset">Offset of string to read.</param>
    /// <param name="maxLength">Maximum number of bytes to read.</param>
    /// <param name="sentinel">End sentinel character of the string to read.</param>
    /// <returns>String read from the specified segment and offset.</returns>
    public string GetString(uint segment, uint offset, int maxLength, byte sentinel)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(maxLength);
        try
        {
            uint i;

            for (i = 0; i < maxLength; i++)
            {
                byte value = this.GetByte(segment, offset + i);
                if (value == sentinel) {
                    break;
                }

                buffer[i] = value;
            }

            return Encoding.Latin1.GetString(buffer.AsSpan(0, (int)i));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    /// <summary>
    /// Writes a string to memory as a null-terminated ANSI byte array.
    /// </summary>
    /// <param name="segment">Segment to write string.</param>
    /// <param name="offset">Offset to write string.</param>
    /// <param name="value">String to write to the specified address.</param>
    /// <param name="writeNull">Value indicating whether a null should be written after the string.</param>
    public void SetString(uint segment, uint offset, string value, bool writeNull)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(value.Length);
        try
        {
            uint length = (uint)Encoding.Latin1.GetBytes(value, buffer);
            for (uint i = 0; i < length; i++) {
                this.SetByte(segment, offset + i, buffer[(int)i]);
            }

            if (writeNull) {
                this.SetByte(segment, offset + length, 0);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private T RealModeRead<T>(uint segment, uint offset) where T : unmanaged => PhysicalRead<T>(GetRealModePhysicalAddress(segment, offset));
    private void RealModeWrite<T>(uint segment, uint offset, T value) where T : unmanaged => this.PhysicalWrite(this.GetRealModePhysicalAddress(segment, offset), value);

    private T PhysicalRead<T>(uint address, bool mask = true, bool checkVram = true) where T : unmanaged
    {
        uint fullAddress = mask ? (address & this.addressMask) : address;

        unsafe
        {
            if (this.Ems != null && fullAddress is >= (ExpandedMemoryManager.PageFrameSegment << 4) and < (ExpandedMemoryManager.PageFrameSegment << 4) + 65536)
            {
                return Unsafe.ReadUnaligned<T>(this.RawView + this.Ems.GetMappedAddress(address));
            }
            else if (!checkVram || fullAddress is < VramAddress or >= VramUpperBound)
            {
                return Unsafe.ReadUnaligned<T>(this.RawView + fullAddress);
            }
            else
            {
                if (sizeof(T) == 1)
                {
                    byte? b = this._machine.VgaCard.GetVramByte(fullAddress - VramAddress);
                    if (b is null) {
                        byte fallback = 0;
                        return Unsafe.As<byte, T>(ref fallback);
                    } else {
                        byte value = b.Value;
                        return Unsafe.As<byte, T>(ref value);
                    }
                }
                else if (sizeof(T) == 2)
                {
                    ushort? s = this._machine.VgaCard.GetVramWord(fullAddress - VramAddress);
                    if (s is null) {
                        ushort fallback = 0;
                        return Unsafe.As<ushort, T>(ref fallback);
                    } else {
                        ushort value = s.Value;
                        return Unsafe.As<ushort, T>(ref value);
                    }
                }
                else
                {
                    uint? i = this._machine.VgaCard.GetVramDWord(fullAddress - VramAddress);
                    if (i is null) {
                        uint fallback = 0;
                        return Unsafe.As<uint, T>(ref fallback);
                    } else {
                        uint value = i.Value;
                        return Unsafe.As<uint, T>(ref value);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the EMS handler.
    /// </summary>
    internal ExpandedMemoryManager? Ems { get; set; }
    public bool EnableA20 { get; internal set; }
    public int MemorySize { get; }

    /// <summary>
    /// Gets the location and size of base memory in the system.
    /// </summary>
    public ReservedBlock BaseMemory { get; private set; }

    /// <summary>
    /// Reserves the largest free block of conventional memory as base memory.
    /// </summary>
    internal void ReserveBaseMemory()
    {
        uint length = this._metaAllocator.GetLargestFreeBlockSize();
        ushort segment = this._metaAllocator.Allocate(0x0000, (int)length);
        this.BaseMemory = new ReservedBlock(segment, length);
    }

    /// <summary>
    /// Gets the entire emulated RAM as a <see cref="Span{byte}"/>.
    /// </summary>
    public Span<byte> Span {
        get {
            unsafe {
                return new Span<byte>(this.RawView, this.MemorySize);
            }
        }
    }

    private void PhysicalWrite<T>(uint address, T value, bool mask = true) where T : unmanaged
    {
        uint fullAddress = mask ? (address & this.addressMask) : address;

        unsafe
        {
            if (this.Ems != null && fullAddress is >= (ExpandedMemoryManager.PageFrameSegment << 4) and < (ExpandedMemoryManager.PageFrameSegment << 4) + 65536)
            {
                Unsafe.WriteUnaligned(this.RawView + this.Ems.GetMappedAddress(address), value);
            }
            else if (fullAddress is < VramAddress or > VramUpperBound)
            {
                Unsafe.WriteUnaligned(this.RawView + fullAddress, value);
            }
            else if(this.Video is not null)
            {
                if (sizeof(T) == 1) {
                    this._machine.VgaCard.SetVramByte(fullAddress - VramAddress, Unsafe.As<T, byte>(ref value));
                } else if (sizeof(T) == 2) {
                    this._machine.VgaCard.SetVramWord(fullAddress - VramAddress, Unsafe.As<T, ushort>(ref value));
                } else {
                    this._machine.VgaCard.SetVramDWord(fullAddress - VramAddress, Unsafe.As<T, uint>(ref value));
                }
            }
        }
    }

    /// <summary>
    /// Gets the address of an interrupt handler.
    /// </summary>
    /// <param name="interrupt">Interrupt to get handler address for.</param>
    /// <returns>Segment and offset of the interrupt handler.</returns>
    public SegmentedAddress GetRealModeInterruptAddress(byte interrupt)
    {
        ushort offset = GetUInt16(0, (ushort)(interrupt * 4));
        ushort segment = GetUInt16(0, (ushort)(interrupt * 4 + 2));
        return new SegmentedAddress(segment, offset);
    }

    /// <summary>
    /// Writes a new address to the interrupt table.
    /// </summary>
    /// <param name="interrupt">Interrupt to set handler address for.</param>
    /// <param name="segment">Segment of the interrupt handler.</param>
    /// <param name="offset">Offset of the interrupt handler.</param>
    public void SetInterruptAddress(byte interrupt, ushort segment, ushort offset)
    {
        SetUInt16(0, (ushort)(interrupt * 4), offset);
        SetUInt16(0, (ushort)(interrupt * 4 + 2), segment);
    }

    /// <summary>
    /// Copies font data to emulated memory.
    /// </summary>
    internal void InitializeFonts()
    {
        ReadOnlySpan<byte> ibm8x8 = Fonts.IBM8x8;
        Span<byte> destination = this.GetSpan(FontSegment, Font8x8Offset, ibm8x8.Length);
        ibm8x8.CopyTo(destination);

        Reserve(0xF000, (uint)ibm8x8.Length / 2u);

        SetInterruptAddress(0x43, 0xF000, 0xFA6E);

        // Only the first half of the 8x8 font should go here.
        Span<byte> destinationFirstHalf = this.GetSpan(0xF000, 0xFA6E, ibm8x8.Length / 2);
        ibm8x8[..(ibm8x8.Length / 2)].CopyTo(destinationFirstHalf);

        Fonts.VGA8x16.CopyTo(this.GetSpan(FontSegment, Font8x16Offset, Fonts.VGA8x16.Length));

        Fonts.EGA8x14.CopyTo(this.GetSpan(FontSegment, Font8x14Offset, Fonts.EGA8x14.Length));
    }

    /// <summary>
    /// Segment for interrupt/callback proxies.
    /// </summary>
    private const ushort HandlerSegment = 0xF100;

    /// <summary>
    /// Address of the BIOS int15h C0 data table.
    /// </summary>
    internal static readonly SegmentedAddress BiosConfigurationAddress = new(0xF000, 0x0100);
    /// <summary>
    /// Address of the default interrupt handler.
    /// </summary>
    internal static readonly SegmentedAddress NullInterruptHandler = new(HandlerSegment, 4095);

    /// <summary>
    /// Writes static BIOS configuration data to emulated memory.
    /// </summary>
    internal void InitializeBiosData()
    {
        ushort segment = BiosConfigurationAddress.Segment;
        ushort offset = BiosConfigurationAddress.Offset;

        SetUInt16(segment, offset, 8);
        SetByte(segment, offset + 2u, 0xFC);
        SetByte(segment, offset + 5u, 0x70);
        SetByte(segment, offset + 6u, 0x40);
    }

    /// <summary>
    /// Writes a string to memory as a null-terminated ANSI byte array.
    /// </summary>
    /// <param name="segment">Segment to write string.</param>
    /// <param name="offset">Offset to write string.</param>
    /// <param name="value">String to write to the specified address.</param>
    public void SetString(uint segment, uint offset, string value) => SetString(segment, offset, value, true);

    public void SetUint16(uint address, ushort value) {
        byte value0 = (byte)value;
        MonitorWriteAccess(address, value0);
        Ram[address] = value0;

        byte value1 = (byte)(value >> 8);
        MonitorWriteAccess(address + 1, value1);
        Ram[address + 1] = value1;
    }

    public void SetUint32(uint address, uint value) {
        byte value0 = (byte)value;
        MonitorWriteAccess(address, value0);
        Ram[address] = value0;

        byte value1 = (byte)(value >> 8);
        MonitorWriteAccess(address + 1, value1);
        Ram[address + 1] = value1;

        byte value2 = (byte)(value >> 16);
        MonitorWriteAccess(address + 2, value2);
        Ram[address + 2] = value2;

        byte value3 = (byte)(value >> 24);
        MonitorWriteAccess(address + 3, value3);
        Ram[address + 3] = value3;
    }

    public void SetUint8(uint address, byte value) {
        MonitorWriteAccess(address, value);
        MemoryUtils.SetUint8(Ram, address, value);
    }

    public void ToggleBreakPoint(BreakPoint breakPoint, bool on) {
        BreakPointType? type = breakPoint.BreakPointType;
        switch (type) {
            case BreakPointType.READ:
                _readBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.WRITE:
                _writeBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            case BreakPointType.ACCESS:
                _readBreakPoints.ToggleBreakPoint(breakPoint, on);
                _writeBreakPoints.ToggleBreakPoint(breakPoint, on);
                break;
            default:
                throw new UnrecoverableException($"Trying to add unsupported breakpoint of type {type}");
        }
    }

    private void MonitorRangeReadAccess(uint startAddress, uint endAddress) {
        _readBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

    private void MonitorRangeWriteAccess(uint startAddress, uint endAddress) {
        _writeBreakPoints.TriggerBreakPointsWithAddressRange(startAddress, endAddress);
    }

    private void MonitorReadAccess(uint address) {
        _readBreakPoints.TriggerMatchingBreakPoints(address);
    }

    private void MonitorWriteAccess(uint address, byte value) {
        CurrentlyWritingByte = value;
        _writeBreakPoints.TriggerMatchingBreakPoints(address);
    }
}