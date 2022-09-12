namespace Spice86.Core.Emulator.Video.Vesa;

internal static class VesaFunctions
{
    public const byte ReturnVBEControllerInformation = 0x00;
    public const byte ReturnSVGAModeInformation = 0x01;
    public const byte SetSVGAVideoMode = 0x02;
    public const byte MemoryWindowControl = 0x05;
    public const byte DisplayStartControl = 0x07;
}
