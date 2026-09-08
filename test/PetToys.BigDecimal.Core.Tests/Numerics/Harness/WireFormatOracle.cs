using System;
using System.Collections.Generic;
using System.Numerics;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The two database wire formats, composed and read from <see cref="BigInteger"/> arithmetic and
/// the documented layouts alone.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here consults the codecs, <c>Words</c>, or any member of <see cref="BigDecimal"/>. The
/// ClickHouse side is barely an oracle at all: its format is exactly what
/// <see cref="BigInteger.ToByteArray(bool, bool)"/> produces, so the base class library answers it.
/// The PostgreSQL side decomposes into base-10000 groups and lays out the header itself.
/// </para>
/// <para>
/// The limit is worth stating where it is: this shares the reading of the PostgreSQL layout with
/// the code it checks, so a misreading of the format would be invisible to it. Only a live server
/// disproves that, and that test belongs to the adapters.
/// </para>
/// </remarks>
public static class WireFormatOracle
{
    /// <summary>The four header fields of a PostgreSQL <c>numeric</c>.</summary>
    public const int PostgresHeaderSize = 8;

    /// <summary>Decimal digits per base-10000 group.</summary>
    public const int PostgresDecDigits = 4;

    /// <summary>The sign word of a positive value.</summary>
    public const ushort PostgresPositive = 0x0000;

    /// <summary>The sign word of a negative value.</summary>
    public const ushort PostgresNegative = 0x4000;

    /// <summary>The sign word of NaN.</summary>
    public const ushort PostgresNaN = 0xC000;

    /// <summary>The sign word of positive infinity.</summary>
    public const ushort PostgresPositiveInfinity = 0xD000;

    /// <summary>The sign word of negative infinity.</summary>
    public const ushort PostgresNegativeInfinity = 0xF000;

    private const int NBase = 10_000;

    /// <summary>Composes the PostgreSQL binary payload of a value.</summary>
    /// <param name="value">The mantissa and scale to encode.</param>
    /// <returns>The payload the format calls for.</returns>
    public static byte[] Postgres(OracleValue value)
    {
        var sign = value.Unscaled.Sign < 0 ? PostgresNegative : PostgresPositive;
        var magnitude = BigInteger.Abs(value.Unscaled);

        // The groups only line up with the point when the scale is a multiple of four.
        var pad = (PostgresDecDigits - (value.Scale % PostgresDecDigits)) % PostgresDecDigits;
        var padded = magnitude * BigInteger.Pow(10, pad);
        var pointGroup = (value.Scale + pad) / PostgresDecDigits;

        var groups = new List<ushort>();
        while (!padded.IsZero)
        {
            padded = BigInteger.DivRem(padded, NBase, out var group);
            groups.Add((ushort)group);
        }

        var lowest = 0;
        while (lowest < groups.Count && groups[lowest] == 0)
        {
            lowest++;
        }

        var ndigits = groups.Count - lowest;
        var weight = ndigits == 0 ? 0 : groups.Count - 1 - pointGroup;

        var payload = new byte[PostgresHeaderSize + (ndigits * sizeof(short))];
        WriteBigEndian(payload, 0, ndigits);
        WriteBigEndian(payload, 2, weight);
        WriteBigEndian(payload, 4, sign);
        WriteBigEndian(payload, 6, value.Scale);

        for (var j = 0; j < ndigits; j++)
        {
            WriteBigEndian(payload, PostgresHeaderSize + (j * sizeof(short)), groups[groups.Count - 1 - j]);
        }

        return payload;
    }

    /// <summary>Lays out a PostgreSQL payload from its header fields and its groups.</summary>
    /// <remarks>
    /// <paramref name="ndigits"/> is taken rather than derived from <paramref name="groups"/>, so
    /// that a test can compose a header which disagrees with the payload it arrived in.
    /// </remarks>
    /// <param name="ndigits">The count to put in the header.</param>
    /// <param name="weight">The base-10000 weight of the first group.</param>
    /// <param name="sign">The sign word.</param>
    /// <param name="dscale">The display scale.</param>
    /// <param name="groups">The groups, most significant first.</param>
    /// <returns>The payload.</returns>
    public static byte[] PostgresLayout(
        int ndigits, int weight, ushort sign, int dscale, ReadOnlySpan<ushort> groups)
    {
        var payload = new byte[PostgresHeaderSize + (groups.Length * sizeof(short))];
        WriteBigEndian(payload, 0, ndigits);
        WriteBigEndian(payload, 2, weight);
        WriteBigEndian(payload, 4, sign);
        WriteBigEndian(payload, 6, dscale);

        for (var j = 0; j < groups.Length; j++)
        {
            WriteBigEndian(payload, PostgresHeaderSize + (j * sizeof(short)), groups[j]);
        }

        return payload;
    }

    /// <summary>Composes a PostgreSQL payload for a non-finite value.</summary>
    /// <param name="sign">One of the three non-finite sign words.</param>
    /// <returns>The eight-byte payload.</returns>
    public static byte[] PostgresNonFinite(ushort sign)
    {
        var payload = new byte[PostgresHeaderSize];
        WriteBigEndian(payload, 4, sign);

        return payload;
    }

    /// <summary>Reads a PostgreSQL payload as the exact rational it carries.</summary>
    /// <param name="payload">The payload.</param>
    /// <returns>
    /// The exact mantissa and the scale at which it is exact, which is not the payload's display
    /// scale: the payload may carry digits below it, and it may claim more of them than it carries.
    /// </returns>
    public static (BigInteger Unscaled, int Scale) PostgresExact(ReadOnlySpan<byte> payload)
    {
        var ndigits = ReadSigned(payload, 0);
        var weight = ReadSigned(payload, 2);
        var sign = ReadBigEndian(payload, 4);

        var value = BigInteger.Zero;
        for (var j = 0; j < ndigits; j++)
        {
            value = (value * NBase) + ReadBigEndian(payload, PostgresHeaderSize + (j * sizeof(short)));
        }

        // The last group sits at 10000^(weight - ndigits + 1), so that many groups are fractional.
        var fractionalGroups = ndigits - 1 - weight;
        var scale = Math.Max(fractionalGroups, 0) * PostgresDecDigits;
        if (fractionalGroups < 0)
        {
            value *= BigInteger.Pow(NBase, -fractionalGroups);
        }

        return (sign == PostgresNegative ? -value : value, scale);
    }

    /// <summary>
    /// The value a reader is required to produce from an exact rational and a display scale.
    /// </summary>
    /// <remarks>
    /// Stated as a search rather than as a formula: the answer carries as many fractional digits as
    /// the display scale asks for and the mantissa allows, and it is rounded once from the exact
    /// input to that scale. Rounding to the display scale first and reducing afterwards rounds
    /// twice and can land a unit in the last place away.
    /// </remarks>
    /// <param name="unscaled">The exact mantissa.</param>
    /// <param name="exactScale">The scale at which <paramref name="unscaled"/> is exact.</param>
    /// <param name="displayScale">The payload's <c>dscale</c>.</param>
    /// <returns>The required result.</returns>
    public static OracleValue PostgresExpected(BigInteger unscaled, int exactScale, int displayScale)
    {
        var wanted = Math.Min(displayScale, BigIntegerOracle.MaxScale);

        for (var scale = wanted; scale > 0; scale--)
        {
            var candidate = Rescale(unscaled, exactScale, scale);
            if (BigInteger.Abs(candidate) <= BigIntegerOracle.MaxMagnitude)
            {
                return new OracleValue(candidate, scale);
            }
        }

        var atZero = Rescale(unscaled, exactScale, 0);
        if (BigInteger.Abs(atZero) > BigIntegerOracle.MaxMagnitude)
        {
            throw new OverflowException();
        }

        return new OracleValue(atZero, 0);
    }

    /// <summary>Composes the ClickHouse payload of an unscaled value at a width.</summary>
    /// <remarks>
    /// The format is two's complement little-endian, which is what
    /// <see cref="BigInteger.ToByteArray(bool, bool)"/> already produces, so this is the base class
    /// library answering rather than a second implementation of the same idea.
    /// </remarks>
    /// <param name="unscaled">The signed value to encode.</param>
    /// <param name="size">The payload width in bytes.</param>
    /// <returns>The payload.</returns>
    /// <exception cref="OverflowException">The value does not fit the width.</exception>
    public static byte[] ClickHouse(BigInteger unscaled, int size)
    {
        var minimal = unscaled.ToByteArray(isUnsigned: false, isBigEndian: false);
        var fill = unscaled.Sign < 0 ? (byte)0xFF : (byte)0x00;

        if (minimal.Length > size)
        {
            for (var i = size; i < minimal.Length; i++)
            {
                if (minimal[i] != fill)
                {
                    throw new OverflowException();
                }
            }

            if (((minimal[size - 1] & 0x80) != 0) != (unscaled.Sign < 0))
            {
                throw new OverflowException();
            }
        }

        var payload = new byte[size];
        payload.AsSpan().Fill(fill);
        minimal.AsSpan(0, Math.Min(size, minimal.Length)).CopyTo(payload);

        return payload;
    }

    /// <summary>Reads a ClickHouse payload as the signed value it carries.</summary>
    /// <param name="payload">The payload.</param>
    /// <returns>The value.</returns>
    public static BigInteger ClickHouseValue(ReadOnlySpan<byte> payload) =>
        new(payload, isUnsigned: false, isBigEndian: false);

    /// <summary>The mantissa a value carries at another scale, rounded once, half to even.</summary>
    /// <param name="unscaled">The mantissa.</param>
    /// <param name="from">The scale it is exact at.</param>
    /// <param name="to">The scale wanted.</param>
    /// <returns>The mantissa at <paramref name="to"/>.</returns>
    public static BigInteger Rescale(BigInteger unscaled, int from, int to)
    {
        if (to >= from)
        {
            return unscaled * BigInteger.Pow(10, to - from);
        }

        var divisor = BigInteger.Pow(10, from - to);
        var sign = unscaled.Sign;
        var magnitude = BigInteger.Abs(unscaled);
        var quotient = BigInteger.DivRem(magnitude, divisor, out var remainder);
        var twice = remainder * 2;

        if (twice > divisor || (twice == divisor && !quotient.IsEven))
        {
            quotient += BigInteger.One;
        }

        return sign < 0 ? -quotient : quotient;
    }

    /// <summary>Writes one big-endian sixteen-bit header field.</summary>
    /// <remarks>
    /// The narrowing is deliberate and is written as such. A header field is sixteen bits of wire:
    /// a negative weight, or a count past 32767, goes out as the bits it is. This assembly is built
    /// with checked arithmetic in Debug, where an unannotated narrowing cast throws instead of
    /// truncating, so the intent has to be stated rather than assumed.
    /// </remarks>
    private static void WriteBigEndian(byte[] destination, int offset, int value)
    {
        unchecked
        {
            destination[offset] = (byte)(value >> 8);
            destination[offset + 1] = (byte)value;
        }
    }

    private static ushort ReadBigEndian(ReadOnlySpan<byte> source, int offset) =>
        (ushort)((source[offset] << 8) | source[offset + 1]);

    /// <summary>Reads a header field that the format defines as signed.</summary>
    private static short ReadSigned(ReadOnlySpan<byte> source, int offset) =>
        unchecked((short)ReadBigEndian(source, offset));
}
