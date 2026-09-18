namespace Spice86.Tests.Bios;

using FluentAssertions;

using Spice86.Core.Emulator.InterruptHandlers.Bios.Structures;
using Spice86.Core.Emulator.InterruptHandlers.Input.Keyboard;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.Memory.Mmu;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class BiosKeyboardBufferTests {
    private const ushort Bda = MemoryMap.BiosDataSegment;

    private readonly Memory _memory = new(new(), new Ram(64 * 1024), new A20Gate(), new RealModeMmu8086(), false);

    [Fact]
    public void RingPointersAreOffsetsFromTheBiosDataSegment() {
        BiosDataArea bda = new(_memory, 640);
        _ = new BiosKeyboardBuffer(_memory, bda);

        bda.KbdBufStartOffset.Should().Be(0x1E);
        bda.KbdBufEndOffset.Should().Be(0x3E);
        bda.KbdBufHead.Should().Be(0x1E);
        bda.KbdBufTail.Should().Be(0x1E);
    }

    [Fact]
    public void AnEnqueuedKeyLandsInTheRingAt0040_001E() {
        BiosDataArea bda = new(_memory, 640);
        BiosKeyboardBuffer buffer = new(_memory, bda);

        buffer.EnqueueKeyCode(0x4800);

        _memory.UInt16[Bda, 0x1E].Should().Be(0x4800);
        buffer.DequeueKeyCode().Should().Be(0x4800);
    }

    // A program that owns INT 9 and reads the ring itself, with the standard BIOS numbers hard-coded:
    // Betrayal at Krondor's isr_keyboard (enqueue, tail wraps only at 0x3C) and kbhit_read (dequeue,
    // head wraps at 0040:0082). With absolute pointers in the BDA the tail ran past the ring after
    // ~16 keys and every later key went unread.
    [Fact]
    public void AProgramThatHardCodesTheStandardRingKeepsReadingPastSixteenKeys() {
        BiosDataArea bda = new(_memory, 640);
        _ = new BiosKeyboardBuffer(_memory, bda);

        for (ushort key = 1; key <= 100; key++) {
            GameIsrEnqueue(key);
            GameKbhitRead().Should().Be(key);
            bda.KbdBufTail.Should().BeInRange(0x1E, 0x3C);
        }
    }

    private void GameIsrEnqueue(ushort code) {
        ushort head = _memory.UInt16[Bda, 0x1A];
        ushort tail = _memory.UInt16[Bda, 0x1C];
        bool full = head == 0x3C ? tail == 0x1E : (ushort)(head + 2) == tail;
        if (full) {
            return;
        }
        _memory.UInt16[Bda, tail] = code;
        if (tail == 0x3C) {
            tail = 0x1C;
        }
        _memory.UInt16[Bda, 0x1C] = (ushort)(tail + 2);
    }

    private ushort GameKbhitRead() {
        ushort head = _memory.UInt16[Bda, 0x1A];
        if (head == _memory.UInt16[Bda, 0x1C]) {
            return 0;
        }
        ushort code = _memory.UInt16[Bda, head];
        head += 2;
        if (head == _memory.UInt16[Bda, 0x82]) {
            head = _memory.UInt16[Bda, 0x80];
        }
        _memory.UInt16[Bda, 0x1A] = head;
        return code;
    }
}
