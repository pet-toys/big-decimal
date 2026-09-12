using System;
using System.Globalization;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Custom numeric format strings: every format string that is not a standard specifier.
/// </summary>
/// <remarks>
/// The engine runs twice over the chosen section, once measuring and once writing, so the length
/// promised and the length written are the same code. Every rule was read back from
/// <see cref="decimal"/>: <c>##.##</c> renders zero as the empty string, an explicit negative
/// section suppresses the sign, an empty one falls back to the positive section with a sign, and
/// a custom format reads the number separators even when it scales by a percent.
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

        // A value that rounds to zero takes the zero section, rounded once by the first section:
        // "0.00;(0.00)" renders a ten-thousandth as 0.00, "0.0000;(0.00)" as 0.0000.
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

    private DigitText Prepare(Span<char> buffer, scoped CustomSpec spec, out int exponent)
    {
        var digits = Decompose(buffer);
        exponent = 0;

        if (spec.Scientific)
        {
            // Not clamped to one: ".0E+0" renders 1234.5678 as .1E+4.
            var mantissaDigits = spec.IntegerPlaceholders;
            exponent = digits.IsZero ? 0 : digits.Point - mantissaDigits;
            digits.Shift(mantissaDigits - digits.Point);
            digits.RoundTo(spec.FractionPlaceholders);

            // A carry is one more place in the exponent: 9.99 with "0.0e+00" is 1.0e+01.
            if (!digits.IsZero && digits.Point != mantissaDigits)
            {
                exponent += digits.Point - mantissaDigits;
                digits.Shift(mantissaDigits - digits.Point);
            }

            // A mantissa that rounded away takes the exponent with it: "E+0" renders 1234.5678 as E+0.
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

    // One section serves every value with a sign; two make the second the negative one, three
    // the third the zero one. An empty negative section falls back to the positive one plus a
    // sign, which is why ";;" renders -0.5 as a lone minus.
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

    // The index of the closing quote, or the length: an unterminated literal runs to the end.
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
                    // Before any placeholder a comma is a literal: ",.00" renders 0.5 as .50.
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
                        // A second exponent token is literal text: "0.0E+0E+0" renders 1.2E+3E+0.
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

        // Zeros only: in "0.0E+0#" the '#' is a mantissa placeholder, and 1.594e19 renders 1.5E+199.
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
                        // The leftmost placeholder carries every digit the others cannot.
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

                        // With no integer placeholder the digits go here: ".##" renders 1234.5678 as 1234.57.
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

                    // Any other exponent token is literal text, written whole so its zeros are not filled.
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

    // Every significant integer digit, but never fewer than the zero placeholders demand; a value
    // below one has none of its own, so "##.##" renders zero as nothing.
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

    // Right-aligned, so a part widened by zero placeholders reads leading zeros from DigitText.At.
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

    // Writes into a destination, or counts what it would write.
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
