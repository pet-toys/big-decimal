using System;
using System.Globalization;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Custom numeric format strings: every format string that is not a standard specifier.
/// </summary>
/// <remarks>
/// <para>
/// The engine runs twice over the chosen section, once measuring and once writing. Measuring by
/// rendering rather than by arithmetic over the descriptor is deliberate: the length a formatter
/// promises and the length it writes are then the same code, and that promise is what the caller's
/// destination is checked against.
/// </para>
/// <para>
/// Every rule here was read back from <see cref="decimal"/> rather than from documentation. The
/// ones that surprise: <c>##.##</c> renders zero as the empty string, an explicit negative section
/// suppresses the sign entirely, an empty negative section falls back to the positive one with a
/// sign, and a custom format reads the number separators even when it scales by a percent.
/// </para>
/// </remarks>
public readonly partial struct BigDecimal
{
    private const char PerMille = '‰';

    private bool TryFormatCustom(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        NumberFormatInfo info,
        out int required)
    {
        var section = SelectSection(format, IsNegative, IsZero, out var writeSign);
        var spec = ParseCustom(section);

        Span<char> buffer = stackalloc char[DigitBufferLength];
        var digits = Prepare(buffer, spec, out var exponent);

        // A value that is not zero but rounds to zero is rendered as a zero, so the section can
        // only be settled after the rounding that the section itself decides. Three sections send
        // it to the third, and fewer send it back to the first: a negative that rounds away is no
        // longer negative, so "0.00;(0.00)" renders a ten-thousandth as 0.00 rather than (0.00).
        //
        // The digits are not prepared again. The framework rounds once, by the section it first
        // chose, and renders that zero through the section it lands on, which is why
        // "0.0000;(0.00)" renders a ten-thousandth as 0.0000 rather than 0.0001.
        if (digits.IsZero && !IsZero)
        {
            section = ZeroFallbackSection(format);
            writeSign = false;
            spec = ParseCustom(section);
        }

        writeSign = writeSign && !digits.IsZero;

        var measure = new Emitter(default, true);
        WriteCustom(ref measure, section, spec, digits, info, writeSign, exponent);
        required = measure.Length;

        if (destination.Length < required)
        {
            charsWritten = 0;
            return false;
        }

        var writer = new Emitter(destination, false);
        WriteCustom(ref writer, section, spec, digits, info, writeSign, exponent);
        charsWritten = writer.Length;
        return true;
    }

    /// <summary>Decomposes the value and applies the scaling and rounding a section asks for.</summary>
    private DigitText Prepare(Span<char> buffer, scoped CustomSpec spec, out int exponent)
    {
        var digits = Decompose(buffer);
        exponent = 0;

        if (spec.Scientific)
        {
            // Not clamped to one: ".0E+0" asks for no integer digit at all and renders 1234.5678
            // as .1E+4, where clamping would render it as 1.2E+3.
            var mantissaDigits = spec.IntegerPlaceholders;
            exponent = digits.IsZero ? 0 : digits.Point - mantissaDigits;
            digits.Shift(mantissaDigits - digits.Point);
            digits.RoundTo(spec.FractionPlaceholders);

            // Rounding can carry, and a carry is one more place in the exponent rather than one
            // more digit in the mantissa: 9.99 with "0.0e+00" is 1.0e+01, not 10.0e+00.
            if (!digits.IsZero && digits.Point != mantissaDigits)
            {
                exponent += digits.Point - mantissaDigits;
                digits.Shift(mantissaDigits - digits.Point);
            }

            // A mantissa that rounded away to nothing takes the exponent with it, as it does for
            // the standard specifier: "E+0" renders 1234.5678 as E+0, not as E+4.
            if (digits.IsZero)
            {
                exponent = 0;
            }
        }
        else
        {
            digits.Shift(spec.Scaling);
            digits.RoundTo(spec.FractionPlaceholders);
        }

        return digits;
    }

    /// <summary>The section a value that rounded away to zero is rendered by.</summary>
    private static ReadOnlySpan<char> ZeroFallbackSection(ReadOnlySpan<char> format)
    {
        SplitSections(format, out var first, out var second);

        if (first < 0)
        {
            return format;
        }

        var positive = format[..first];
        if (second < 0)
        {
            return positive;
        }

        var zeroSection = format[(second + 1)..];
        return zeroSection.IsEmpty ? positive : zeroSection;
    }

    /// <summary>Chooses the section a value is rendered by, and says whether a sign goes with it.</summary>
    /// <remarks>
    /// One section serves every value and takes a sign. Two make the second the negative one, and
    /// three make the third the zero one. An empty section is not an empty rendering: the negative
    /// one falling back to the positive one plus a sign is why <c>";;"</c> renders -0.5 as a lone
    /// minus sign.
    /// </remarks>
    private static ReadOnlySpan<char> SelectSection(
        ReadOnlySpan<char> format,
        bool negative,
        bool zero,
        out bool writeSign)
    {
        SplitSections(format, out var first, out var second);

        if (first < 0)
        {
            writeSign = negative;
            return format;
        }

        var positive = format[..first];
        var negativeSection = second < 0 ? format[(first + 1)..] : format.Slice(first + 1, second - first - 1);

        if (zero && second >= 0)
        {
            var zeroSection = format[(second + 1)..];
            writeSign = false;
            return zeroSection.IsEmpty ? positive : zeroSection;
        }

        if (negative && !negativeSection.IsEmpty)
        {
            writeSign = false;
            return negativeSection;
        }

        writeSign = negative;
        return positive;
    }

    private static void SplitSections(ReadOnlySpan<char> format, out int first, out int second)
    {
        first = -1;
        second = -1;

        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c is '\\')
            {
                i++;
                continue;
            }

            if (c is '\'' or '"')
            {
                i = SkipLiteral(format, i);
                continue;
            }

            if (c is not ';')
            {
                continue;
            }

            if (first < 0)
            {
                first = i;
            }
            else
            {
                second = i;
                return;
            }
        }
    }

    // Returns the index of the closing quote, or the length when the literal is unterminated,
    // which the framework treats as running to the end of the format.
    private static int SkipLiteral(ReadOnlySpan<char> format, int start)
    {
        var quote = format[start];
        for (var i = start + 1; i < format.Length; i++)
        {
            if (format[i] == quote)
            {
                return i;
            }
        }

        return format.Length;
    }

    private static CustomSpec ParseCustom(ReadOnlySpan<char> section)
    {
        var spec = default(CustomSpec);
        var seenPoint = false;
        var firstZero = -1;
        var lastZero = -1;
        var pendingCommas = 0;
        var sawIntegerPlaceholder = false;

        for (var i = 0; i < section.Length; i++)
        {
            var c = section[i];

            switch (c)
            {
                case '\\':
                    i++;
                    continue;
                case '\'' or '"':
                    i = SkipLiteral(section, i);
                    continue;
                case '0' or '#':
                    if (seenPoint)
                    {
                        if (c is '0')
                        {
                            lastZero = spec.FractionPlaceholders;
                        }

                        spec.FractionPlaceholders++;
                    }
                    else
                    {
                        if (pendingCommas > 0 && sawIntegerPlaceholder)
                        {
                            spec.Grouped = true;
                        }

                        pendingCommas = 0;
                        sawIntegerPlaceholder = true;
                        if (c is '0' && firstZero < 0)
                        {
                            firstZero = spec.IntegerPlaceholders;
                        }

                        spec.IntegerPlaceholders++;
                    }

                    continue;
                case ',':
                    // A comma before any digit placeholder is a literal, not a scale: ",.00"
                    // renders 0.5 as .50, not as .00.
                    if (!seenPoint && sawIntegerPlaceholder)
                    {
                        pendingCommas++;
                    }

                    continue;
                case '.':
                    if (!seenPoint)
                    {
                        seenPoint = true;

                        // Commas left hanging against the point scale rather than group.
                        spec.Scaling -= 3 * pendingCommas;
                        pendingCommas = 0;
                    }

                    continue;
                case '%':
                    spec.Scaling += 2;
                    continue;
                case PerMille:
                    spec.Scaling += 3;
                    continue;
                case 'E' or 'e':
                    if (TryReadExponent(section, i, out var end, out var minDigits, out var alwaysSign))
                    {
                        // A second exponent section is literal text: "0.0E+0E+0" renders as
                        // 1.2E+3E+0, the trailing zeros written rather than filled. A first one is
                        // scientific even with no digit placeholder before it, which looks like
                        // literal text and is not: "E+0" renders -0.5 as -E+1, the mantissa having
                        // rounded to nothing and carried into the exponent.
                        if (spec.Scientific)
                        {
                            i = end - 1;
                            continue;
                        }

                        spec.Scientific = true;
                        spec.ExponentStart = i;
                        spec.ExponentEnd = end;
                        spec.ExponentMinDigits = minDigits;
                        spec.ExponentAlwaysSign = alwaysSign;
                        spec.ExponentLowercase = c is 'e';
                        i = end - 1;
                    }

                    continue;
                default:
                    continue;
            }
        }

        // Commas trailing the digits with no point after them scale as well.
        spec.Scaling -= 3 * pendingCommas;

        spec.RequiredInteger = firstZero < 0 ? 0 : spec.IntegerPlaceholders - firstZero;
        spec.RequiredFraction = lastZero + 1;
        return spec;
    }

    private static bool TryReadExponent(
        ReadOnlySpan<char> section,
        int start,
        out int end,
        out int minDigits,
        out bool alwaysSign)
    {
        end = start + 1;
        minDigits = 0;
        alwaysSign = false;

        if (end < section.Length && section[end] is '+' or '-')
        {
            alwaysSign = section[end] is '+';
            end++;
        }

        // Zeros only. A '#' after the exponent is a mantissa placeholder, not part of the token:
        // "0.0E+0#" renders 1.594e19 as 1.5E+199, the trailing 9 being the mantissa's second
        // fractional digit written after the exponent.
        while (end < section.Length && section[end] is '0')
        {
            minDigits++;
            end++;
        }

        return minDigits > 0;
    }

    private static void WriteCustom(
        ref Emitter emitter,
        ReadOnlySpan<char> section,
        scoped CustomSpec spec,
        scoped DigitText digits,
        NumberFormatInfo info,
        bool writeSign,
        int exponent)
    {
        if (writeSign)
        {
            emitter.Write(info.NegativeSign);
        }

        var integerDigits = IntegerDigitCount(digits, spec);
        var fractionDigits = FractionDigitCount(digits, spec);
        var groupSizes = spec.Grouped ? CultureData.NumberGroupSizes(info) : default;
        var written = 0;
        var fraction = 0;
        var integerPlaceholder = 0;
        var seenPoint = false;

        for (var i = 0; i < section.Length; i++)
        {
            var c = section[i];

            switch (c)
            {
                case '\\':
                    if (i + 1 < section.Length)
                    {
                        emitter.Write(section[i + 1]);
                    }

                    i++;
                    continue;
                case '\'' or '"':
                    var close = SkipLiteral(section, i);
                    foreach (var literal in section[Math.Min(i + 1, section.Length)..Math.Min(close, section.Length)])
                    {
                        emitter.Write(literal);
                    }

                    i = close;
                    continue;
                case '0' or '#':
                    if (seenPoint)
                    {
                        if (fraction < fractionDigits)
                        {
                            emitter.Write(digits.At(digits.Point + fraction));
                            fraction++;
                        }
                    }
                    else
                    {
                        // The leftmost placeholder carries every digit the placeholders cannot,
                        // which is what makes a lone "#" render a four-digit integer part in full.
                        var overflow = integerPlaceholder == 0
                            ? Math.Max(integerDigits - spec.IntegerPlaceholders, 0)
                            : 0;
                        var takes = overflow
                            + (spec.IntegerPlaceholders - integerPlaceholder <= integerDigits - written ? 1 : 0);

                        WriteIntegerDigits(
                            ref emitter, digits, integerDigits, ref written, takes, spec.Grouped, groupSizes, info);
                        integerPlaceholder++;
                    }

                    continue;
                case '.':
                    if (!seenPoint)
                    {
                        seenPoint = true;

                        // Integer digits with no placeholder to sit in are written here rather than
                        // dropped: ".##" renders 1234.5678 as 1234.57, not as .57.
                        if (spec.IntegerPlaceholders == 0)
                        {
                            WriteIntegerDigits(
                                ref emitter,
                                digits,
                                integerDigits,
                                ref written,
                                integerDigits - written,
                                spec.Grouped,
                                groupSizes,
                                info);
                        }

                        if (fractionDigits > 0)
                        {
                            emitter.Write(info.NumberDecimalSeparator);
                        }
                    }

                    continue;
                case ',':
                    continue;
                case '%':
                    emitter.Write(info.PercentSymbol);
                    continue;
                case PerMille:
                    emitter.Write(info.PerMilleSymbol);
                    continue;
                default:
                    if (spec.Scientific && i == spec.ExponentStart)
                    {
                        WriteExponent(ref emitter, spec, info, exponent);
                        i = spec.ExponentEnd - 1;
                        continue;
                    }

                    // Any other exponent token is literal text, and it is written whole so that the
                    // digit placeholders inside it are not filled from the value. The parser skips
                    // exactly the same range, so the two passes agree.
                    if (c is 'E' or 'e' && TryReadExponent(section, i, out var literalEnd, out _, out _))
                    {
                        foreach (var token in section[i..literalEnd])
                        {
                            emitter.Write(token);
                        }

                        i = literalEnd - 1;
                        continue;
                    }

                    emitter.Write(c);
                    continue;
            }
        }
    }

    private static void WriteIntegerDigits(
        ref Emitter emitter,
        scoped DigitText digits,
        int integerDigits,
        ref int written,
        int take,
        bool grouped,
        ReadOnlySpan<int> groupSizes,
        NumberFormatInfo info)
    {
        for (var index = 0; index < take; index++)
        {
            emitter.Write(IntegerDigit(digits, written, integerDigits));
            written++;

            var remaining = integerDigits - written;
            if (grouped && remaining > 0 && IsGroupBoundary(remaining, groupSizes))
            {
                emitter.Write(info.NumberGroupSeparator);
            }
        }
    }

    private static void WriteExponent(
        ref Emitter emitter,
        scoped CustomSpec spec,
        NumberFormatInfo info,
        int exponent)
    {
        emitter.Write(spec.ExponentLowercase ? 'e' : 'E');

        if (exponent < 0)
        {
            emitter.Write(info.NegativeSign);
        }
        else if (spec.ExponentAlwaysSign)
        {
            emitter.Write(info.PositiveSign);
        }

        Span<char> buffer = stackalloc char[10];
        var length = 0;
        var magnitude = Math.Abs(exponent);
        do
        {
            buffer[length++] = (char)('0' + (magnitude % 10));
            magnitude /= 10;
        }
        while (magnitude > 0);

        while (length < spec.ExponentMinDigits)
        {
            buffer[length++] = '0';
        }

        for (var i = length - 1; i >= 0; i--)
        {
            emitter.Write(buffer[i]);
        }
    }

    // How many digits the integer part writes: every significant one it has, but never fewer than
    // the zero placeholders demand. A value below one has none of its own, which is why "##.##"
    // renders zero as nothing at all.
    private static int IntegerDigitCount(scoped DigitText digits, scoped CustomSpec spec) =>
        Math.Max(digits.IsZero ? 0 : Math.Max(digits.Point, 0), spec.RequiredInteger);

    // A trailing '#' holding a zero is not written; a trailing '0' is.
    private static int FractionDigitCount(scoped DigitText digits, scoped CustomSpec spec)
    {
        for (var i = spec.FractionPlaceholders; i > spec.RequiredFraction; i--)
        {
            if (digits.At(digits.Point + i - 1) != '0')
            {
                return i;
            }
        }

        return spec.RequiredFraction;
    }

    // The digits are right-aligned against the integer part, so a part widened by zero
    // placeholders reads its leading zeros from before the first significant digit.
    private static char IntegerDigit(scoped DigitText digits, int index, int integerDigits)
    {
        var offset = integerDigits - Math.Max(digits.Point, 0);
        return digits.At(index - offset);
    }

    private static bool IsGroupBoundary(int remaining, ReadOnlySpan<int> groupSizes)
    {
        var cumulative = 0;
        for (var index = 0; ; index++)
        {
            var size = GroupSizeAt(groupSizes, index);
            if (size == 0)
            {
                return false;
            }

            cumulative += size;
            if (cumulative == remaining)
            {
                return true;
            }

            if (cumulative > remaining)
            {
                return false;
            }
        }
    }

    private ref struct CustomSpec
    {
        internal int IntegerPlaceholders;
        internal int RequiredInteger;
        internal int FractionPlaceholders;
        internal int RequiredFraction;
        internal bool Grouped;
        internal int Scaling;
        internal bool Scientific;
        internal int ExponentMinDigits;
        internal bool ExponentAlwaysSign;
        internal bool ExponentLowercase;
        internal int ExponentStart;
        internal int ExponentEnd;
    }

    /// <summary>Writes into a destination, or counts what it would write.</summary>
    private ref struct Emitter
    {
        private readonly Span<char> destination;
        private readonly bool measuring;

        internal Emitter(Span<char> destination, bool measuring)
        {
            this.destination = destination;
            this.measuring = measuring;
        }

        internal int Length { get; private set; }

        internal void Write(char value)
        {
            if (!measuring)
            {
                destination[Length] = value;
            }

            Length++;
        }

        internal void Write(string value)
        {
            if (!measuring)
            {
                value.CopyTo(destination[Length..]);
            }

            Length += value.Length;
        }
    }
}
