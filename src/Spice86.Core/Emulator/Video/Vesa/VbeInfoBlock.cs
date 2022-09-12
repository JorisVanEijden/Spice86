using System.Runtime.InteropServices;

namespace Spice86.Core.Emulator.Video.Vesa;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VbeInfoBlock
{
    public uint VbeSignature;
    public ushort VbeVersion;
    public uint OemStringPtr;
    public uint Capabilities;
    public uint VideoModePtr;
    public ushort TotalMemory;

    public ushort OemSoftwareRev;
    public uint OemVendorNamePtr;
    public uint OemProductNamePtr;
    public uint OemProductRevPtr;
}
