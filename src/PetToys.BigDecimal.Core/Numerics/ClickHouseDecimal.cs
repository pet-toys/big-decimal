using System;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The ClickHouse <c>Decimal32</c>, <c>Decimal64</c>, <c>Decimal128</c> and <c>Decimal256</c> wire
/// format: a two's complement little-endian integer of 4, 8, 16 or 32 bytes, whose scale belongs to
/// the column type rather than to the payload.
/// </summary>
/// <remarks>
/// The two directions are deliberately asymmetric. Reading is total by construction: 256 bits of
/// two's complement is at most 2^255 and a column scale is at most 76, so it never overflows and
/// never rounds. Writing can do both, and refuses a non-finite value by name because no width has
/// a counterpart for one. The width comes from the span's length rather than a separate argument
/// that could disagree with the bytes; the column's precision is the caller's to enforce, being a
/// property of the declared type rather than of the payload.
/// </remarks>
internal static class ClickHouseDecimal
{
    /// <summary>The payload width of a <c>Decimal32</c> column.</summary>
    internal const int Decimal32Size = 4;

    /// <summary>The payload width of a <c>Decimal64</c> column.</summary>
    internal const int Decimal64Size = 8;

    /// <summary>The payload width of a <c>Decimal128</c> column.</summary>
    internal const int Decimal128Size = 16;

    /// <summary>The payload width of a <c>Decimal256</c> column.</summary>
    internal const int Decimal256Size = 32;

    /// <summary>The widest payload, which is also the width every read is sign-extended into.</summary>
    private const int MaxSize = Decimal256Size;

    // Sized to hold the check rather than the product: a magnitude that has outgrown five words
    // has outgrown Decimal256 as well, and scaling stops there.
    private const int WorkWords = BigDecimal.WordCount + 2;

    /// <summary>Reads a ClickHouse decimal payload.</summary>
    /// <param name="source">The payload, 4, 8, 16 or 32 bytes, two's complement little-endian.</param>
    /// <param name="scale">The scale of the column the payload came from.</param>
    /// <returns>The value the payload and the scale describe, exactly.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="source"/> is not one of the four widths, or <paramref name="scale"/> is
    /// outside 0 to <see cref="BigDecimal.MaxScale"/>.
    /// </exception>
    internal static BigDecimal Read(ReadOnlySpan<byte> source, int scale)
    {
        ThrowIfNotAWidth(source.Length, nameof(source));
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, BigDecimal.MaxScale);

        var isNegative = (source[^1] & 0x80) != 0;

        // Sign-extended into the widest width first, so that one reader serves all four and the
        // narrow ones do not each need a negation of their own. Only the bytes above the payload
        // are filled: the rest are about to be overwritten, and at Decimal256 there are none.
        Span<byte> extended = stackalloc byte[MaxSize];
        source.CopyTo(extended);
        extended[source.Length..].Fill(isNegative ? (byte)0xFF : (byte)0x00);

        Span<ulong> magnitude = stackalloc ulong[BigDecimal.WordCount];
        for (var i = 0; i < BigDecimal.WordCount; i++)
        {
            magnitude[i] = BinaryPrimitives.ReadUInt64LittleEndian(extended[(i * sizeof(ulong))..]);
        }

        if (isNegative)
        {
            Negate(magnitude);
        }

        return BigDecimal.FromWords(magnitude, isNegative, scale);
    }

    /// <summary>Writes a value as a ClickHouse decimal payload.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="scale">The scale of the column being written to.</param>
    /// <param name="destination">
    /// Receives the payload. Its length selects the width and must be 4, 8, 16 or 32 bytes.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="destination"/> is not one of the four widths, or <paramref name="scale"/> is
    /// outside 0 to <see cref="BigDecimal.MaxScale"/>.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="value"/> is NaN or an infinity. No ClickHouse decimal width has a
    /// counterpart for any of the three.
    /// </exception>
    /// <exception cref="OverflowException">
    /// The value at the column's scale is outside the two's complement range of the width.
    /// </exception>
    internal static void Write(BigDecimal value, int scale, Span<byte> destination)
    {
        ThrowIfNotAWidth(destination.Length, nameof(destination));
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, BigDecimal.MaxScale);

        // Answered here rather than left to the magnitude accessors, which refuse a non-finite
        // value with a message about magnitudes. The caller is writing to a column, and the message
        // that helps names the value and the column.
        if (value.IsNonFinite)
        {
            ThrowNonFiniteHasNoColumn(value, destination.Length);
        }

        Span<ulong> magnitude = stackalloc ulong[WorkWords];
        var length = value.CopyMagnitude(magnitude);
        var isNegative = value.IsNegative;

        // The rescale comes before the range check, because the range is a property of the value at
        // the column's scale and not of the value as it arrived.
        if (scale > value.Scale)
        {
            length = ScaleUpUntilItCannotFit(magnitude, length, scale - value.Scale);
        }
        else if (scale < value.Scale)
        {
            length = Words.DivPow10Round(
                magnitude, length, value.Scale - scale, isNegative, MidpointRounding.ToEven);
        }

        if (!FitsTheWidth(magnitude, length, isNegative, destination.Length))
        {
            BigDecimal.ThrowMantissaOverflow();
        }

        Span<ulong> payload = stackalloc ulong[BigDecimal.WordCount];
        magnitude[..BigDecimal.WordCount].CopyTo(payload);
        if (isNegative)
        {
            Negate(payload);
        }

        Span<byte> full = stackalloc byte[MaxSize];
        for (var i = 0; i < BigDecimal.WordCount; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(full[(i * sizeof(ulong))..], payload[i]);
        }

        full[..destination.Length].CopyTo(destination);
    }

    /// <summary>
    /// Multiplies by a power of ten, stopping as soon as the result has outgrown every width.
    /// </summary>
    /// <remarks>
    /// Past five words the value is already past <c>Decimal256</c> and no further digit changes
    /// that, so the loop stops and reports a length the range check reads as too wide.
    /// </remarks>
    private static int ScaleUpUntilItCannotFit(Span<ulong> magnitude, int length, int power)
    {
        while (power > 0 && length <= BigDecimal.WordCount)
        {
            var chunk = Math.Min(power, Words.MaxZerosPerPass);
            length = Words.MulAddSmall(magnitude, length, Words.Pow10[chunk], 0);
            power -= chunk;
        }

        return length;
    }

    /// <summary>
    /// Replaces a four-word value by its two's complement, in place. Its own inverse, which is why
    /// one method serves the reader and the writer alike.
    /// </summary>
    private static void Negate(Span<ulong> words)
    {
        unchecked
        {
            ulong carry = 1;
            for (var i = 0; i < BigDecimal.WordCount; i++)
            {
                var word = ~words[i] + carry;
                carry = carry != 0 && word == 0 ? 1UL : 0UL;
                words[i] = word;
            }
        }
    }

    /// <summary>Answers whether a magnitude fits the two's complement range of a width.</summary>
    /// <remarks>
    /// The range is asymmetric by one: <c>b</c> bits hold every magnitude below 2^(b-1), and
    /// 2^(b-1) itself only when negative. Written over the sign bit's word rather than once per
    /// width, so the four cannot come to disagree.
    /// </remarks>
    private static bool FitsTheWidth(ReadOnlySpan<ulong> magnitude, int length, bool isNegative, int size)
    {
        if (length > BigDecimal.WordCount)
        {
            return false;
        }

        var signBit = (size * 8) - 1;
        var signWord = signBit / 64;
        var threshold = 1UL << (signBit % 64);

        for (var i = length - 1; i > signWord; i--)
        {
            if (magnitude[i] != 0)
            {
                return false;
            }
        }

        var top = signWord < length ? magnitude[signWord] : 0UL;
        if (top < threshold)
        {
            return true;
        }

        if (top != threshold || !isNegative)
        {
            return false;
        }

        // Exactly the sign bit and nothing under it is the one value a width holds in negative and
        // not in positive: -2^(b-1).
        for (var i = 0; i < signWord && i < length; i++)
        {
            if (magnitude[i] != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void ThrowIfNotAWidth(int size, string parameterName)
    {
        if (size is not (Decimal32Size or Decimal64Size or Decimal128Size or Decimal256Size))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                size,
                "A ClickHouse decimal payload is 4, 8, 16 or 32 bytes.");
        }
    }

    [DoesNotReturn]
    private static void ThrowNonFiniteHasNoColumn(BigDecimal value, int size)
    {
        var name = BigDecimal.IsNaN(value)
            ? "NaN"
            : BigDecimal.IsPositiveInfinity(value) ? "Infinity" : "-Infinity";

        var column = size switch
        {
            Decimal32Size => "Decimal32",
            Decimal64Size => "Decimal64",
            Decimal128Size => "Decimal128",
            _ => "Decimal256",
        };

        throw new NotSupportedException(
            name + " cannot be written to a ClickHouse " + column + " column: no ClickHouse decimal type represents it.");
    }
}
