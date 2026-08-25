using System;
using System.Globalization;
using System.Text;

namespace PetToys.BigDecimal.Numerics;

public readonly partial struct BigDecimal : IParsable<BigDecimal>, ISpanParsable<BigDecimal>, IUtf8SpanParsable<BigDecimal>
{
    private const NumberStyles DefaultStyles = NumberStyles.Number | NumberStyles.AllowExponent;

    /// <summary>Parses a decimal number for the current culture.</summary>
    /// <returns>The parsed value, with the scale the text implies.</returns>
    /// <exception cref="FormatException">The text is not a number this type accepts.</exception>
    /// <exception cref="OverflowException">The integer part does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Parse(string s) => Parse(s, DefaultStyles, CultureInfo.CurrentCulture);

    /// <summary>Parses a decimal number for the given culture.</summary>
    /// <returns>The parsed value, with the scale the text implies.</returns>
    /// <exception cref="FormatException">The text is not a number this type accepts.</exception>
    /// <exception cref="OverflowException">The integer part does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Parse(string s, IFormatProvider? provider) => Parse(s, DefaultStyles, provider);

    /// <summary>Parses a decimal number with the given styles and culture.</summary>
    /// <remarks>
    /// Fractional digits beyond what the magnitude can hold are rounded to nearest with ties to
    /// even; only an integer part that does not fit is rejected.
    /// </remarks>
    /// <returns>The parsed value, with the scale the text implies.</returns>
    /// <exception cref="FormatException">The text is not a number this type accepts.</exception>
    /// <exception cref="OverflowException">The integer part does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Parse(string s, NumberStyles style, IFormatProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), style, provider);
    }

    /// <summary>Parses a decimal number from characters, for the given culture.</summary>
    /// <returns>The parsed value, with the scale the text implies.</returns>
    /// <exception cref="FormatException">The text is not a number this type accepts.</exception>
    /// <exception cref="OverflowException">The integer part does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s, DefaultStyles, provider);

    /// <summary>Parses a decimal number from characters, with the given styles and culture.</summary>
    /// <returns>The parsed value, with the scale the text implies.</returns>
    /// <exception cref="FormatException">The text is not a number this type accepts.</exception>
    /// <exception cref="OverflowException">The integer part does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Parse(ReadOnlySpan<char> s, NumberStyles style = DefaultStyles, IFormatProvider? provider = null)
    {
        var status = TryParseCore(s, style, provider, out var result);
        return status switch
        {
            ParseStatus.Ok => result,
            ParseStatus.Overflow => throw new OverflowException("Value was either too large or too small for a BigDecimal."),
            _ => throw new FormatException("The input string was not in a correct format."),
        };
    }

    /// <summary>Parses a decimal number from UTF-8 bytes, for the given culture.</summary>
    /// <returns>The parsed value, with the scale the text implies.</returns>
    /// <exception cref="FormatException">The text is not a number this type accepts.</exception>
    /// <exception cref="OverflowException">The integer part does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) =>
        Parse(utf8Text, DefaultStyles, provider);

    /// <summary>Parses a decimal number from UTF-8 bytes, with the given styles and culture.</summary>
    /// <returns>The parsed value, with the scale the text implies.</returns>
    /// <exception cref="FormatException">The text is not a number this type accepts.</exception>
    /// <exception cref="OverflowException">The integer part does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Parse(ReadOnlySpan<byte> utf8Text, NumberStyles style = DefaultStyles, IFormatProvider? provider = null)
    {
        Span<char> chars = stackalloc char[MaxCharsPlain + 32];
        if (TryTranscode(utf8Text, chars, out var written))
        {
            return Parse(chars[..written], style, provider);
        }

        return Parse(Encoding.UTF8.GetString(utf8Text).AsSpan(), style, provider);
    }

    /// <summary>Tries to parse a decimal number for the current culture.</summary>
    /// <returns><see langword="true"/> when the text was parsed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? s, out BigDecimal result) =>
        TryParse(s, DefaultStyles, CultureInfo.CurrentCulture, out result);

    /// <summary>Tries to parse a decimal number for the given culture.</summary>
    /// <returns><see langword="true"/> when the text was parsed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? s, IFormatProvider? provider, out BigDecimal result) =>
        TryParse(s, DefaultStyles, provider, out result);

    /// <summary>Tries to parse a decimal number with the given styles and culture.</summary>
    /// <returns><see langword="true"/> when the text was parsed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out BigDecimal result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), style, provider, out result);
    }

    /// <summary>Tries to parse a decimal number from characters, for the current culture.</summary>
    /// <returns><see langword="true"/> when the text was parsed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out BigDecimal result) =>
        TryParse(s, DefaultStyles, CultureInfo.CurrentCulture, out result);

    /// <summary>Tries to parse a decimal number from characters, for the given culture.</summary>
    /// <returns><see langword="true"/> when the text was parsed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out BigDecimal result) =>
        TryParse(s, DefaultStyles, provider, out result);

    /// <summary>Tries to parse a decimal number from characters, with the given styles and culture.</summary>
    /// <returns><see langword="true"/> when the text was parsed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out BigDecimal result) =>
        TryParseCore(s, style, provider, out result) == ParseStatus.Ok;

    /// <summary>Tries to parse a decimal number from UTF-8 bytes, for the current culture.</summary>
    /// <returns><see langword="true"/> when the text was parsed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, out BigDecimal result) =>
        TryParse(utf8Text, DefaultStyles, CultureInfo.CurrentCulture, out result);

    /// <summary>Tries to parse a decimal number from UTF-8 bytes, for the given culture.</summary>
    /// <returns><see langword="true"/> when the text was parsed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out BigDecimal result) =>
        TryParse(utf8Text, DefaultStyles, provider, out result);

    /// <summary>Tries to parse a decimal number from UTF-8 bytes, with the given styles and culture.</summary>
    /// <returns><see langword="true"/> when the text was parsed; otherwise <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, NumberStyles style, IFormatProvider? provider, out BigDecimal result)
    {
        Span<char> chars = stackalloc char[MaxCharsPlain + 32];
        if (TryTranscode(utf8Text, chars, out var written))
        {
            return TryParse(chars[..written], style, provider, out result);
        }

        return TryParse(Encoding.UTF8.GetString(utf8Text).AsSpan(), style, provider, out result);
    }

    private static bool TryTranscode(ReadOnlySpan<byte> utf8Text, Span<char> destination, out int written)
    {
        if (utf8Text.Length > destination.Length)
        {
            written = 0;
            return false;
        }

        return Encoding.UTF8.TryGetChars(utf8Text, destination, out written);
    }

    private enum ParseStatus
    {
        Ok,
        Format,
        Overflow,
    }

    private static ParseStatus TryParseCore(
        ReadOnlySpan<char> input,
        NumberStyles style,
        IFormatProvider? provider,
        out BigDecimal result)
    {
        result = default;
        var info = NumberFormatInfo.GetInstance(provider);

        if ((style & NumberStyles.AllowLeadingWhite) != 0)
        {
            input = input.TrimStart();
        }

        if ((style & NumberStyles.AllowTrailingWhite) != 0)
        {
            input = input.TrimEnd();
        }

        if (input.IsEmpty)
        {
            return ParseStatus.Format;
        }

        var negative = false;
        if ((style & NumberStyles.AllowLeadingSign) != 0)
        {
            if (TryConsume(ref input, info.NegativeSign))
            {
                negative = true;
            }
            else
            {
                TryConsume(ref input, info.PositiveSign);
            }
        }

        if ((style & NumberStyles.AllowTrailingSign) != 0 && !negative && TryConsumeTrailing(ref input, info.NegativeSign))
        {
            negative = true;
        }

        if (input.IsEmpty)
        {
            return ParseStatus.Format;
        }

        Span<ulong> magnitude = stackalloc ulong[WorkWords];
        magnitude.Clear();
        var length = 0;
        var scale = 0;
        var seenDigit = false;
        var seenPoint = false;
        var truncated = false;
        var sticky = false;
        var exponent = 0;

        var decimalSeparator = info.NumberDecimalSeparator;
        var groupSeparator = info.NumberGroupSeparator;
        var allowThousands = (style & NumberStyles.AllowThousands) != 0;
        var allowDecimalPoint = (style & NumberStyles.AllowDecimalPoint) != 0;
        var allowExponent = (style & NumberStyles.AllowExponent) != 0;

        ulong chunk = 0;
        var chunkDigits = 0;

        while (!input.IsEmpty)
        {
            var c = input[0];
            if (char.IsAsciiDigit(c))
            {
                seenDigit = true;
                if (length == 0 && chunkDigits == 0 && c == '0')
                {
                    if (seenPoint)
                    {
                        scale++;
                    }

                    input = input[1..];
                    continue;
                }

                if (chunkDigits + (length * 19) < MaxDigits + 20)
                {
                    chunk = (chunk * 10) + (ulong)(c - '0');
                    chunkDigits++;
                    if (chunkDigits == 19)
                    {
                        length = Words.MulAddSmall(magnitude, length, Words.TenPow19, chunk);
                        chunk = 0;
                        chunkDigits = 0;
                    }

                    if (seenPoint)
                    {
                        scale++;
                    }
                }
                else
                {
                    truncated = true;
                    if (seenPoint)
                    {
                        sticky |= c != '0';
                    }
                    else
                    {
                        exponent++;
                    }
                }

                input = input[1..];
                continue;
            }

            if (allowDecimalPoint && !seenPoint && StartsWith(input, decimalSeparator))
            {
                seenPoint = true;
                input = input[decimalSeparator.Length..];
                continue;
            }

            if (allowThousands && !seenPoint && StartsWith(input, groupSeparator))
            {
                input = input[groupSeparator.Length..];
                continue;
            }

            if (allowExponent && (c is 'e' or 'E'))
            {
                input = input[1..];
                if (!TryParseExponent(input, info, out var parsed))
                {
                    return ParseStatus.Format;
                }

                exponent += parsed;
                input = default;
                break;
            }

            return ParseStatus.Format;
        }

        if (!seenDigit)
        {
            return ParseStatus.Format;
        }

        if (chunkDigits > 0)
        {
            length = Words.MulAddSmall(magnitude, length, Words.Pow10[chunkDigits], chunk);
        }

        if (sticky)
        {
            length = Words.MulAddSmall(magnitude, length, 10, 1);
            scale++;
        }

        scale -= exponent;

        if (truncated)
        {
            if (scale < 0)
            {
                return ParseStatus.Overflow;
            }
        }

        try
        {
            result = Pack(magnitude, length, negative, scale);
            return ParseStatus.Ok;
        }
        catch (OverflowException)
        {
            return ParseStatus.Overflow;
        }
    }

    private static bool TryParseExponent(ReadOnlySpan<char> input, NumberFormatInfo info, out int exponent)
    {
        exponent = 0;
        if (input.IsEmpty)
        {
            return false;
        }

        var negative = false;
        if (TryConsume(ref input, info.NegativeSign))
        {
            negative = true;
        }
        else
        {
            TryConsume(ref input, info.PositiveSign);
        }

        if (input.IsEmpty)
        {
            return false;
        }

        long value = 0;
        foreach (var c in input)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            value = (value * 10) + (c - '0');
            if (value > 100_000)
            {
                value = 100_000;
            }
        }

        exponent = (int)(negative ? -value : value);
        return true;
    }

    private static bool StartsWith(ReadOnlySpan<char> input, string value) =>
        value.Length > 0 && input.StartsWith(value, StringComparison.Ordinal);

    private static bool TryConsume(ref ReadOnlySpan<char> input, string token)
    {
        if (!StartsWith(input, token))
        {
            return false;
        }

        input = input[token.Length..];
        return true;
    }

    private static bool TryConsumeTrailing(ref ReadOnlySpan<char> input, string token)
    {
        if (token.Length == 0 || !input.EndsWith(token, StringComparison.Ordinal))
        {
            return false;
        }

        input = input[..^token.Length];
        return true;
    }
}
