using System;
using System.Buffers.Binary;
using System.Diagnostics;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The PostgreSQL binary <c>numeric</c> wire format: four <see cref="short"/> header fields in
/// network byte order followed by base-10000 digit groups.
/// </summary>
/// <remarks>
/// The layout is <c>ndigits</c>, <c>weight</c>, <c>sign</c>, <c>dscale</c>, then <c>ndigits</c>
/// groups, and the value is the sign applied to the sum of <c>digits[j] * 10000^(weight - j)</c>.
/// <c>dscale</c> carries the trailing zeros. The three non-finite values are the sign codes
/// PostgreSQL 14 introduced. Writing never overflows and never rounds; reading can do both,
/// because the payload is not bounded by this type's range.
/// </remarks>
internal static class PostgresNumeric
{
    /// <summary>The four header fields, each a <see cref="short"/>.</summary>
    internal const int HeaderSize = 4 * sizeof(short);

    /// <summary>The largest payload this type can produce, header included.</summary>
    /// <remarks>78 digits padded onto the base-10000 grid by at most 3 is 21 groups.</remarks>
    internal const int MaxByteCount = HeaderSize + (MaxGroups * sizeof(short));

    /// <summary>Decimal digits per group. PostgreSQL calls this <c>DEC_DIGITS</c>.</summary>
    private const int DecDigits = 4;

    /// <summary>The base of a group. PostgreSQL calls this <c>NBASE</c>.</summary>
    private const int NBase = 10_000;

    private const int MaxGroups = 21;

    /// <summary>PostgreSQL's own fourteen-bit display scale field; a wider payload is malformed.</summary>
    private const int MaxDisplayScale = 16_383;

    private const ushort SignPositive = 0x0000;
    private const ushort SignNegative = 0x4000;
    private const ushort SignNaN = 0xC000;
    private const ushort SignPositiveInfinity = 0xD000;
    private const ushort SignNegativeInfinity = 0xF000;

    // Four words padded by at most three digits is five, plus one for MulAddSmall's carry.
    private const int EncodeWords = BigDecimal.WordCount + 2;

    // Six words is 115 digits, well past the 78 a result carries plus guard digits; a
    // multiplication may push it to seven, and MulAddSmall needs an eighth for its carry.
    private const int DecodeCap = 6;
    private const int DecodeWords = DecodeCap + 2;

    /// <summary>Answers how many bytes a value needs, before anything is written.</summary>
    /// <remarks>
    /// The same decomposition the write costs, paid twice because Npgsql asks a handler for a
    /// length before it hands over a buffer.
    /// </remarks>
    internal static int GetByteCount(BigDecimal value)
    {
        if (value.IsNonFinite)
        {
            return HeaderSize;
        }

        Span<ushort> groups = stackalloc ushort[MaxGroups];
        var count = Decompose(value, groups, out _, out var lowest);

        return HeaderSize + ((count - lowest) * sizeof(short));
    }

    /// <summary>Writes a value in the PostgreSQL binary <c>numeric</c> format.</summary>
    /// <returns>
    /// <see langword="false"/> only when <paramref name="destination"/> is shorter than
    /// <see cref="GetByteCount"/> answered; every value of this type can be written.
    /// </returns>
    internal static bool TryWrite(BigDecimal value, Span<byte> destination, out int written)
    {
        written = 0;

        if (value.IsNonFinite)
        {
            if (destination.Length < HeaderSize)
            {
                return false;
            }

            var code = BigDecimal.IsNaN(value)
                ? SignNaN
                : BigDecimal.IsPositiveInfinity(value) ? SignPositiveInfinity : SignNegativeInfinity;

            WriteHeader(destination, 0, 0, code, 0);
            written = HeaderSize;
            return true;
        }

        Span<ushort> groups = stackalloc ushort[MaxGroups];
        var count = Decompose(value, groups, out var weight, out var lowest);
        var ndigits = count - lowest;
        var need = HeaderSize + (ndigits * sizeof(short));

        if (destination.Length < need)
        {
            return false;
        }

        WriteHeader(
            destination,
            ndigits,
            ndigits == 0 ? 0 : weight,
            value.IsNegative ? SignNegative : SignPositive,
            value.Scale);

        // Decomposed least significant first, written most significant first.
        for (var j = 0; j < ndigits; j++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                destination[(HeaderSize + (j * sizeof(short)))..], groups[count - 1 - j]);
        }

        written = need;
        return true;
    }

    /// <summary>Reads a PostgreSQL binary <c>numeric</c> payload.</summary>
    /// <returns>The value, rounded half to even where the fraction is longer than the type holds.</returns>
    /// <exception cref="FormatException">The payload is not a well-formed <c>numeric</c>.</exception>
    /// <exception cref="OverflowException">The integer part is larger than the type's magnitude.</exception>
    internal static BigDecimal Read(ReadOnlySpan<byte> source)
    {
        if (source.Length < HeaderSize)
        {
            throw Malformed("A PostgreSQL numeric payload carries an eight-byte header.");
        }

        var ndigits = BinaryPrimitives.ReadInt16BigEndian(source);
        var weight = BinaryPrimitives.ReadInt16BigEndian(source[2..]);
        var sign = BinaryPrimitives.ReadUInt16BigEndian(source[4..]);
        var dscale = BinaryPrimitives.ReadUInt16BigEndian(source[6..]);

        if (ndigits < 0 || source.Length != HeaderSize + (ndigits * sizeof(short)))
        {
            throw Malformed("A PostgreSQL numeric payload carries exactly ndigits groups.");
        }

        // As the server does; a dscale this type cannot hold is clamped further down, not refused.
        if (dscale > MaxDisplayScale)
        {
            throw Malformed("A PostgreSQL numeric display scale is at most 16383.");
        }

        switch (sign)
        {
            case SignNaN:
            case SignPositiveInfinity:
            case SignNegativeInfinity:
                if (ndigits != 0)
                {
                    throw Malformed("A PostgreSQL numeric with a non-finite sign carries no groups.");
                }

                return sign switch
                {
                    SignNaN => BigDecimal.NaN,
                    SignPositiveInfinity => BigDecimal.PositiveInfinity,
                    _ => BigDecimal.NegativeInfinity,
                };
            case SignPositive:
            case SignNegative:
                break;
            default:
                throw Malformed("A PostgreSQL numeric sign is one of the five documented codes.");
        }

        var isNegative = sign == SignNegative;

        // The server strips leading zero groups; a payload from anywhere else may not have.
        var first = 0;
        ushort leadingGroup;
        while (true)
        {
            if (first == ndigits)
            {
                return BigDecimal.FromWords([], false, Math.Min((int)dscale, BigDecimal.MaxScale));
            }

            leadingGroup = ReadGroup(source, first);
            if (leadingGroup != 0)
            {
                break;
            }

            first++;
        }

        // The decimal exponent of the leading digit: the value lies in [10^leading, 10^(leading+1)).
        var leading = (DecDigits * (weight - first)) + Words.DecimalDigitCount(leadingGroup) - 1;

        // At 10^78 or above no scale can represent the value.
        if (leading >= BigDecimal.MaxDigits + 1)
        {
            BigDecimal.ThrowMantissaOverflow();
        }

        // Decided before a digit is accumulated, so the value is rounded once.
        var scale = Math.Min(
            Math.Min((int)dscale, BigDecimal.MaxScale),
            Math.Max(BigDecimal.MaxDigits - leading, 0));

        Span<ulong> accumulator = stackalloc ulong[DecodeWords];
        var length = 0;
        var sticky = false;
        var last = first;

        for (var j = first; j < ndigits; j++)
        {
            var group = ReadGroup(source, j);

            if (length <= DecodeCap)
            {
                length = Words.MulAddSmall(accumulator, length, NBase, group);
                last = j;
            }
            else
            {
                // Past the usable width only a non-zero digit matters, to decide a tie.
                sticky |= group != 0;
            }
        }

        // At most two scales are tried, each rounded from the accumulator rather than from the
        // previous attempt: 78 digits fit only sometimes, and rounding twice lands an ulp off.
        Span<ulong> attempt = stackalloc ulong[DecodeWords];
        while (true)
        {
            accumulator.CopyTo(attempt);
            var attemptLength = Align(attempt, length, weight - last, scale, sticky, isNegative);

            if (attemptLength <= BigDecimal.WordCount)
            {
                return BigDecimal.Pack(attempt, attemptLength, isNegative, scale);
            }

            if (scale == 0)
            {
                BigDecimal.ThrowMantissaOverflow();
            }

            scale--;
        }
    }

    // Brings an accumulated group run to a scale, rounding once where the scale asks for fewer
    // digits than the run carries. groupExponent is the base-10000 exponent of the lowest group.
    private static int Align(
        Span<ulong> value, int length, int groupExponent, int scale, bool sticky, bool isNegative)
    {
        var shift = (DecDigits * groupExponent) + scale;

        if (shift > 0)
        {
            return Words.ScaleUp(value, length, shift);
        }

        if (shift == 0)
        {
            return length;
        }

        // A non-zero tail below the accumulator decides a tie; a lowest digit forced from zero
        // to one says "something below" without crossing a boundary.
        if (sticky && Words.RemSmall(value, length, Words.Pow10Divisors[1]) == 0)
        {
            length = Words.AddOne(value, length);
        }

        return Words.DivPow10Round(value, length, -shift, isNegative, MidpointRounding.ToEven);
    }

    // Checked on every read, the leading digit's included, so a group at or above 10000 is
    // reported as malformed rather than as an overflow.
    private static ushort ReadGroup(ReadOnlySpan<byte> source, int index)
    {
        var group = BinaryPrimitives.ReadUInt16BigEndian(source[(HeaderSize + (index * sizeof(short)))..]);

        return group < NBase ? group : throw Malformed("A PostgreSQL numeric group is below 10000.");
    }

    private static void WriteHeader(Span<byte> destination, int ndigits, int weight, ushort sign, int dscale)
    {
        BinaryPrimitives.WriteInt16BigEndian(destination, (short)ndigits);
        BinaryPrimitives.WriteInt16BigEndian(destination[2..], (short)weight);
        BinaryPrimitives.WriteUInt16BigEndian(destination[4..], sign);
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..], (ushort)dscale);
    }

    // Splits a finite value into base-10000 groups, least significant first, padding the scale
    // up to a multiple of four so the groups line up with the point. lowest is the index of the
    // lowest non-zero group, where the written groups stop.
    private static int Decompose(BigDecimal value, Span<ushort> groups, out int weight, out int lowest)
    {
        Span<ulong> work = stackalloc ulong[EncodeWords];
        var length = value.CopyMagnitude(work);
        var scale = value.Scale;
        var pad = (DecDigits - (scale % DecDigits)) % DecDigits;

        if (pad > 0 && length > 0)
        {
            length = Words.ScaleUp(work, length, pad);
        }

        var count = 0;
        while (length > 0)
        {
            length = Words.DivRemSmall(work, length, Words.Pow10Divisors[DecDigits], out var group);
            Debug.Assert(count < MaxGroups, "the group count is bounded by the magnitude and the padding");
            groups[count++] = (ushort)group;
        }

        lowest = 0;
        while (lowest < count && groups[lowest] == 0)
        {
            lowest++;
        }

        weight = count - 1 - ((scale + pad) / DecDigits);
        return count;
    }

    private static FormatException Malformed(string message) => new(message);
}
