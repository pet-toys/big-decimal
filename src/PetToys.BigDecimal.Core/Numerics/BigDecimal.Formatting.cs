using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace PetToys.BigDecimal.Numerics;

public readonly partial struct BigDecimal : IFormattable, ISpanFormattable, IUtf8SpanFormattable
{
    internal const int MaxCharsPlain = 1 + MaxDigits + 1 + MaxScale;

    // The significand and nothing more. Scaling moves the point, which is an integer beside the
    // digits, and an integer part wider than the significand reads its trailing zeros from
    // DigitText.At rather than from the buffer, so no shift widens what has to be stored.
    private const int DigitBufferLength = MaxDigits + 1;

    // What the framework accepts after a standard specifier: F999999999 renders a billion
    // characters there, F1000000000 throws.
    private const int MaxStandardPrecision = 999_999_999;

    // The intermediate buffer the UTF-8 overload and ToString format through. It covers every
    // standard specifier at any precision a culture can carry (NumberFormatInfo allows 99 decimal
    // digits, and the widest rendering is 334 characters plus a pattern), so only an explicit
    // precision or a long custom format goes past it and rents.
    private const int StackFormatChars = 512;

    /// <summary>Formats the value for the current culture, trailing zeros included.</summary>
    /// <returns>The formatted value.</returns>
    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <summary>Formats the value for the given culture, trailing zeros included.</summary>
    /// <returns>The formatted value.</returns>
    public string ToString(IFormatProvider? formatProvider) => ToString(null, formatProvider);

    /// <summary>Formats the value with the given format string, for the current culture.</summary>
    /// <remarks>
    /// The supported specifiers are <c>C</c>, <c>E</c>, <c>F</c>, <c>G</c>, <c>N</c>, <c>P</c> and
    /// <c>R</c>, with their lowercase forms and an optional precision, which is the set
    /// <see cref="decimal"/> supports. Any other single letter throws
    /// <see cref="FormatException"/>; any longer string is a custom numeric format string.
    /// </remarks>
    /// <returns>The formatted value.</returns>
    /// <exception cref="FormatException">The format string is not supported.</exception>
    public string ToString(string? format) => ToString(format, CultureInfo.CurrentCulture);

    /// <summary>Formats the value with the given format string and culture.</summary>
    /// <remarks>
    /// The supported specifiers are <c>C</c>, <c>E</c>, <c>F</c>, <c>G</c>, <c>N</c>, <c>P</c> and
    /// <c>R</c>, with their lowercase forms and an optional precision, which is the set
    /// <see cref="decimal"/> supports. Any other single letter throws
    /// <see cref="FormatException"/>; any longer string is a custom numeric format string.
    /// <c>N</c>, <c>C</c> and <c>P</c> group the integer part by their own group size list in
    /// full: the first entry sizes the rightmost group and the last entry repeats, an entry of
    /// zero stops grouping, and an empty list does not group at all. They also lay a negative
    /// value out by their own negative pattern, so a culture that writes <c>(1,234.5)</c> gets
    /// that rather than a leading sign.
    /// </remarks>
    /// <returns>The formatted value.</returns>
    /// <exception cref="FormatException">The format string is not supported.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        var info = NumberFormatInfo.GetInstance(formatProvider);
        Span<char> buffer = stackalloc char[StackFormatChars];
        if (TryFormatCore(buffer, out var written, format, info, out var required))
        {
            return new string(buffer[..written]);
        }

        // The failed call reported the length it needed, so the string is the only allocation and
        // there is no cap on how long a format string may ask for.
        return string.Create(
            required,
            (Value: this, Format: format, Info: info),
            static (span, state) => state.Value.TryFormatCore(span, out _, state.Format, state.Info, out _));
    }

    /// <summary>Tries to format the value into a span of characters.</summary>
    /// <remarks>
    /// The supported specifiers are <c>C</c>, <c>E</c>, <c>F</c>, <c>G</c>, <c>N</c>, <c>P</c> and
    /// <c>R</c>, with their lowercase forms and an optional precision, which is the set
    /// <see cref="decimal"/> supports. Any other single letter throws
    /// <see cref="FormatException"/>; any longer string is a custom numeric format string.
    /// </remarks>
    /// <returns><see langword="true"/> when the destination was long enough; otherwise <see langword="false"/>.</returns>
    /// <exception cref="FormatException">The format string is not supported.</exception>
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null) =>
        TryFormatCore(destination, out charsWritten, format, NumberFormatInfo.GetInstance(provider), out _);

    /// <summary>Tries to format the value into a span of UTF-8 bytes.</summary>
    /// <remarks>
    /// The specifiers and the text are the same as for the <see cref="char"/> overload.
    /// </remarks>
    /// <returns><see langword="true"/> when the value was written; otherwise <see langword="false"/>.</returns>
    /// <exception cref="FormatException">The format string is not supported.</exception>
    public bool TryFormat(
        Span<byte> utf8Destination,
        out int bytesWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        bytesWritten = 0;
        var info = NumberFormatInfo.GetInstance(provider);
        Span<char> chars = stackalloc char[StackFormatChars];
        if (TryFormatCore(chars, out var charsWritten, format, info, out var required))
        {
            return Encoding.UTF8.TryGetBytes(chars[..charsWritten], utf8Destination, out bytesWritten);
        }

        // Longer than the stack bound, so the intermediate is rented rather than fixed. What the
        // caller passed is still the only thing that decides whether this call succeeds: the
        // internal buffer is sized from the text, which is what D4 was about.
        var rented = ArrayPool<char>.Shared.Rent(required);
        try
        {
            return TryFormatCore(rented, out charsWritten, format, info, out _)
                && Encoding.UTF8.TryGetBytes(rented.AsSpan(0, charsWritten), utf8Destination, out bytesWritten);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    internal bool TryFormatInvariant(Span<char> destination, out int charsWritten) =>
        TryFormatCore(destination, out charsWritten, default, NumberFormatInfo.InvariantInfo, out _);

    /// <summary>Formats the value, reporting the length the destination would have needed.</summary>
    /// <remarks>
    /// Every formatter computes its exact length before writing anything, so reporting it costs
    /// nothing and is what sizes the buffers of the two callers that have to make one. A constant
    /// in either of those places is how the UTF-8 overload came to decline destinations that were
    /// long enough.
    /// </remarks>
    private bool TryFormatCore(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        NumberFormatInfo info,
        out int required)
    {
        if (!IsStandardSpecifier(format, out var specifier, out var precision))
        {
            return TryFormatCustom(destination, out charsWritten, format, info, out required);
        }

        var lowercase = !format.IsEmpty && char.IsLower(format[0]);

        switch (specifier)
        {
            case 'F':
                return TryFormatFixed(destination, out charsWritten, out required, Fixed(info, precision), 0);
            case 'N':
                return TryFormatFixed(destination, out charsWritten, out required, Number(info, precision), 0);
            case 'C':
                return TryFormatFixed(destination, out charsWritten, out required, Currency(info, precision), 0);
            case 'P':
                return TryFormatFixed(destination, out charsWritten, out required, Percent(info, precision), 2);
            case 'E':
                return TryFormatExponential(destination, out charsWritten, out required, info, precision ?? 6, lowercase);
            case 'G' or 'R':
                return precision is > 0
                    ? TryFormatSignificant(destination, out charsWritten, out required, info, precision.Value, lowercase)
                    : TryFormatFixed(destination, out charsWritten, out required, Stored(info), 0);
            default:
                ThrowUnsupportedFormat(format);
                charsWritten = 0;
                required = 0;
                return false;
        }
    }

    /// <summary>Renders the value with a fixed count of fractional digits, inside a pattern.</summary>
    /// <remarks>
    /// One formatter for <c>F</c>, <c>N</c>, <c>C</c>, <c>P</c> and the plain rendering of
    /// <c>G</c>: they differ in the culture members they read, in how they lay the sign out and in
    /// whether they group, and in nothing else.
    /// </remarks>
    private bool TryFormatFixed(
        Span<char> destination,
        out int charsWritten,
        out int required,
        scoped in FixedStyle style,
        int scaleShift)
    {
        Span<char> buffer = stackalloc char[DigitBufferLength];
        var digits = Decompose(buffer);
        digits.Shift(scaleShift);
        digits.RoundTo(style.Precision);

        return TryWriteFixed(destination, out charsWritten, out required, ref digits, in style);
    }

    /// <summary>Renders the value in scientific notation, which is what <c>E</c> does.</summary>
    private bool TryFormatExponential(
        Span<char> destination,
        out int charsWritten,
        out int required,
        NumberFormatInfo info,
        int precision,
        bool lowercase)
    {
        Span<char> buffer = stackalloc char[DigitBufferLength];
        var digits = Decompose(buffer);
        digits.RoundToSignificant(precision + 1);

        return TryWriteScientific(
            destination, out charsWritten, out required, ref digits, info, precision, lowercase, 3);
    }

    /// <summary>
    /// Renders the value to a count of significant digits, which is what <c>G</c> with a precision
    /// does.
    /// </summary>
    /// <remarks>
    /// Measured against <see cref="decimal"/> rather than read out of the documentation: the
    /// rounding leaves trailing zeros and they are stripped, so <c>1.000</c> with <c>G3</c> is
    /// <c>1</c>; the rendering is fixed-point while the decimal exponent is greater than -5 and
    /// less than the precision, and scientific otherwise; and the scientific form here writes two
    /// exponent digits where <c>E</c> writes three.
    /// </remarks>
    private bool TryFormatSignificant(
        Span<char> destination,
        out int charsWritten,
        out int required,
        NumberFormatInfo info,
        int precision,
        bool lowercase)
    {
        Span<char> buffer = stackalloc char[DigitBufferLength];
        var digits = Decompose(buffer);
        digits.RoundToSignificant(precision);
        digits.StripTrailingZeros();

        var exponent = digits.Point - 1;
        if (digits.IsZero || (exponent > -5 && exponent < precision))
        {
            var style = new FixedStyle
            {
                DecimalSeparator = info.NumberDecimalSeparator,
                GroupSeparator = string.Empty,
                Symbol = string.Empty,
                NegativeLayout = SignPrefix,
                PositiveLayout = DigitsOnly,
                NegativeSign = info.NegativeSign,
                Precision = Math.Max(digits.Count - digits.Point, 0),
            };

            return TryWriteFixed(destination, out charsWritten, out required, ref digits, in style);
        }

        return TryWriteScientific(
            destination,
            out charsWritten,
            out required,
            ref digits,
            info,
            Math.Max(digits.Count - 1, 0),
            lowercase,
            2);
    }

    private static bool TryWriteFixed(
        Span<char> destination,
        out int charsWritten,
        out int required,
        scoped ref DigitText digits,
        scoped in FixedStyle style)
    {
        var layout = digits.Negative ? style.NegativeLayout : style.PositiveLayout;
        var integerLength = Math.Max(digits.Point, 1);
        var separators = CountGroupSeparators(integerLength, style.GroupSizes);

        required = integerLength
            + (separators * style.GroupSeparator.Length)
            + (style.Precision > 0 ? style.DecimalSeparator.Length + style.Precision : 0)
            + LayoutLength(layout, style.NegativeSign.Length, style.Symbol.Length);
        if (destination.Length < required)
        {
            charsWritten = 0;
            return false;
        }

        var pos = 0;
        foreach (var token in layout)
        {
            switch (token)
            {
                case 'n':
                    pos += WriteGroupedInteger(
                        destination[pos..], ref digits, style.GroupSizes, style.GroupSeparator, separators);
                    pos += WriteFraction(destination[pos..], ref digits, style.DecimalSeparator, style.Precision);
                    break;
                case '-':
                    style.NegativeSign.CopyTo(destination[pos..]);
                    pos += style.NegativeSign.Length;
                    break;
                case '(' or ')' or ' ':
                    destination[pos++] = token;
                    break;
                default:
                    style.Symbol.CopyTo(destination[pos..]);
                    pos += style.Symbol.Length;
                    break;
            }
        }

        charsWritten = pos;
        return true;
    }

    private static bool TryWriteScientific(
        Span<char> destination,
        out int charsWritten,
        out int required,
        scoped ref DigitText digits,
        NumberFormatInfo info,
        int precision,
        bool lowercase,
        int minimumExponentDigits)
    {
        var exponent = digits.IsZero ? 0 : digits.Point - 1;
        var sign = digits.Negative ? info.NegativeSign : string.Empty;
        var point = info.NumberDecimalSeparator;
        var exponentSign = exponent < 0 ? info.NegativeSign : info.PositiveSign;

        Span<char> exponentDigits = stackalloc char[10];
        var exponentLength = 0;
        var magnitude = Math.Abs(exponent);
        do
        {
            exponentDigits[exponentLength++] = (char)('0' + (magnitude % 10));
            magnitude /= 10;
        }
        while (magnitude > 0);

        while (exponentLength < minimumExponentDigits)
        {
            exponentDigits[exponentLength++] = '0';
        }

        required = sign.Length
            + 1
            + (precision > 0 ? point.Length + precision : 0)
            + 1
            + exponentSign.Length
            + exponentLength;
        if (destination.Length < required)
        {
            charsWritten = 0;
            return false;
        }

        var pos = 0;
        sign.CopyTo(destination[pos..]);
        pos += sign.Length;
        destination[pos++] = digits.At(0);

        if (precision > 0)
        {
            point.CopyTo(destination[pos..]);
            pos += point.Length;

            for (var i = 1; i <= precision; i++)
            {
                destination[pos++] = digits.At(i);
            }
        }

        destination[pos++] = lowercase ? 'e' : 'E';
        exponentSign.CopyTo(destination[pos..]);
        pos += exponentSign.Length;
        for (var i = exponentLength - 1; i >= 0; i--)
        {
            destination[pos++] = exponentDigits[i];
        }

        charsWritten = pos;
        return true;
    }

    /// <summary>Counts what a layout writes besides the digits themselves.</summary>
    private static int LayoutLength(string layout, int signLength, int symbolLength)
    {
        var length = 0;
        foreach (var token in layout)
        {
            length += token switch
            {
                'n' => 0,
                '-' => signLength,
                '(' or ')' or ' ' => 1,
                _ => symbolLength,
            };
        }

        return length;
    }

    private static int WriteFraction(
        Span<char> destination,
        scoped ref DigitText digits,
        string separator,
        int precision)
    {
        if (precision <= 0)
        {
            return 0;
        }

        separator.CopyTo(destination);
        var pos = separator.Length;

        for (var i = 0; i < precision; i++)
        {
            destination[pos++] = digits.At(digits.Point + i);
        }

        return pos;
    }

    /// <summary>
    /// Returns the group size at a position in the walk, the last entry standing in for every
    /// position past the end of the list.
    /// </summary>
    /// <remarks>
    /// A size of zero stops grouping and an empty list never groups, so both come back as zero and
    /// the callers below treat that as the end of the walk. The framework's own setter rejects a
    /// zero anywhere but last and rejects anything above nine, so no other value can arrive here.
    /// </remarks>
    private static int GroupSizeAt(ReadOnlySpan<int> groupSizes, int index) =>
        groupSizes.IsEmpty ? 0 : groupSizes[Math.Min(index, groupSizes.Length - 1)];

    /// <summary>Counts the separators that grouping an integer part of this length will write.</summary>
    /// <remarks>
    /// Needed before anything is written, because the destination-length check depends on it. The
    /// walk runs once per group rather than once per digit, and it divides nothing.
    /// </remarks>
    private static int CountGroupSeparators(int length, ReadOnlySpan<int> groupSizes)
    {
        var separators = 0;
        var remaining = length;

        for (var index = 0; ; index++)
        {
            var size = GroupSizeAt(groupSizes, index);
            if (size == 0 || remaining <= size)
            {
                return separators;
            }

            remaining -= size;
            separators++;
        }
    }

    /// <summary>Writes the integer part, grouped, and returns how many characters it took.</summary>
    /// <remarks>
    /// Right to left, because that is how grouping is defined: the first entry of the size list
    /// sizes the rightmost group. The digits come through <see cref="DigitText.At"/>, so an
    /// integer part wider than the significand writes the trailing zeros it implies without them
    /// having to be materialised anywhere.
    /// </remarks>
    private static int WriteGroupedInteger(
        Span<char> destination,
        scoped ref DigitText digits,
        ReadOnlySpan<int> groupSizes,
        string groupSeparator,
        int separators)
    {
        var length = Math.Max(digits.Point, 1);
        var written = length + (separators * groupSeparator.Length);
        var write = written;
        var read = length;

        for (var index = 0; ; index++)
        {
            var size = GroupSizeAt(groupSizes, index);
            if (size == 0 || read <= size)
            {
                break;
            }

            read -= size;
            write -= size;
            for (var i = 0; i < size; i++)
            {
                destination[write + i] = IntegerDigit(ref digits, read + i);
            }

            write -= groupSeparator.Length;
            groupSeparator.CopyTo(destination[write..]);
        }

        for (var i = 0; i < read; i++)
        {
            destination[i] = IntegerDigit(ref digits, i);
        }

        return written;
    }

    // A value below one has no integer digits at all and renders a single zero, which is the only
    // case where the position asked for is not a position among the digits.
    private static char IntegerDigit(scoped ref DigitText digits, int index) =>
        digits.Point <= 0 ? '0' : digits.At(index);

    private readonly ref struct FixedStyle
    {
        internal ReadOnlySpan<int> GroupSizes { get; init; }

        internal string GroupSeparator { get; init; }

        internal string DecimalSeparator { get; init; }

        internal string Symbol { get; init; }

        internal string NegativeLayout { get; init; }

        internal string PositiveLayout { get; init; }

        internal string NegativeSign { get; init; }

        internal int Precision { get; init; }
    }

    private const string SignPrefix = "-n";

    private const string DigitsOnly = "n";

    /// <summary>The style of a value rendered as stored, which is what <c>G</c> and <c>R</c> do.</summary>
    private FixedStyle Stored(NumberFormatInfo info) => new()
    {
        DecimalSeparator = info.NumberDecimalSeparator,
        GroupSeparator = string.Empty,
        Symbol = string.Empty,
        NegativeLayout = SignPrefix,
        PositiveLayout = DigitsOnly,
        NegativeSign = info.NegativeSign,
        Precision = Scale,
    };

    private static FixedStyle Fixed(NumberFormatInfo info, int? precision) => new()
    {
        DecimalSeparator = info.NumberDecimalSeparator,
        GroupSeparator = string.Empty,
        Symbol = string.Empty,
        NegativeLayout = SignPrefix,
        PositiveLayout = DigitsOnly,
        NegativeSign = info.NegativeSign,
        Precision = precision ?? info.NumberDecimalDigits,
    };

    private static FixedStyle Number(NumberFormatInfo info, int? precision) => new()
    {
        DecimalSeparator = info.NumberDecimalSeparator,
        GroupSeparator = info.NumberGroupSeparator,
        GroupSizes = CultureData.NumberGroupSizes(info),
        Symbol = string.Empty,
        NegativeLayout = NumberPatterns.NumberNegative(info.NumberNegativePattern),
        PositiveLayout = DigitsOnly,
        NegativeSign = info.NegativeSign,
        Precision = precision ?? info.NumberDecimalDigits,
    };

    private static FixedStyle Currency(NumberFormatInfo info, int? precision) => new()
    {
        DecimalSeparator = info.CurrencyDecimalSeparator,
        GroupSeparator = info.CurrencyGroupSeparator,
        GroupSizes = CultureData.CurrencyGroupSizes(info),
        Symbol = info.CurrencySymbol,
        NegativeLayout = NumberPatterns.CurrencyNegative(info.CurrencyNegativePattern),
        PositiveLayout = NumberPatterns.CurrencyPositive(info.CurrencyPositivePattern),
        NegativeSign = info.NegativeSign,
        Precision = precision ?? info.CurrencyDecimalDigits,
    };

    private static FixedStyle Percent(NumberFormatInfo info, int? precision) => new()
    {
        DecimalSeparator = info.PercentDecimalSeparator,
        GroupSeparator = info.PercentGroupSeparator,
        GroupSizes = CultureData.PercentGroupSizes(info),
        Symbol = info.PercentSymbol,
        NegativeLayout = NumberPatterns.PercentNegative(info.PercentNegativePattern),
        PositiveLayout = NumberPatterns.PercentPositive(info.PercentPositivePattern),
        NegativeSign = info.NegativeSign,
        Precision = precision ?? info.PercentDecimalDigits,
    };

    private DigitText Decompose(Span<char> buffer)
    {
        if (IsZero)
        {
            return new DigitText(buffer, 0, 1, false);
        }

        var count = WriteDigits(buffer);
        return new DigitText(buffer, count, count - Scale, IsNegative);
    }

    private int WriteDigits(Span<char> destination)
    {
        Span<ulong> magnitude = stackalloc ulong[WordCount];
        var len = CopyMagnitude(magnitude);
        if (len == 0)
        {
            destination[0] = '0';
            return 1;
        }

        Span<char> reversed = stackalloc char[MaxDigits + 1];
        var count = 0;
        while (len > 0)
        {
            len = Words.DivRemSmall(magnitude, len, Words.TenPow19Divisor, out var chunk);
            for (var i = 0; i < 19; i++)
            {
                reversed[count++] = (char)('0' + (int)(chunk % 10));
                chunk /= 10;
                if (len == 0 && chunk == 0)
                {
                    break;
                }
            }
        }

        for (var i = 0; i < count; i++)
        {
            destination[i] = reversed[count - 1 - i];
        }

        return count;
    }

    /// <summary>Decides whether a format string is a standard specifier, and reads its precision.</summary>
    /// <remarks>
    /// A letter followed only by digits is a standard specifier; anything else, including a letter
    /// followed by anything but digits, is a custom numeric format string. That is the framework's
    /// own rule and it is why <c>Z</c> throws while <c>ZZ</c> renders as literal text.
    /// </remarks>
    private static bool IsStandardSpecifier(ReadOnlySpan<char> format, out char specifier, out int? precision)
    {
        specifier = 'G';
        precision = null;

        if (format.IsEmpty)
        {
            return true;
        }

        if (!char.IsAsciiLetter(format[0]))
        {
            return false;
        }

        long value = 0;
        for (var i = 1; i < format.Length; i++)
        {
            if (!char.IsAsciiDigit(format[i]))
            {
                return false;
            }

            value = (value * 10) + (format[i] - '0');
            if (value > MaxStandardPrecision)
            {
                ThrowUnsupportedFormat(format);
            }
        }

        specifier = char.ToUpperInvariant(format[0]);
        precision = format.Length > 1 ? (int)value : null;
        return true;
    }

    [DoesNotReturn]
    private static void ThrowUnsupportedFormat(ReadOnlySpan<char> format) =>
        throw new FormatException(
            $"The format string '{format}' is not supported by BigDecimal. Use C, E, F, G, N, P or R "
            + "with an optional precision, or a custom numeric format string.");
}
