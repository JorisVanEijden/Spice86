﻿// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using Spice86.Core.Emulator.InterruptHandlers.Video;

namespace Spice86.Core.Emulator.Video.Modes;
public class CgaMode4 : VideoMode
{
    private const uint BaseAddress = 0x18000;
    private unsafe readonly byte* videoRam;

    public CgaMode4(VideoBiosInt10Handler video) : base(320, 200, 2, false, 8, VideoModeType.Graphics, video)
    {
        unsafe
        {
            this.videoRam = video.RawView;
        }
    }

    public override int Stride => 80;

    internal override byte GetVramByte(uint offset)
    {
        offset -= BaseAddress;
        unsafe
        {
            return videoRam[offset];
        }
    }
    internal override void SetVramByte(uint offset, byte value)
    {
        offset -= BaseAddress;
        unsafe
        {
            videoRam[offset] = value;
        }
    }
    internal override ushort GetVramWord(uint offset)
    {
        offset -= BaseAddress;
        unsafe
        {
            return *(ushort*)(videoRam + offset);
        }
    }
    internal override void SetVramWord(uint offset, ushort value)
    {
        offset -= BaseAddress;
        unsafe
        {
            *(ushort*)(videoRam + offset) = value;
        }
    }
    internal override uint GetVramDWord(uint offset)
    {
        offset -= BaseAddress;
        unsafe
        {
            return *(uint*)(videoRam + offset);
        }
    }
    internal override void SetVramDWord(uint offset, uint value)
    {
        offset -= BaseAddress;
        unsafe
        {
            *(uint*)(videoRam + offset) = value;
        }
    }
    internal override void WriteCharacter(int x, int y, int index, byte foreground, byte background)
    {
        throw new NotImplementedException("WriteCharacter in CGA.");
    }
}
