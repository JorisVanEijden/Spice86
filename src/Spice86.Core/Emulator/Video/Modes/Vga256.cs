﻿using Spice86.Core.Emulator.Devices.Video;
using Spice86.Core.Emulator.InterruptHandlers.Video;

namespace Spice86.Core.Emulator.Video.Modes;

/// <summary>
/// Implements functionality for chained 8-bit 256-color VGA modes.
/// </summary>
public sealed class Vga256 : VideoMode
{
    private unsafe readonly byte* videoRam;

    public Vga256(int width, int height, VgaCard video) : base(width, height, 8, false, 8, VideoModeType.Graphics, video)
    {
        if(base.IsDisposed) {
            return;
        }
        unsafe
        {
            this.videoRam = (byte*)video.VideoRam;
        }
    }

    public override int MouseWidth => this.PixelWidth * 2;

    internal override byte GetVramByte(uint offset)
    {
        if (base.IsDisposed) {
            return 0;
        }
        unsafe {
            return videoRam[offset];
        }
    }
    internal override void SetVramByte(uint offset, byte value)
    {
        if (base.IsDisposed) {
            return;
        }
        unsafe {
            videoRam[offset] = value;
        }
    }
    internal override ushort GetVramWord(uint offset)
    {
        if (base.IsDisposed) {
            return 0;
        }
        unsafe {
            return *(ushort*)(videoRam + offset);
        }
    }
    internal override void SetVramWord(uint offset, ushort value)
    {
        if (base.IsDisposed) {
            return;
        }
        unsafe {
            *(ushort*)(videoRam + offset) = value;
        }
    }
    internal override uint GetVramDWord(uint offset)
    {
        if (base.IsDisposed) {
            return 0;
        }
        unsafe {
            return *(uint*)(videoRam + offset);
        }
    }
    internal override void SetVramDWord(uint offset, uint value)
    {
        if (base.IsDisposed) {
            return;
        }
        unsafe {
            *(uint*)(videoRam + offset) = value;
        }
    }
    internal override void WriteCharacter(int x, int y, int index, byte foreground, byte background)
    {
        if (base.IsDisposed) {
            return;
        }
        unsafe {
            int stride = this.Stride;
            int startPos = (y * stride * 8) + x * 8;
            byte[] font = this.Font;

            for (int row = 0; row < 8; row++)
            {
                uint value = font[index * 8 + row];
                int pos = startPos + (row * stride);

                for (int column = 0; column < 8; column++) {
                    this.videoRam[pos + column] = (value & (0x80 >> column)) != 0 ? foreground : background;
                }
            }
        }
    }
}
