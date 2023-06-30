namespace Spice86.Core.Emulator.Memory;

using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.Shared.Emulator.Errors;

/// <summary>
/// Represents the memory bus of the IBM PC.
/// </summary>
public interface IMemory {
    /// <summary>
    /// Represents the optional 20th address line suppression feature for legacy 8086 programs.
    /// </summary>
    A20Gate A20Gate { get; }
    
    /// <summary>
    /// Gets a copy of the current memory state.
    /// </summary>
    public byte[] Ram { get; }

    /// <summary>
    ///     Writes a 4-byte value to ram.
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    public void SetUint32(uint address, uint value);

    /// <summary>
    ///     Writes a 2-byte value to ram.
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    public void SetUint16(uint address, ushort value);

    /// <summary>
    ///     Writes a 1-byte value to ram.
    /// </summary>
    /// <param name="address">The address to write to</param>
    /// <param name="value">The value to write</param>
    public void SetUint8(uint address, byte value);

    /// <summary>
    ///     Read a 4-byte value from ram.
    /// </summary>
    /// <param name="address">The address to read from</param>
    /// <returns>The value at that address</returns>
    public uint GetUint32(uint address);

    /// <summary>
    /// Returns a <see cref="Span{T}"/> that represents the specified range of memory.
    /// </summary>
    /// <param name="address">The starting address of the memory range.</param>
    /// <param name="length">The length of the memory range.</param>
    /// <returns>A <see cref="Span{T}"/> instance that represents the specified range of memory.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no memory device supports the specified memory range.</exception>
    public Span<byte> GetSpan(int address, int length);

    /// <summary>
    /// Returns an array of bytes read from RAM.
    /// </summary>
    /// <param name="address">The start address.</param>
    /// <param name="length">The length of the array.</param>
    /// <returns>The array of bytes, read from RAM.</returns>
    public byte[] GetData(uint address, uint length);

    /// <summary>
    ///     Read a 2-byte value from ram.
    /// </summary>
    /// <param name="address">The address to read from</param>
    /// <returns>The value at that address</returns>
    public ushort GetUint16(uint address);

    /// <summary>
    ///     Read a 1-byte value from ram.
    /// </summary>
    /// <param name="address">The address to read from</param>
    /// <returns>The value at that address</returns>
    public byte GetUint8(uint address);

    /// <summary>
    ///     Load data from a byte array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of bytes to write</param>
    public void LoadData(uint address, byte[] data);

    /// <summary>
    ///     Load data from a byte array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of bytes to write</param>
    /// <param name="length">How many bytes to read from the byte array</param>
    public void LoadData(uint address, byte[] data, int length);

    /// <summary>
    ///     Load data from a words array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of words to write</param>
    public void LoadData(uint address, ushort[] data);

    /// <summary>
    ///     Load data from a word array into memory.
    /// </summary>
    /// <param name="address">The memory address to start writing</param>
    /// <param name="data">The array of words to write</param>
    /// <param name="length">How many words to read from the byte array</param>
    public void LoadData(uint address, ushort[] data, int length);

    /// <summary>
    ///     Copy bytes from one memory address to another.
    /// </summary>
    /// <param name="sourceAddress">The address in memory to start reading from</param>
    /// <param name="destinationAddress">The address in memory to start writing to</param>
    /// <param name="length">How many bytes to copy</param>
    public void MemCopy(uint sourceAddress, uint destinationAddress, uint length);

    /// <summary>
    ///     Fill a range of memory with a value.
    /// </summary>
    /// <param name="address">The memory address to start writing to</param>
    /// <param name="value">The byte value to write</param>
    /// <param name="amount">How many times to write the value</param>
    public void Memset(uint address, byte value, uint amount);

    /// <summary>
    ///     Fill a range of memory with a value.
    /// </summary>
    /// <param name="address">The memory address to start writing to</param>
    /// <param name="value">The ushort value to write</param>
    /// <param name="amount">How many times to write the value</param>
    public void Memset(uint address, ushort value, uint amount);

    /// <summary>
    ///     Find the address of a value in memory.
    /// </summary>
    /// <param name="address">The address in memory to start the search from</param>
    /// <param name="len">The maximum amount of memory to search</param>
    /// <param name="value">The sequence of bytes to search for</param>
    /// <returns>The address of the first occurence of the specified sequence of bytes, or null if not found.</returns>
    uint? SearchValue(uint address, int len, IList<byte> value);

    /// <summary>
    ///     Enable or disable a memory breakpoint.
    /// </summary>
    /// <param name="breakPoint">The breakpoint to enable or disable</param>
    /// <param name="on">true to enable a breakpoint, false to disable it</param>
    /// <exception cref="NotSupportedException"></exception>
    void ToggleBreakPoint(BreakPoint breakPoint, bool on);
    
    /// <summary>
    ///     Allows write breakpoints to access the byte being written before it actually is.
    /// </summary>
    byte CurrentlyWritingByte {
        get;
        set;
    }

    /// <summary>
    ///     The number of bytes in the memory map.
    /// </summary>
    int Size { get; }

    /// <summary>
    ///     Allows indexed byte access to the memory map.
    /// </summary>
    public UInt8Indexer UInt8 { get; }

    /// <summary>
    ///     Allows indexed word access to the memory map.
    /// </summary>
    public UInt16Indexer UInt16 { get; }

    /// <summary>
    ///     Allows indexed double word access to the memory map.
    /// </summary>
    public UInt32Indexer UInt32 { get; }

    /// <summary>
    ///     Allow a class to register for a certain memory range.
    /// </summary>
    /// <param name="baseAddress">The start of the frame</param>
    /// <param name="size">The size of the window</param>
    /// <param name="memoryDevice">The memory device to use</param>
    public void RegisterMapping(uint baseAddress, uint size, IMemoryDevice memoryDevice);

    /// <summary>
    /// Read a string from memory.
    /// </summary>
    /// <param name="address">The address in memory from where to read</param>
    /// <param name="maxLength">The maximum string length</param>
    /// <returns></returns>
    public string GetZeroTerminatedString(uint address, int maxLength);

    /// <summary>
    /// Writes a string directly to memory.
    /// </summary>
    /// <param name="address">The address at which to write the string</param>
    /// <param name="value">The string to write</param>
    /// <param name="maxLength">The maximum length to write</param>
    /// <exception cref="UnrecoverableException"></exception>
    public void SetZeroTerminatedString(uint address, string value, int maxLength);

    /// <summary>
    /// Gets a string from memory.
    /// </summary>
    /// <param name="address">The start address</param>
    /// <param name="length">The length, in bytes</param>
    /// <returns>The string from memory.</returns>
    public string GetString(uint address, int length);
}