namespace Spice86.Core.Emulator.Devices.Sound.Ym7128b;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public static partial class YM7128B {
    public const int YM7128B_Float_Min = -1;
    public const int YM7128B_Float_Max = 1;
    private const string Version = "0.1.1";

    static readonly ReadOnlyCollection<sbyte> YM7128B_GainDecibel_Table = Array.AsReadOnly(new sbyte[]
    {
    -128,  //  0 = -oo
    - 60,  //  1
    - 58,  //  2
    - 56,  //  3
    - 54,  //  4
    - 52,  //  5
    - 50,  //  6
    - 48,  //  7
    - 46,  //  8
    - 44,  //  9
    - 42,  // 10
    - 40,  // 11
    - 38,  // 12
    - 36,  // 13
    - 34,  // 14
    - 32,  // 15
    - 30,  // 16
    - 28,  // 17
    - 26,  // 18
    - 24,  // 19
    - 22,  // 20
    - 20,  // 21
    - 18,  // 22
    - 16,  // 23
    - 14,  // 24
    - 12,  // 25
    - 10,  // 26
    -  8,  // 27
    -  6,  // 28
    -  4,  // 29
    -  2,  // 30
       0   // 31
});

    private static int GainFixedTable(double real) => (short)(real * (int)YM7128B_ImplementationSpecs.YM7128B_Gain_Max) & unchecked((short)YM7128B_ImplementationSpecs.YM7128B_Gain_Mask);

    static readonly ReadOnlyCollection<short> YM7128B_GainFixed_Table = Array.AsReadOnly(new short[]
{
    // Pseudo-negative gains
    (short)~GainFixedTable(0.000000000000000000),  // -oo dB-
    (short)~GainFixedTable(0.001000000000000000),  // -60 dB-
    (short)~GainFixedTable(0.001258925411794167),  // -58 dB-
    (short)~GainFixedTable(0.001584893192461114),  // -56 dB-
    (short)~GainFixedTable(0.001995262314968879),  // -54 dB-
    (short)~GainFixedTable(0.002511886431509579),  // -52 dB-
    (short)~GainFixedTable(0.003162277660168379),  // -50 dB-
    (short)~GainFixedTable(0.003981071705534973),  // -48 dB-
    (short)~GainFixedTable(0.005011872336272725),  // -46 dB-
    (short)~GainFixedTable(0.006309573444801930),  // -44 dB-
    (short)~GainFixedTable(0.007943282347242814),  // -42 dB-
    (short)~GainFixedTable(0.010000000000000000),  // -40 dB-
    (short)~GainFixedTable(0.012589254117941675),  // -38 dB-
    (short)~GainFixedTable(0.015848931924611134),  // -36 dB-
    (short)~GainFixedTable(0.019952623149688799),  // -34 dB-
    (short)~GainFixedTable(0.025118864315095794),  // -32 dB-
    (short)~GainFixedTable(0.031622776601683791),  // -30 dB-
    (short)~GainFixedTable(0.039810717055349734),  // -28 dB-
    (short)~GainFixedTable(0.050118723362727220),  // -26 dB-
    (short)~GainFixedTable(0.063095734448019331),  // -24 dB-
    (short)~GainFixedTable(0.079432823472428138),  // -22 dB-
    (short)~GainFixedTable(0.100000000000000006),  // -20 dB-
    (short)~GainFixedTable(0.125892541179416728),  // -18 dB-
    (short)~GainFixedTable(0.158489319246111343),  // -16 dB-
    (short)~GainFixedTable(0.199526231496887974),  // -14 dB-
    (short)~GainFixedTable(0.251188643150958013),  // -12 dB-
    (short)~GainFixedTable(0.316227766016837941),  // -10 dB-
    (short)~GainFixedTable(0.398107170553497203),  // - 8 dB-
    (short)~GainFixedTable(0.501187233627272244),  // - 6 dB-
    (short)~GainFixedTable(0.630957344480193250),  // - 4 dB-
    (short)~GainFixedTable(0.794328234724281490),  // - 2 dB-
    (short)~GainFixedTable(1.000000000000000000),  // - 0 dB-

    // Positive gains
    (short)+GainFixedTable(0.000000000000000000),  // -oo dB(short)+
    (short)+GainFixedTable(0.001000000000000000),  // -60 dB(short)+
    (short)+GainFixedTable(0.001258925411794167),  // -58 dB(short)+
    (short)+GainFixedTable(0.001584893192461114),  // -56 dB(short)+
    (short)+GainFixedTable(0.001995262314968879),  // -54 dB(short)+
    (short)+GainFixedTable(0.002511886431509579),  // -52 dB(short)+
    (short)+GainFixedTable(0.003162277660168379),  // -50 dB(short)+
    (short)+GainFixedTable(0.003981071705534973),  // -48 dB(short)+
    (short)+GainFixedTable(0.005011872336272725),  // -46 dB(short)+
    (short)+GainFixedTable(0.006309573444801930),  // -44 dB(short)+
    (short)+GainFixedTable(0.007943282347242814),  // -42 dB(short)+
    (short)+GainFixedTable(0.010000000000000000),  // -40 dB(short)+
    (short)+GainFixedTable(0.012589254117941675),  // -38 dB(short)+
    (short)+GainFixedTable(0.015848931924611134),  // -36 dB(short)+
    (short)+GainFixedTable(0.019952623149688799),  // -34 dB(short)+
    (short)+GainFixedTable(0.025118864315095794),  // -32 dB(short)+
    (short)+GainFixedTable(0.031622776601683791),  // -30 dB(short)+
    (short)+GainFixedTable(0.039810717055349734),  // -28 dB(short)+
    (short)+GainFixedTable(0.050118723362727220),  // -26 dB(short)+
    (short)+GainFixedTable(0.063095734448019331),  // -24 dB(short)+
    (short)+GainFixedTable(0.079432823472428138),  // -22 dB(short)+
    (short)+GainFixedTable(0.100000000000000006),  // -20 dB(short)+
    (short)+GainFixedTable(0.125892541179416728),  // -18 dB(short)+
    (short)+GainFixedTable(0.158489319246111343),  // -16 dB(short)+
    (short)+GainFixedTable(0.199526231496887974),  // -14 dB(short)+
    (short)+GainFixedTable(0.251188643150958013),  // -12 dB(short)+
    (short)+GainFixedTable(0.316227766016837941),  // -10 dB(short)+
    (short)+GainFixedTable(0.398107170553497203),  // - 8 dB(short)+
    (short)+GainFixedTable(0.501187233627272244),  // - 6 dB(short)+
    (short)+GainFixedTable(0.630957344480193250),  // - 4 dB(short)+
    (short)+GainFixedTable(0.794328234724281490),  // - 2 dB(short)+
    (short)+GainFixedTable(1.000000000000000000)   // - 0 dB+
});
    static readonly ReadOnlyCollection<double> YM7128B_GainFloat_Table = Array.AsReadOnly(new double[]
{
    // Negative gains
    -0.000000000000000000,  // -oo dB-
    -0.001000000000000000,  // -60 dB-
    -0.001258925411794167,  // -58 dB-
    -0.001584893192461114,  // -56 dB-
    -0.001995262314968879,  // -54 dB-
    -0.002511886431509579,  // -52 dB-
    -0.003162277660168379,  // -50 dB-
    -0.003981071705534973,  // -48 dB-
    -0.005011872336272725,  // -46 dB-
    -0.006309573444801930,  // -44 dB-
    -0.007943282347242814,  // -42 dB-
    -0.010000000000000000,  // -40 dB-
    -0.012589254117941675,  // -38 dB-
    -0.015848931924611134,  // -36 dB-
    -0.019952623149688799,  // -34 dB-
    -0.025118864315095794,  // -32 dB-
    -0.031622776601683791,  // -30 dB-
    -0.039810717055349734,  // -28 dB-
    -0.050118723362727220,  // -26 dB-
    -0.063095734448019331,  // -24 dB-
    -0.079432823472428138,  // -22 dB-
    -0.100000000000000006,  // -20 dB-
    -0.125892541179416728,  // -18 dB-
    -0.158489319246111343,  // -16 dB-
    -0.199526231496887974,  // -14 dB-
    -0.251188643150958013,  // -12 dB-
    -0.316227766016837941,  // -10 dB-
    -0.398107170553497203,  // - 8 dB-
    -0.501187233627272244,  // - 6 dB-
    -0.630957344480193250,  // - 4 dB-
    -0.794328234724281490,  // - 2 dB-
    -1.000000000000000000,  // - 0 dB-

    // Positive gains
    +0.000000000000000000,  // -oo dB+
    +0.001000000000000000,  // -60 dB+
    +0.001258925411794167,  // -58 dB+
    +0.001584893192461114,  // -56 dB+
    +0.001995262314968879,  // -54 dB+
    +0.002511886431509579,  // -52 dB+
    +0.003162277660168379,  // -50 dB+
    +0.003981071705534973,  // -48 dB+
    +0.005011872336272725,  // -46 dB+
    +0.006309573444801930,  // -44 dB+
    +0.007943282347242814,  // -42 dB+
    +0.010000000000000000,  // -40 dB+
    +0.012589254117941675,  // -38 dB+
    +0.015848931924611134,  // -36 dB+
    +0.019952623149688799,  // -34 dB+
    +0.025118864315095794,  // -32 dB+
    +0.031622776601683791,  // -30 dB+
    +0.039810717055349734,  // -28 dB+
    +0.050118723362727220,  // -26 dB+
    +0.063095734448019331,  // -24 dB+
    +0.079432823472428138,  // -22 dB+
    +0.100000000000000006,  // -20 dB+
    +0.125892541179416728,  // -18 dB+
    +0.158489319246111343,  // -16 dB+
    +0.199526231496887974,  // -14 dB+
    +0.251188643150958013,  // -12 dB+
    +0.316227766016837941,  // -10 dB+
    +0.398107170553497203,  // - 8 dB+
    +0.501187233627272244,  // - 6 dB+
    +0.630957344480193250,  // - 4 dB+
    +0.794328234724281490,  // - 2 dB+
    +1.000000000000000000   // - 0 dB+
});

    private static short Tap(int index) => (short)(index * ((int)YM7128B_DatasheetSpecs.YM7128B_Buffer_Length - 1) / ((int)YM7128B_DatasheetSpecs.YM7128B_Tap_Value_Count - 1));

    public static string GetVersion() => Version;
}