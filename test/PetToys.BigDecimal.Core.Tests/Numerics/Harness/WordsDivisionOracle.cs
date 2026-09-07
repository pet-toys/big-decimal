using System;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Division of a magnitude by a single 64-bit word, written the way the package wrote it before
/// the division primitive was replaced: a <see cref="UInt128"/> built from the running remainder
/// and the current word, divided and taken modulo by the runtime.
/// </summary>
/// <remarks>
/// <para>
/// This is the reference the replacement is measured for correctness against, so it is kept word
/// for word as it stood and is not tidied. It is deliberately the slow form: the runtime does not
/// turn this shape into a hardware divide, which is the reason the package stopped using it, and
/// the reason it is a sound oracle - nothing about the new implementation can be right by
/// construction here.
/// </para>
/// <para>
/// It calls nothing in the package under test, not even to normalise a length. The harness rule is
/// that an oracle written by reading the code under test agrees with it including where it is
/// wrong; a helper borrowed from the implementation is the same mistake in a smaller place.
/// </para>
/// </remarks>
public static class WordsDivisionOracle
{
    /// <summary>Divides a 128-bit value by a single word.</summary>
    /// <param name="high">The high half of the dividend, which must be below <paramref name="divisor"/>.</param>
    /// <param name="low">The low half of the dividend.</param>
    /// <param name="divisor">The divisor, which must not be zero.</param>
    /// <param name="remainder">Receives the remainder.</param>
    /// <returns>The quotient.</returns>
    public static ulong DivRem2By1(ulong high, ulong low, ulong divisor, out ulong remainder)
    {
        var current = new UInt128(high, low);
        remainder = (ulong)(current % divisor);

        return (ulong)(current / divisor);
    }

    /// <summary>Divides a magnitude by a single word in place.</summary>
    /// <param name="accumulator">The magnitude, replaced by the quotient.</param>
    /// <param name="length">The number of significant words in <paramref name="accumulator"/>.</param>
    /// <param name="divisor">The divisor, which must not be zero.</param>
    /// <param name="remainder">Receives the remainder.</param>
    /// <returns>The number of significant words in the quotient.</returns>
    public static int DivRemSmall(Span<ulong> accumulator, int length, ulong divisor, out ulong remainder)
    {
        ulong rem = 0;
        for (var i = length - 1; i >= 0; i--)
        {
            var current = new UInt128(rem, accumulator[i]);
            accumulator[i] = (ulong)(current / divisor);
            rem = (ulong)(current % divisor);
        }

        remainder = rem;

        return Normalize(accumulator[..length]);
    }

    /// <summary>Returns the remainder of a magnitude divided by a single word, leaving it unchanged.</summary>
    /// <param name="value">The magnitude.</param>
    /// <param name="length">The number of significant words in <paramref name="value"/>.</param>
    /// <param name="divisor">The divisor, which must not be zero.</param>
    /// <returns>The remainder.</returns>
    public static ulong RemSmall(ReadOnlySpan<ulong> value, int length, ulong divisor)
    {
        ulong rem = 0;
        for (var i = length - 1; i >= 0; i--)
        {
            rem = (ulong)(new UInt128(rem, value[i]) % divisor);
        }

        return rem;
    }

    /// <summary>Divides a magnitude by a single word into a separate quotient.</summary>
    /// <param name="numerator">The magnitude, which is left unchanged.</param>
    /// <param name="length">The number of significant words in <paramref name="numerator"/>.</param>
    /// <param name="divisor">The divisor, which must not be zero.</param>
    /// <param name="quotient">Receives the quotient. It must hold <paramref name="length"/> words.</param>
    /// <param name="remainder">Receives the remainder.</param>
    /// <returns>The number of significant words in the quotient.</returns>
    public static int DivRemIntoQuotient(
        ReadOnlySpan<ulong> numerator,
        int length,
        ulong divisor,
        Span<ulong> quotient,
        out ulong remainder)
    {
        ulong rem = 0;
        for (var i = length - 1; i >= 0; i--)
        {
            var current = new UInt128(rem, numerator[i]);
            quotient[i] = (ulong)(current / divisor);
            rem = (ulong)(current % divisor);
        }

        remainder = rem;

        return Normalize(quotient[..length]);
    }

    /// <summary>Counts the significant words of a magnitude, its own copy of a helper the package also has.</summary>
    /// <param name="value">The magnitude.</param>
    /// <returns>The index one past the highest non-zero word.</returns>
    private static int Normalize(ReadOnlySpan<ulong> value)
    {
        var length = value.Length;
        while (length > 0 && value[length - 1] == 0)
        {
            length--;
        }

        return length;
    }
}
