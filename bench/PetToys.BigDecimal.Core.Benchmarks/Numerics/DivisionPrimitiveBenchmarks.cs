using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Dividing a magnitude by a single 64-bit word, the way the package did it before the division
/// primitive was replaced and the way it does it now, measured against each other in one run.
/// </summary>
/// <remarks>
/// <para>
/// This is the group the claim rests on. A run taken after a change and compared against a report
/// recorded before it measures the machine as much as the code: between two runs on this machine a
/// week apart, <see cref="decimal"/> itself moved by 1.5x to 2.3x. Two arms of one group, executed
/// by the same runner minutes apart on the same operands, do not have that problem, and the ratio
/// column is then the answer rather than an invitation to subtract two reports.
/// </para>
/// <para>
/// Both arms are copies of the loops rather than calls into the package: the benchmark project is
/// not on the package's <c>InternalsVisibleTo</c> list, and widening that for a measurement would
/// be the wrong trade. So this group answers "is the algorithm cheaper", at the widths the mantissa
/// can take. Whether the operations built on it became cheaper is a different question, answered by
/// the public rows and by the relationships between them - an exact division against an inexact
/// one, a hash of a value carrying zeros against one that carries none.
/// </para>
/// <para>
/// The operands are literals rather than a seeded draw, so that the two arms and two runs see the
/// same words. The divisors are the two the package actually divides by on these paths: ten, which
/// rounding ends on, and the largest power of ten a word holds, which formatting peels with.
/// </para>
/// </remarks>
public class DivisionPrimitiveBenchmarks
{
    // Four fixed words, high entropy and none of them special. Held as literals because a
    // measurement whose operands come from a seed is reproducible only for as long as nobody
    // touches the seed.
    private static readonly ulong[] Source =
    [
        0x9E37_79B9_7F4A_7C15UL,
        0xBF58_476D_1CE4_E5B9UL,
        0x94D0_49BB_1331_11EBUL,
        0x2545_F491_4F6C_DD1DUL,
    ];

    private readonly ulong[] quotient = new ulong[4];

    private int shift;
    private ulong normalized;
    private ulong reciprocal;

    /// <summary>The number of words in the magnitude being divided.</summary>
    [Params(1, 2, 4)]
    public int Words { get; set; }

    /// <summary>The divisor, one of the two the package divides by on these paths.</summary>
    [Params(10UL, 10_000_000_000_000_000_000UL)]
    public ulong Divisor { get; set; }

    /// <summary>Prepares the divisor, which the package prepares once at type initialisation.</summary>
    [GlobalSetup]
    public void Setup()
    {
        shift = BitOperations.LeadingZeroCount(Divisor);
        normalized = Divisor << shift;
        reciprocal = unchecked((ulong)(UInt128.MaxValue / normalized));
    }

    /// <summary>The replaced form: a <see cref="UInt128"/> divided by a <see cref="ulong"/> per word.</summary>
    /// <returns>The remainder and a quotient word, so that neither the loop nor its stores are elided.</returns>
    [Benchmark(Baseline = true)]
    public ulong Replaced()
    {
        var source = Source;
        var destination = quotient;
        ulong rem = 0;
        for (var i = Words - 1; i >= 0; i--)
        {
            var current = new UInt128(rem, source[i]);
            destination[i] = (ulong)(current / Divisor);
            rem = (ulong)(current % Divisor);
        }

        return rem ^ destination[0];
    }

    /// <summary>The primitive: one widening multiplication and at most two corrections per word.</summary>
    /// <returns>The remainder and a quotient word, so that neither the loop nor its stores are elided.</returns>
    [Benchmark]
    public ulong Primitive()
    {
        var source = Source;
        var destination = quotient;
        var places = shift;
        var divisor = normalized;
        var inverse = reciprocal;

        ulong rem = 0;
        if (places != 0)
        {
            rem = source[Words - 1] >> (64 - places);
        }

        for (var i = Words - 1; i >= 0; i--)
        {
            var below = i > 0 ? source[i - 1] : 0UL;
            var low = places == 0 ? source[i] : (source[i] << places) | (below >> (64 - places));
            destination[i] = DivRem2By1(rem, low, divisor, inverse, out rem);
        }

        return (rem >> places) ^ destination[0];
    }

    /// <summary>The primitive where the divisor is the caller's, so its reciprocal is computed per call.</summary>
    /// <remarks>
    /// The shape of the single-word branch of the multi-word divider, which cannot read a prepared
    /// divisor from a table. The reciprocal is one division of the kind the primitive exists to
    /// avoid, so this arm answers whether it pays for itself at each width, and where it does not.
    /// </remarks>
    /// <returns>The remainder and a quotient word, so that neither the loop nor its stores are elided.</returns>
    [Benchmark]
    public ulong PrimitivePerCallDivisor()
    {
        var source = Source;
        var destination = quotient;
        var places = BitOperations.LeadingZeroCount(Divisor);
        var divisor = Divisor << places;
        var inverse = unchecked((ulong)(UInt128.MaxValue / divisor));

        ulong rem = 0;
        if (places != 0)
        {
            rem = source[Words - 1] >> (64 - places);
        }

        for (var i = Words - 1; i >= 0; i--)
        {
            var below = i > 0 ? source[i - 1] : 0UL;
            var low = places == 0 ? source[i] : (source[i] << places) | (below >> (64 - places));
            destination[i] = DivRem2By1(rem, low, divisor, inverse, out rem);
        }

        return (rem >> places) ^ destination[0];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong DivRem2By1(ulong high, ulong low, ulong divisor, ulong reciprocal, out ulong remainder)
    {
        unchecked
        {
            var estimate = Math.BigMul(reciprocal, high, out var estimateLow);
            estimateLow += low;
            if (estimateLow < low)
            {
                estimate++;
            }

            estimate += high;
            estimate++;

            var rest = low - (estimate * divisor);
            if (rest > estimateLow)
            {
                estimate--;
                rest += divisor;
            }

            if (rest >= divisor)
            {
                estimate++;
                rest -= divisor;
            }

            remainder = rest;

            return estimate;
        }
    }
}
