#define YM7128B_USE_MINPHASE //!< Enables minimum-phase oversampler kernel
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
    private static ushort Tap(int index) => (ushort)(index * ((int)YM7128B_DatasheetSpecs.YM7128B_Buffer_Length - 1) / ((int)YM7128B_DatasheetSpecs.YM7128B_Tap_Value_Count - 1));

    static readonly ReadOnlyCollection<ushort> YM7128B_Tap_Table = Array.AsReadOnly(new ushort[]
{
    Tap( 0),  //   0.0 ms
    Tap( 1),  //   3.2 ms
    Tap( 2),  //   6.5 ms
    Tap( 3),  //   9.7 ms
    Tap( 4),  //  12.9 ms
    Tap( 5),  //  16.1 ms
    Tap( 6),  //  19.3 ms
    Tap( 7),  //  22.6 ms
    Tap( 8),  //  25.8 ms
    Tap( 9),  //  29.0 ms
    Tap(10),  //  32.3 ms
    Tap(11),  //  35.5 ms
    Tap(12),  //  38.7 ms
    Tap(13),  //  41.9 ms
    Tap(14),  //  45.2 ms
    Tap(15),  //  48.4 ms
    Tap(16),  //  51.6 ms
    Tap(17),  //  54.9 ms
    Tap(18),  //  58.1 ms
    Tap(19),  //  61.3 ms
    Tap(20),  //  64.5 ms
    Tap(21),  //  67.8 ms
    Tap(22),  //  71.0 ms
    Tap(23),  //  74.2 ms
    Tap(24),  //  77.4 ms
    Tap(25),  //  80.7 ms
    Tap(26),  //  83.9 ms
    Tap(27),  //  87.1 ms
    Tap(28),  //  90.4 ms
    Tap(29),  //  93.6 ms
    Tap(30),  //  96.8 ms
    Tap(31)   // 100.0 ms
});

    private static short Kernel(double real) {
        unchecked {
            return ((short)(((short)real) * ((short)YM7128B_ImplementationSpecs.YM7128B_Fixed_Max) & ((short)YM7128B_ImplementationSpecs.YM7128B_Coeff_Mask)));
        }
    }

    static readonly ReadOnlyCollection<short> YM7128B_OversamplerFixed_Kernel = Array.AsReadOnly(new short[]
{
#if YM7128B_USE_MINPHASE
    // minimum phase
    Kernel(+0.073585247514714749),
    Kernel(+0.269340051166713890),
    Kernel(+0.442535202999738531),
    Kernel(+0.350129745841520346),
    Kernel(+0.026195691646307945),
    Kernel(-0.178423532471468610),
    Kernel(-0.081176763571493171),
    Kernel(+0.083194010466739091),
    Kernel(+0.067960765530891545),
    Kernel(-0.035840063980478287),
    Kernel(-0.044393769145659796),
    Kernel(+0.013156688603347873),
    Kernel(+0.023451305043275420),
    Kernel(-0.004374029821991059),
    Kernel(-0.009480786001493536),
    Kernel(+0.002700502551912207),
    Kernel(+0.003347671274177581),
    Kernel(-0.002391896275498628),
    Kernel(+0.000483958628744376)
#else
    // linear phase
    Kernel(+0.005969087803865891),
    Kernel(-0.003826518613910499),
    Kernel(-0.016623943725986926),
    Kernel(+0.007053928712894589),
    Kernel(+0.038895802111020034),
    Kernel(-0.010501507751597486),
    Kernel(-0.089238395139830201),
    Kernel(+0.013171814880420758),
    Kernel(+0.312314472963171053),
    Kernel(+0.485820312497107776),
    Kernel(+0.312314472963171053),
    Kernel(+0.013171814880420758),
    Kernel(-0.089238395139830201),
    Kernel(-0.010501507751597486),
    Kernel(+0.038895802111020034),
    Kernel(+0.007053928712894589),
    Kernel(-0.016623943725986926),
    Kernel(-0.003826518613910499),
    Kernel(+0.005969087803865891)
#endif
});

    public static short YM7128B_OversamplerFixed_Process(
    ref YM7128B_OversamplerFixed self,
    short input) {
        int accum = 0;
        for (byte i = 0; i < (byte)YM7128B_OversamplerSpecs.YM7128B_Oversampler_Length; ++i) {
            short sample = self.Buffer[i];
            self.Buffer[i] = input;
            input = sample;
            short kernel = YM7128B_OversamplerFixed_Kernel[i];
            short oversampled = YM7128B_MulFixed(sample, kernel);
            accum += oversampled;
        }
        short clamped = YM7128B_ClampFixed(accum);
        unchecked {
            short output = (short)(clamped & (short)YM7128B_ImplementationSpecs.YM7128B_Signal_Mask);
            return output;
        }
    }

    private static double KernelDouble(double real) => real;

    static readonly ReadOnlyCollection<double> YM7128B_OversamplerFloat_Kernel = Array.AsReadOnly(new double[]
{
#if YM7128B_USE_MINPHASE
    // minimum phase
    KernelDouble(+0.073585247514714749),
    KernelDouble(+0.269340051166713890),
    KernelDouble(+0.442535202999738531),
    KernelDouble(+0.350129745841520346),
    KernelDouble(+0.026195691646307945),
    KernelDouble(-0.178423532471468610),
    KernelDouble(-0.081176763571493171),
    KernelDouble(+0.083194010466739091),
    KernelDouble(+0.067960765530891545),
    KernelDouble(-0.035840063980478287),
    KernelDouble(-0.044393769145659796),
    KernelDouble(+0.013156688603347873),
    KernelDouble(+0.023451305043275420),
    KernelDouble(-0.004374029821991059),
    KernelDouble(-0.009480786001493536),
    KernelDouble(+0.002700502551912207),
    KernelDouble(+0.003347671274177581),
    KernelDouble(-0.002391896275498628),
    KernelDouble(+0.000483958628744376)
#else
    // linear phase
    KernelDouble(+0.005969087803865891),
    KernelDouble(-0.003826518613910499),
    KernelDouble(-0.016623943725986926),
    KernelDouble(+0.007053928712894589),
    KernelDouble(+0.038895802111020034),
    KernelDouble(-0.010501507751597486),
    KernelDouble(-0.089238395139830201),
    KernelDouble(+0.013171814880420758),
    KernelDouble(+0.312314472963171053),
    KernelDouble(+0.485820312497107776),
    KernelDouble(+0.312314472963171053),
    KernelDouble(+0.013171814880420758),
    KernelDouble(-0.089238395139830201),
    KernelDouble(-0.010501507751597486),
    KernelDouble(+0.038895802111020034),
    KernelDouble(+0.007053928712894589),
    KernelDouble(-0.016623943725986926),
    KernelDouble(-0.003826518613910499),
    KernelDouble(+0.005969087803865891)
#endif
});

    private static double YM7128B_OversamplerFloat_Process(
    ref YM7128B_OversamplerFloat self,
    double input
    )
    {
        double accum = 0;

        for (byte i = 0; i < (byte)YM7128B_OversamplerSpecs.YM7128B_Oversampler_Length; ++i) {
            double sample = self.Buffer[i];
            self.Buffer[i] = input;
            input = sample;
            double kernel = YM7128B_OversamplerFloat_Kernel[i];
            double oversampled = YM7128B_MulFloat(sample, kernel);
            accum += oversampled;
        }

        double output = YM7128B_ClampFloat(accum);
        return output;
    }

    private static double YM7128B_MulFloat(double a, double b) => a * b;

    private static double YM7128B_ClampFloat(double signal)
    {
        if (signal < YM7128B_Float_Min) {
            return YM7128B_Float_Min;
        }
        if (signal > YM7128B_Float_Max) {
            return YM7128B_Float_Max;
        }
        return signal;
    }

    private static short YM7128B_MulFixed(short a, short b) {
        unchecked {
            int aa = a & (short)YM7128B_ImplementationSpecs.YM7128B_Operand_Mask;
            int bb = b & (short)YM7128B_ImplementationSpecs.YM7128B_Operand_Mask;
            int mm = aa * bb;
            short x = (short)(mm >> (short)YM7128B_ImplementationSpecs.YM7128B_Fixed_Decimals);
            short y = (short)(x & (short)YM7128B_ImplementationSpecs.YM7128B_Operand_Mask);
            return y;
        }
    }

    private static short YM7128B_ClampFixed(int signal) {
        if (signal < (int)YM7128B_ImplementationSpecs.YM7128B_Fixed_Min) {
            signal = (int)YM7128B_ImplementationSpecs.YM7128B_Fixed_Min;
        }
        if (signal > (int)YM7128B_ImplementationSpecs.YM7128B_Fixed_Max) {
            signal = (int)YM7128B_ImplementationSpecs.YM7128B_Fixed_Max;
        }
        unchecked {
            return (short)((short)signal & (short)YM7128B_ImplementationSpecs.YM7128B_Operand_Mask);
        }
    }



    public static string GetVersion() => Version;
}