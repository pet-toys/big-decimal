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
/// <c>dscale</c> is carried beside the digits rather than implied by them, which is what lets
/// trailing zeros survive a round trip. All three non-finite values cross this wire, as the sign
/// codes PostgreSQL 14 introduced.
/// <para>
/// The two directions are deliberately asymmetric. Writing never overflows and never rounds -
/// PostgreSQL holds far more in both directions than this type does - so its only failure is a
/// destination the caller made too short, reported as <see langword="false"/>. Reading can do
/// both, because the payload is not bounded by this type's range.
/// </para>
/// </remarks>
internal static class PostgresNumeric
{
    /// <summary>The four header fields, each a <see cref="short"/>.</summary>
    internal const int HeaderSize = 4 * sizeof(short);

    /// <summary>The largest payload this type can produce, header included.</summary>
    /// <remarks>
    /// Derived, not quoted: 78 digits of magnitude, padded onto the base-10000 grid by at most 3,
    /// is 81 digits and so 21 groups. Zero groups are stripped and the scale rides in
    /// <c>dscale</c>, so the bound does not grow with the scale.
    /// </remarks>
    internal const int MaxByteCount = HeaderSize + (MaxGroups * sizeof(short));

    /// <summary>Decimal digits per group. PostgreSQL calls this <c>DEC_DIGITS</c>.</summary>
    private const int DecDigits = 4;

    /// <summary>The base of a group. PostgreSQL calls this <c>NBASE</c>.</summary>
    private const int NBase = 10_000;

    /// <summary>The most groups a value of this type spans. See <see cref="MaxByteCount"/>.</summary>
    private const int MaxGroups = 21;

    /// <summary>The widest display scale the format carries.</summary>
    /// <remarks>
    /// PostgreSQL's own fourteen-bit field, so this is about the payload being well formed. Whether
    /// the scale is representable here is a separate question, answered by clamping further down.
    /// </remarks>
    private const int MaxDisplayScale = 16_383;

    private const ushort SignPositive = 0x0000;
    private const ushort SignNegative = 0x4000;
    private const ushort SignNaN = 0xC000;
    private const ushort SignPositiveInfinity = 0xD000;
    private const ushort SignNegativeInfinity = 0xF000;

    // Four words padded onto the group grid by at most three decimal digits is five words, and
    // MulAddSmall needs one more for the carry it may write.
    private const int EncodeWords = BigDecimal.WordCount + 2;

    // The decode accumulator. Six words is 115 decimal digits, comfortably more than the 78 a
    // result can carry plus the guard digits the rounding needs; a multiplication may push it to
    // seven, and MulAddSmall needs the eighth for its carry.
    private const int DecodeCap = 6;
    private const int DecodeWords = DecodeCap + 2;

    /// <summary>Answers how many bytes a value needs, before anything is written.</summary>
    /// <remarks>
    /// This costs the same decomposition the write costs, so a caller that asks and then writes
    /// pays for it twice. That is the shape the driver imposes rather than a choice: Npgsql asks a
    /// handler for a length and only then hands it a buffer.
    /// </remarks>
    /// <param name="value">The value to be written.</param>
    /// <returns>The exact number of bytes <see cref="TryWrite"/> will write.</returns>
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
    /// <param name="value">The value to write. Every value of this type can be written.</param>
    /// <param name="destination">Receives the payload.</param>
    /// <param name="written">Receives the number of bytes written, or zero.</param>
    /// <returns>
    /// <see langword="true"/> when the value was written; <see langword="false"/> when
    /// <paramref name="destination"/> was shorter than <see cref="GetByteCount"/> answered.
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

        // The groups come out of the decomposition least significant first and go onto the wire
        // most significant first.
        for (var j = 0; j < ndigits; j++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                destination[(HeaderSize + (j * sizeof(short)))..], groups[count - 1 - j]);
        }

        written = need;
        return true;
    }

    /// <summary>Reads a PostgreSQL binary <c>numeric</c> payload.</summary>
    /// <param name="source">The payload.</param>
    /// <returns>The value the payload describes, rounded half to even where it is longer than the
    /// type can hold.</returns>
    /// <exception cref="FormatException">
    /// The payload is not a well-formed <c>numeric</c>: too short for its header, a length that
    /// disagrees with <c>ndigits</c>, a <c>dscale</c> past the format's own 16383, an unrecognised
    /// sign code, digit groups under a non-finite sign, or a group at or above 10000.
    /// </exception>
    /// <exception cref="OverflowException">
    /// The integer part of the value is larger than the type's magnitude. The fraction is rounded
    /// rather than refused, so this is the only way a read fails on the value itself.
    /// </exception>
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

        // Checked before the sign is acted on: a non-finite code is still a header, and one that
        // disagrees with the length it arrived in is a framing error rather than a value.
        if (ndigits < 0 || source.Length != HeaderSize + (ndigits * sizeof(short)))
        {
            throw Malformed("A PostgreSQL numeric payload carries exactly ndigits groups.");
        }

        // No more permissive than the server: PostgreSQL refuses a dscale outside its fourteen-bit
        // field rather than masking it. A dscale this type cannot hold is clamped, not refused.
        if (dscale > MaxDisplayScale)
        {
            throw Malformed("A PostgreSQL numeric display scale is at most 16383.");
        }

        switch (sign)
        {
            case SignNaN:
            case SignPositiveInfinity:
            case SignNegativeInfinity:
                // A non-finite value has no digits, so a group under one of these codes is a byte
                // no value can use. Refused rather than skipped, which would also skip the range
                // check every other group passes.
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

        // A leading zero group carries no digits and would put the leading digit in the wrong
        // place. The server strips them; a payload from anywhere else may not have.
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

        // 78 digits is the widest magnitude, so a value at 10^78 or above has no representation at
        // any scale and the fraction cannot buy anything back.
        if (leading >= BigDecimal.MaxDigits + 1)
        {
            BigDecimal.ThrowMantissaOverflow();
        }

        // Decided before a digit is accumulated, so the value is rounded once. Anything wider
        // passes MaxScale or the mantissa; rounding to dscale first would round twice.
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
                // Past the width the result can use. The digits themselves no longer matter, only
                // whether any of them was non-zero, which is what decides a tie further up.
                sticky |= group != 0;
            }
        }

        // At most two scales are tried, each rounded from the accumulator as it stands rather than
        // from the previous attempt: `scale` is the widest that fits 78 digits, which the
        // magnitude holds only sometimes, and rounding one from the other would land an ulp off.
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

    /// <summary>
    /// Brings an accumulated group run to a scale, rounding once where the scale asks for fewer
    /// digits than the run carries.
    /// </summary>
    /// <param name="value">The accumulated groups, replaced by the unscaled result.</param>
    /// <param name="length">The number of significant words in <paramref name="value"/>.</param>
    /// <param name="groupExponent">The base-10000 exponent of the run's lowest group.</param>
    /// <param name="scale">The scale to bring it to.</param>
    /// <param name="sticky">Whether a non-zero group was consumed below the run.</param>
    /// <param name="isNegative">The sign, which some rounding modes read.</param>
    /// <returns>The number of significant words in the result.</returns>
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

        // A non-zero tail below the accumulator decides a tie, and the rounding helper only sees
        // what it divides away. Forcing a zero lowest digit to one says "something below" without
        // crossing a boundary: one is neither zero nor five.
        if (sticky && Words.RemSmall(value, length, Words.Pow10Divisors[1]) == 0)
        {
            length = Words.AddOne(value, length);
        }

        return Words.DivPow10Round(value, length, -shift, isNegative, MidpointRounding.ToEven);
    }

    /// <summary>Reads one group, refusing a value the base cannot carry.</summary>
    /// <remarks>
    /// The check lives here rather than in the accumulate loop so that it also covers the groups
    /// read before it: the leading zero scan, and the leading digit's own group, whose digit count
    /// decides whether the value overflows. Without it a group at or above 10000 could be reported
    /// as a value too large for the type rather than as the malformed payload it is.
    /// </remarks>
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

    /// <summary>Splits a value into base-10000 groups aligned on the decimal point.</summary>
    /// <remarks>
    /// The groups only line up with the point when the scale is a multiple of four, so the
    /// magnitude is padded up to the next multiple first. Padding by at most three digits is what
    /// makes the group count bounded by <see cref="MaxGroups"/>.
    /// </remarks>
    /// <param name="value">The value to split. Must be finite.</param>
    /// <param name="groups">Receives the groups, least significant first.</param>
    /// <param name="weight">Receives the base-10000 weight of the most significant group.</param>
    /// <param name="lowest">
    /// Receives the index of the lowest non-zero group, which is where the written groups stop.
    /// </param>
    /// <returns>The number of groups written, zero for a zero value.</returns>
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
