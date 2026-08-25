using System;
using System.Buffers.Binary;
using System.Numerics;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Draws <see cref="BigDecimal"/> values from the classes in <see cref="ValueClass"/>, together
/// with the exact mantissa and scale they were built from.
/// </summary>
/// <remarks>
/// Deliberately not a uniform sampler. Every draw picks a class first and a value second, so that
/// one-word magnitudes, word boundaries, exact powers of ten and the extremes occur as often as
/// four-word noise does. The scale is drawn with its own weighting: mostly inside the range
/// database columns actually declare, sometimes anywhere in the domain, and sometimes at the very
/// edge of it.
/// </remarks>
/// <param name="random">The seeded source of randomness. One per case, never shared.</param>
public sealed class ValueGenerator(Random random)
{
    private static readonly ValueClass[] AllClasses = Enum.GetValues<ValueClass>();
    private static readonly ScaleRelationship[] AllRelationships = Enum.GetValues<ScaleRelationship>();

    /// <summary>The largest magnitude the type can hold, 2^256 - 1.</summary>
    public static BigInteger MaxMagnitude { get; } = (BigInteger.One << 256) - BigInteger.One;

    /// <summary>Draws a value from a class chosen at random.</summary>
    /// <returns>The drawn value.</returns>
    public FuzzValue Next() => Next(AllClasses[random.Next(AllClasses.Length)]);

    /// <summary>Draws a value that is not zero, for use where zero is undefined.</summary>
    /// <returns>The drawn value.</returns>
    public FuzzValue NextNonZero()
    {
        while (true)
        {
            var value = Next();
            if (!value.Value.IsZero)
            {
                return value;
            }
        }
    }

    /// <summary>Draws a value from a named class.</summary>
    /// <param name="valueClass">The class to draw from.</param>
    /// <returns>The drawn value.</returns>
    public FuzzValue Next(ValueClass valueClass) => Build(valueClass, Magnitude(valueClass), NextScale());

    /// <summary>Draws a value from a named class at a given scale.</summary>
    /// <param name="valueClass">The class to draw from.</param>
    /// <param name="scale">The scale to build it at.</param>
    /// <returns>The drawn value.</returns>
    public FuzzValue Next(ValueClass valueClass, int scale) => Build(valueClass, Magnitude(valueClass), scale);

    /// <summary>Draws a pair whose scales stand in a relationship chosen at random.</summary>
    /// <returns>The drawn pair.</returns>
    public (FuzzValue Left, FuzzValue Right) NextPair() =>
        NextPair(AllRelationships[random.Next(AllRelationships.Length)]);

    /// <summary>Draws a pair whose scales stand in the given relationship.</summary>
    /// <param name="relationship">How the two scales are to relate.</param>
    /// <returns>The drawn pair.</returns>
    public (FuzzValue Left, FuzzValue Right) NextPair(ScaleRelationship relationship)
    {
        var left = Next();
        var rightClass = AllClasses[random.Next(AllClasses.Length)];

        return (left, Build(rightClass, Magnitude(rightClass), RelatedScale(left.Scale, relationship)));
    }

    private static FuzzValue Build(ValueClass valueClass, BigInteger magnitude, int scale) =>
        new(BigDecimal.FromScaled(magnitude, scale), magnitude, scale, valueClass);

    private int RelatedScale(int scale, ScaleRelationship relationship) => relationship switch
    {
        ScaleRelationship.Equal => scale,
        ScaleRelationship.OffByOne => scale == BigDecimal.MaxScale ? scale - 1 : scale + 1,
        _ => scale <= BigDecimal.MaxScale / 2
            ? random.Next(scale + 40, BigDecimal.MaxScale + 1)
            : random.Next(0, scale - 39),
    };

    private BigInteger Magnitude(ValueClass valueClass)
    {
        var magnitude = valueClass switch
        {
            ValueClass.OneWord => RandomWords(1),
            ValueClass.TwoWords => RandomWords(2),
            ValueClass.ThreeWords => RandomWords(3),
            ValueClass.FourWords => RandomWords(4),
            ValueClass.BelowWordBoundary => (BigInteger.One << (64 * random.Next(1, 5))) - BigInteger.One,
            ValueClass.AtWordBoundary => BigInteger.One << (64 * random.Next(1, 4)),
            ValueClass.Zero => BigInteger.Zero,
            ValueClass.PowerOfTen => BigInteger.Pow(10, random.Next(0, 77)),
            ValueClass.TrailingZeros => TrailingZeros(),
            _ => MaxMagnitude,
        };

        return magnitude.IsZero || random.Next(2) == 0 ? magnitude : -magnitude;
    }

    private BigInteger TrailingZeros()
    {
        var head = RandomWords(random.Next(1, 3));
        var zeros = random.Next(1, 21);
        var scaled = head * BigInteger.Pow(10, zeros);

        return scaled > MaxMagnitude ? head : scaled;
    }

    private BigInteger RandomWords(int words)
    {
        Span<byte> bytes = stackalloc byte[32];
        bytes.Clear();
        random.NextBytes(bytes[..(words * 8)]);

        // Keep the value in the class it was asked for: the top word has to be non-zero, or a
        // three-word draw quietly becomes a two-word one.
        var top = bytes.Slice((words - 1) * 8, 8);
        if (BinaryPrimitives.ReadUInt64LittleEndian(top) == 0)
        {
            top[7] = 0x80;
        }

        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private int NextScale() => random.Next(10) switch
    {
        0 => random.Next(2) == 0 ? 0 : BigDecimal.MaxScale,
        1 or 2 => random.Next(0, BigDecimal.MaxScale + 1),
        _ => random.Next(0, 39),
    };
}
