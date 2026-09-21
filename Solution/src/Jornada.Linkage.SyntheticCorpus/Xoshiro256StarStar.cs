using System.Globalization;
using System.Numerics;

namespace Jornada.Linkage.SyntheticCorpus;

/// <summary>
/// PRNG estável do benchmark sintético. A versão faz parte do manifesto do corpus.
/// </summary>
public sealed class Xoshiro256StarStar
{
    public const string AlgorithmVersion = "XOSHIRO256SS_SPLITMIX64_V1";

    private ulong s0;
    private ulong s1;
    private ulong s2;
    private ulong s3;

    public Xoshiro256StarStar(ulong seed)
    {
        var state = seed;
        s0 = SplitMix64(ref state);
        s1 = SplitMix64(ref state);
        s2 = SplitMix64(ref state);
        s3 = SplitMix64(ref state);
        InitialStateHex = string.Join(
            ":",
            s0.ToString("X16", CultureInfo.InvariantCulture),
            s1.ToString("X16", CultureInfo.InvariantCulture),
            s2.ToString("X16", CultureInfo.InvariantCulture),
            s3.ToString("X16", CultureInfo.InvariantCulture));
    }

    public string InitialStateHex { get; }

    public ulong NextUInt64()
    {
        var result = BitOperations.RotateLeft(unchecked(s1 * 5UL), 7);
        result = unchecked(result * 9UL);

        var t = unchecked(s1 << 17);

        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;

        s2 ^= t;
        s3 = BitOperations.RotateLeft(s3, 45);

        return result;
    }

    public ulong NextUInt64(ulong exclusiveUpperBound)
    {
        if (exclusiveUpperBound == 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound), "Limite deve ser positivo.");

        var threshold = unchecked((0UL - exclusiveUpperBound) % exclusiveUpperBound);
        while (true)
        {
            var candidate = NextUInt64();
            if (candidate >= threshold)
                return candidate % exclusiveUpperBound;
        }
    }

    public int NextInt32(int exclusiveUpperBound)
    {
        if (exclusiveUpperBound <= 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound), "Limite deve ser positivo.");

        return checked((int)NextUInt64((ulong)exclusiveUpperBound));
    }

    public double NextUnitInterval()
        => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    private static ulong SplitMix64(ref ulong state)
    {
        state = unchecked(state + 0x9E3779B97F4A7C15UL);
        var z = state;
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }
}
