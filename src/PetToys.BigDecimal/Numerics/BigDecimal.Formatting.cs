using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace PetToys.BigDecimal.Numerics;

public readonly partial struct BigDecimal : IFormattable, ISpanFormattable, IUtf8SpanFormattable
{
    internal const int MaxCharsPlain = 1 + MaxDigits + 1 + MaxScale;

    private const int MaxFormatChars = 64 * 1024;

    /// <summary>Formats the value for the current culture, trailing zeros included.</summary>
    /// <returns>The formatted value.</returns>
    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);

    /// <summary>Formats the value for the given culture, trailing zeros included.</summary>
    /// <returns>The formatted value.</returns>
    public string ToString(IFormatProvider? formatProvider) => ToString(null, formatProvider);

    /// <summary>Formats the value with the given format string, for the current culture.</summary>
    /// <remarks>
    /// The supported specifiers are <c>G</c> (the value as stored, trailing zeros included),
    /// <c>F</c>, <c>N</c> and <c>E</c>, with their lowercase forms and an optional precision. Any
    /// other specifier throws <see cref="FormatException"/>.
    /// </remarks>
    /// <returns>The formatted value.</returns>
    /// <exception cref="FormatException">The format string is not supported.</exception>
    public string ToString(string? format) => ToString(format, CultureInfo.CurrentCulture);

    /// <summary>Formats the value with the given format string and culture.</summary>
    /// <remarks>
    /// The supported specifiers are <c>G</c> (the value as stored, trailing zeros included),
    /// <c>F</c>, <c>N</c> and <c>E</c>, with their lowercase forms and an optional precision. Any
    /// other specifier throws <see cref="FormatException"/>.
    /// </remarks>
    /// <returns>The formatted value.</returns>
    /// <exception cref="FormatException">The format string is not supported.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        Span<char> buffer = stackalloc char[MaxCharsPlain + 32];
        if (TryFormat(buffer, out var written, format, formatProvider))
        {
            return new string(buffer[..written]);
        }

        return FormatViaBuilder(format, formatProvider);
    }

    /// <summary>Tries to format the value into a span of characters.</summary>
    /// <remarks>
    /// The supported specifiers are <c>G</c> (the value as stored, trailing zeros included),
    /// <c>F</c>, <c>N</c> and <c>E</c>, with their lowercase forms and an optional precision. Any
    /// other specifier throws <see cref="FormatException"/>.
    /// </remarks>
    /// <returns><see langword="true"/> when the destination was long enough; otherwise <see langword="false"/>.</returns>
    /// <exception cref="FormatException">The format string is not supported.</exception>
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        var info = NumberFormatInfo.GetInstance(provider);
        var specifier = format.IsEmpty ? 'G' : char.ToUpperInvariant(format[0]);

        if (!TryParsePrecision(format, out var precision))
        {
            ThrowUnsupportedFormat(format);
        }

        switch (specifier)
        {
            case 'F' or 'N':
                return TryFormatFixed(destination, out charsWritten, info, precision ?? info.NumberDecimalDigits, specifier == 'N');

            case 'E':
                return TryFormatExponential(destination, out charsWritten, info, precision ?? 6, format[0] == 'e');

            case 'G':
                return TryFormatPlain(destination, out charsWritten, info);

            default:
                ThrowUnsupportedFormat(format);
                charsWritten = 0;
                return false;
        }
    }

    /// <summary>Tries to format the value into a span of UTF-8 bytes.</summary>
    /// <remarks>
    /// The supported specifiers are <c>G</c> (the value as stored, trailing zeros included),
    /// <c>F</c>, <c>N</c> and <c>E</c>, with their lowercase forms and an optional precision. Any
    /// other specifier throws <see cref="FormatException"/>. Use
    /// <see cref="ToString(string?, IFormatProvider?)"/> for a wide value with a long explicit
    /// precision, which this overload can decline to write.
    /// </remarks>
    /// <returns><see langword="true"/> when the value was written; otherwise <see langword="false"/>.</returns>
    /// <exception cref="FormatException">The format string is not supported.</exception>
    public bool TryFormat(
        Span<byte> utf8Destination,
        out int bytesWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        Span<char> chars = stackalloc char[MaxCharsPlain + 32];
        if (!TryFormat(chars, out var charsWritten, format, provider))
        {
            bytesWritten = 0;
            return false;
        }

        return Encoding.UTF8.TryGetBytes(chars[..charsWritten], utf8Destination, out bytesWritten);
    }

    internal bool TryFormatInvariant(Span<char> destination, out int charsWritten) =>
        TryFormatPlain(destination, out charsWritten, NumberFormatInfo.InvariantInfo);

    private bool TryFormatPlain(Span<char> destination, out int charsWritten, NumberFormatInfo info)
    {
        Span<char> digits = stackalloc char[MaxDigits + 1];
        var digitCount = WriteDigits(digits);
        var scale = Scale;
        var sign = IsNegative ? info.NegativeSign : string.Empty;
        var point = info.NumberDecimalSeparator;

        var integerDigits = Math.Max(digitCount - scale, 1);
        var leadingZeros = Math.Max(scale - digitCount + 1, 0);
        var total = sign.Length + integerDigits + (scale > 0 ? point.Length + scale : 0);

        if (destination.Length < total)
        {
            charsWritten = 0;
            return false;
        }

        var pos = 0;
        sign.CopyTo(destination[pos..]);
        pos += sign.Length;

        if (leadingZeros > 0)
        {
            destination[pos++] = '0';
        }
        else
        {
            digits[..integerDigits].CopyTo(destination[pos..]);
            pos += integerDigits;
        }

        if (scale > 0)
        {
            point.CopyTo(destination[pos..]);
            pos += point.Length;

            var padding = leadingZeros > 0 ? leadingZeros - 1 : 0;
            destination.Slice(pos, padding).Fill('0');
            pos += padding;

            ReadOnlySpan<char> fraction = leadingZeros > 0 ? digits[..digitCount] : digits[integerDigits..digitCount];
            fraction.CopyTo(destination[pos..]);
            pos += fraction.Length;
        }

        charsWritten = pos;
        return true;
    }

    private bool TryFormatFixed(
        Span<char> destination,
        out int charsWritten,
        NumberFormatInfo info,
        int precision,
        bool grouped)
    {
        var rounded = Round(this, Math.Min(precision, MaxScale), MidpointRounding.AwayFromZero);
        Span<char> digits = stackalloc char[MaxDigits + 1];
        var digitCount = rounded.WriteDigits(digits);
        var scale = rounded.Scale;

        var integerDigits = Math.Max(digitCount - scale, 1);
        ReadOnlySpan<char> integerPart = digitCount > scale ? digits[..integerDigits] : "0";

        var sign = IsNegative && !rounded.IsZero ? info.NegativeSign : string.Empty;
        var point = info.NumberDecimalSeparator;
        var groupSeparator = grouped ? info.NumberGroupSeparator : string.Empty;
        var groupSize = grouped ? (info.NumberGroupSizes.Length > 0 ? info.NumberGroupSizes[0] : 3) : 0;
        var separators = grouped && groupSize > 0 ? (integerPart.Length - 1) / groupSize : 0;

        var total = sign.Length
            + integerPart.Length
            + (separators * groupSeparator.Length)
            + (precision > 0 ? point.Length + precision : 0);
        if (destination.Length < total)
        {
            charsWritten = 0;
            return false;
        }

        var pos = 0;
        sign.CopyTo(destination[pos..]);
        pos += sign.Length;

        for (var i = 0; i < integerPart.Length; i++)
        {
            if (grouped && i > 0 && groupSize > 0 && (integerPart.Length - i) % groupSize == 0)
            {
                groupSeparator.CopyTo(destination[pos..]);
                pos += groupSeparator.Length;
            }

            destination[pos++] = integerPart[i];
        }

        if (precision > 0)
        {
            point.CopyTo(destination[pos..]);
            pos += point.Length;

            var available = Math.Min(scale, precision);
            var fractionStart = digitCount > scale ? integerDigits : 0;
            var leadingZeros = digitCount > scale ? 0 : Math.Min(scale - digitCount, precision);
            destination.Slice(pos, leadingZeros).Fill('0');
            pos += leadingZeros;

            var copied = Math.Min(digitCount - fractionStart, available - leadingZeros);
            if (copied > 0)
            {
                digits.Slice(fractionStart, copied).CopyTo(destination[pos..]);
                pos += copied;
            }

            var trailing = precision - leadingZeros - Math.Max(copied, 0);
            destination.Slice(pos, trailing).Fill('0');
            pos += trailing;
        }

        charsWritten = pos;
        return true;
    }

    private bool TryFormatExponential(
        Span<char> destination,
        out int charsWritten,
        NumberFormatInfo info,
        int precision,
        bool lowercase)
    {
        Span<char> digits = stackalloc char[MaxDigits + 3];
        digits.Fill('0');
        var digitCount = WriteDigits(digits);
        if (IsZero)
        {
            digits[0] = '0';
            digitCount = 1;
        }

        var exponent = IsZero ? 0 : digitCount - 1 - Scale;

        digitCount = RoundSignificand(digits, digitCount, precision + 1, out var carried);
        if (carried)
        {
            exponent++;
        }

        var point = info.NumberDecimalSeparator;
        var mantissaLen = 1 + (precision > 0 ? point.Length + precision : 0);

        var sign = IsNegative ? info.NegativeSign : string.Empty;
        var expSign = exponent < 0 ? info.NegativeSign : info.PositiveSign;
        Span<char> expDigits = stackalloc char[8];
        var expLen = 0;
        var absExponent = Math.Abs(exponent);
        do
        {
            expDigits[expLen++] = (char)('0' + (absExponent % 10));
            absExponent /= 10;
        }
        while (absExponent > 0);

        while (expLen < 3)
        {
            expDigits[expLen++] = '0';
        }

        var total = sign.Length + mantissaLen + 1 + expSign.Length + expLen;
        if (destination.Length < total)
        {
            charsWritten = 0;
            return false;
        }

        var pos = 0;
        sign.CopyTo(destination[pos..]);
        pos += sign.Length;
        destination[pos++] = digits[0];
        if (precision > 0)
        {
            point.CopyTo(destination[pos..]);
            pos += point.Length;

            var copied = Math.Min(precision, digitCount - 1);
            if (copied > 0)
            {
                digits.Slice(1, copied).CopyTo(destination[pos..]);
                pos += copied;
            }

            var padding = precision - Math.Max(copied, 0);
            destination.Slice(pos, padding).Fill('0');
            pos += padding;
        }

        destination[pos++] = lowercase ? 'e' : 'E';
        expSign.CopyTo(destination[pos..]);
        pos += expSign.Length;
        for (var i = expLen - 1; i >= 0; i--)
        {
            destination[pos++] = expDigits[i];
        }

        charsWritten = pos;
        return true;
    }

    private static int RoundSignificand(Span<char> digits, int digitCount, int keep, out bool carried)
    {
        carried = false;
        if (keep <= 0 || digitCount <= keep)
        {
            return digitCount;
        }

        var roundUp = digits[keep] >= '5';
        digitCount = keep;
        if (!roundUp)
        {
            return digitCount;
        }

        for (var i = keep - 1; i >= 0; i--)
        {
            if (digits[i] != '9')
            {
                digits[i]++;
                return digitCount;
            }

            digits[i] = '0';
        }

        digits[0] = '1';
        carried = true;
        return digitCount;
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
            len = Words.DivRemSmall(magnitude, len, Words.TenPow19, out var chunk);
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

    private static bool TryParsePrecision(ReadOnlySpan<char> format, out int? precision)
    {
        precision = null;
        if (format.Length <= 1)
        {
            return true;
        }

        if (format.Length > 10)
        {
            return false;
        }

        var value = 0;
        for (var i = 1; i < format.Length; i++)
        {
            if (!char.IsAsciiDigit(format[i]))
            {
                return false;
            }

            value = (value * 10) + (format[i] - '0');
        }

        precision = value;
        return true;
    }

    [DoesNotReturn]
    private static void ThrowUnsupportedFormat(ReadOnlySpan<char> format) =>
        throw new FormatException(
            $"The format string '{format}' is not supported by BigDecimal. Use G, F, N or E with an "
            + "optional precision; custom numeric format strings are not implemented.");

    private string FormatViaBuilder(string? format, IFormatProvider? provider)
    {
        if (!TryParsePrecision(format, out var precision))
        {
            ThrowUnsupportedFormat(format);
        }

        var size = MaxCharsPlain + 32 + ((precision ?? 0) * 2);
        if (size > MaxFormatChars)
        {
            ThrowUnsupportedFormat(format);
        }

        var buffer = new char[size];
        return TryFormat(buffer, out var written, format, provider)
            ? new string(buffer, 0, written)
            : throw new FormatException($"The format string '{format}' is not supported.");
    }
}
