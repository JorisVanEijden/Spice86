namespace Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;

using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Indexable;

/// <summary>
/// This is a memory based FIFO Queue used to store key codes. <br/>
/// Data about buffer start, and end positions is stored in the Bios Data Area.
/// </summary>
public class BiosKeyboardBuffer {
    private readonly IIndexable _memory;
    private readonly BiosDataArea _biosDataArea;

    /// <summary>
    /// Initializes a new instance.
    /// </summary>
    /// <param name="memory">The memory bus.</param>
    /// <param name="biosDataArea">The memory mapped BIOS values.</param>
    public BiosKeyboardBuffer(IIndexable memory, BiosDataArea biosDataArea) {
        _memory = memory;
        _biosDataArea = biosDataArea;
        // Offsets from segment 0040h, as a real BIOS stores them (0x001E / 0x003E), NOT absolute
        // addresses. Programs that own INT 9 and read the ring directly hard-code that space: Betrayal
        // at Krondor's isr_keyboard wraps its tail only at 0x3C and kbhit_read reads 0040:[head].
        // Absolute values (0x041E) let such a handler's tail run past the ring after ~16 keys while the
        // reader kept wrapping at 0x043E, so every later key went unread and stale ones replayed.
        StartAddress = (ushort)(_biosDataArea.KbdBuf.BaseAddress - _biosDataArea.BaseAddress);
        EndAddress = (ushort)(StartAddress + _biosDataArea.KbdBuf.Count);
        HeadAddress = StartAddress;
        TailAddress = StartAddress;
    }

    /// <summary>
    /// Address of the start of the buffer
    /// </summary>
    private ushort StartAddress { get => _biosDataArea.KbdBufStartOffset; set => _biosDataArea.KbdBufStartOffset = value; }

    /// <summary>
    /// Address of the end of the buffer
    /// </summary>
    private ushort EndAddress { get => _biosDataArea.KbdBufEndOffset; set => _biosDataArea.KbdBufEndOffset = value; }

    /// <summary>
    /// Address where newest item is enqueued
    /// </summary>
    private ushort HeadAddress { get => _biosDataArea.KbdBufHead; set => _biosDataArea.KbdBufHead = value; }

    /// <summary>
    /// Address where the oldest item is enqueued
    /// </summary>
    private ushort TailAddress { get => _biosDataArea.KbdBufTail; set => _biosDataArea.KbdBufTail = value; }

    /// <summary>
    /// Returns whether there is any keycode in the buffer or not
    /// </summary>
    public bool IsEmpty {
        get {
            int head = HeadAddress;
            int tail = TailAddress;
            return head == tail;
        }
    }

    /// <summary>
    /// Enqueues the keycode in the buffer
    /// </summary>
    /// <param name="code">keycode to enqueue</param>
    /// <returns>false when buffer is full, true otherwise</returns>
    public bool EnqueueKeyCode(ushort code) {
        ushort newTail = ComputeNextAddress(TailAddress);
        if (newTail == HeadAddress) {
            // buffer full
            return false;
        }

        _memory.UInt16[MemoryMap.BiosDataSegment, TailAddress] = code;
        TailAddress = newTail;
        return true;
    }

    /// <summary>
    /// Flushes the buffer, resetting the head and tail addresses to the start of the buffer.
    /// </summary>
    public void Flush() {
        // Reset the head and tail addresses to the start of the buffer
        HeadAddress = StartAddress;
        TailAddress = StartAddress;
    }

    /// <summary>
    /// Dequeues the most recent key code from the buffer
    /// </summary>
    /// <returns>the keycode or null if buffer was empty</returns>
    public ushort? DequeueKeyCode() {
        ushort? res = PeekKeyCode();
        if (res is null) {
            // Don't dequeue if nothing in the buffer
            return null;
        }
        HeadAddress = ComputeNextAddress(HeadAddress);
        return res;
    }

    /// <summary>
    /// Peeks at the pending keycode in the BIOS keyboard buffer, and returns it without dequeuing it
    /// </summary>
    /// <returns>The pending keycode, or <c>null</c> if <see cref="IsEmpty"/> is <c>True</c>.</returns>
    public ushort? PeekKeyCode() {
        if (IsEmpty) {
            return null;
        }

        return _memory.UInt16[MemoryMap.BiosDataSegment, HeadAddress];
    }

    private ushort ComputeNextAddress(ushort address) {
        ushort next = (ushort)(address + 2);
        if (next >= EndAddress) {
            return StartAddress;
        }
        return next;
    }
}