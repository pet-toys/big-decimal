using System;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Turns a test's identity and a base constant into the seeds its cases are drawn from.
/// </summary>
/// <remarks>
/// The mix is FNV-1a over the identity, the row index and the base seed. It has to be written out
/// rather than delegated to <see cref="string.GetHashCode()"/>, whose value is randomised per
/// process — the one thing a reproducible seed cannot tolerate. The result is masked to a
/// non-negative value so that the seed a failure reports is the seed a reader types back in.
/// </remarks>
public static class FuzzSeeds
{
    private const uint FnvOffsetBasis = 2_166_136_261u;
    private const uint FnvPrime = 16_777_619u;

    /// <summary>Derives the seed for one row of one test.</summary>
    /// <param name="baseSeed">The run's base constant, from <see cref="FuzzSettings.BaseSeed"/>.</param>
    /// <param name="identity">The test's stable identity, normally its full method name.</param>
    /// <param name="index">The row's position, from zero.</param>
    /// <returns>A non-negative seed, stable across processes, machines and target frameworks.</returns>
    public static int Derive(int baseSeed, string identity, int index)
    {
        ArgumentNullException.ThrowIfNull(identity);

        unchecked
        {
            var hash = FnvOffsetBasis;

            foreach (var character in identity)
            {
                hash = Mix(hash, (byte)character);
                hash = Mix(hash, (byte)(character >> 8));
            }

            hash = MixInt32(hash, index);
            hash = MixInt32(hash, baseSeed);

            return (int)(hash & int.MaxValue);
        }
    }

    private static uint MixInt32(uint hash, int value)
    {
        unchecked
        {
            var bits = (uint)value;

            for (var shift = 0; shift < 32; shift += 8)
            {
                hash = Mix(hash, (byte)(bits >> shift));
            }

            return hash;
        }
    }

    private static uint Mix(uint hash, byte value) => unchecked((hash ^ value) * FnvPrime);
}
