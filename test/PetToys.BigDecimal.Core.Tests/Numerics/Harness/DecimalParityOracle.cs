using System;
using System.Globalization;
using System.Numerics;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The second oracle: <see cref="decimal"/> itself, used only inside its own domain and only where
/// the specified behaviour is parity with it.
/// </summary>
/// <remarks>
/// <see cref="decimal"/> holds 28 or 29 significant digits, which is the domain this package exists
/// to escape, so it can never be the primary reference. It is authoritative about exactly the
/// things parity is claimed for — the five rounding modes, how lenient parsing is about group
/// separators, and culture-sensitive output — and where both oracles apply they have to agree.
/// </remarks>
public static class DecimalParityOracle
{
    /// <summary>The widest scale a <see cref="decimal"/> carries.</summary>
    public const int MaxScale = 28;

    /// <summary>The largest magnitude a <see cref="decimal"/> holds, 2^96 - 1.</summary>
    public static BigInteger MaxMagnitude { get; } = (BigInteger.One << 96) - BigInteger.One;

    /// <summary>Converts a value to <see cref="decimal"/> when it lies inside that type's domain.</summary>
    /// <param name="value">The value to convert.</param>
    /// <param name="result">The converted value, or zero.</param>
    /// <returns><see langword="true"/> when the value fits without loss.</returns>
    public static bool TryToDecimal(OracleValue value, out decimal result)
    {
        result = 0m;
        if (value.Scale > MaxScale || BigInteger.Abs(value.Unscaled) > MaxMagnitude)
        {
            return false;
        }

        var magnitude = BigInteger.Abs(value.Unscaled);

        // The decimal constructor takes the three words as signed integers, so the top bit of each
        // has to be reinterpreted rather than converted. Debug builds check conversions.
        unchecked
        {
            var low = (int)(uint)(magnitude & uint.MaxValue);
            var middle = (int)(uint)((magnitude >> 32) & uint.MaxValue);
            var high = (int)(uint)((magnitude >> 64) & uint.MaxValue);

            result = new decimal(low, middle, high, value.Unscaled.Sign < 0, (byte)value.Scale);
        }

        return true;
    }

    /// <summary>Describes a <see cref="decimal"/> the way the oracle carries values.</summary>
    /// <param name="value">The value to describe.</param>
    /// <returns>Its signed mantissa and scale.</returns>
    public static OracleValue From(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        var written = decimal.GetBits(value, bits);
        var magnitude = BigInteger.Zero;
        var flags = 0u;

        unchecked
        {
            magnitude = new BigInteger((uint)bits[0])
                | (new BigInteger((uint)bits[1]) << 32)
                | (new BigInteger((uint)bits[2]) << 64);
            flags = (uint)bits[written - 1];
        }

        var scale = (int)((flags >> 16) & 0xFF);
        var negative = (flags & 0x8000_0000u) != 0;

        return new OracleValue(negative ? -magnitude : magnitude, scale);
    }

    /// <summary>Formats a value the way <see cref="decimal"/> would, for parity comparisons.</summary>
    /// <param name="value">The value, which must lie inside <see cref="decimal"/>'s domain.</param>
    /// <param name="format">The format specifier.</param>
    /// <param name="provider">The culture to format under.</param>
    /// <returns>The formatted text.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside the domain.</exception>
    public static string Format(OracleValue value, string? format, IFormatProvider? provider) =>
        TryToDecimal(value, out var converted)
            ? converted.ToString(format, provider)
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Outside decimal's domain.");

    /// <summary>Rounds inside <see cref="decimal"/>'s domain, for parity comparisons.</summary>
    /// <param name="value">The value, which must lie inside <see cref="decimal"/>'s domain.</param>
    /// <param name="scale">The scale to round to, from 0 to <see cref="MaxScale"/>.</param>
    /// <param name="mode">The rounding to apply.</param>
    /// <returns>The rounded value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside the domain.</exception>
    public static OracleValue Round(OracleValue value, int scale, MidpointRounding mode) =>
        TryToDecimal(value, out var converted)
            ? From(decimal.Round(converted, scale, mode))
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Outside decimal's domain.");

    /// <summary>Parses text the way <see cref="decimal"/> does, for parity comparisons.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="styles">The styles to allow.</param>
    /// <param name="provider">The culture to parse under.</param>
    /// <param name="result">The parsed value.</param>
    /// <returns><see langword="true"/> when <see cref="decimal"/> accepts the text.</returns>
    public static bool TryParse(string text, NumberStyles styles, IFormatProvider? provider, out OracleValue result)
    {
        if (decimal.TryParse(text, styles, provider, out var parsed))
        {
            result = From(parsed);

            return true;
        }

        result = default;

        return false;
    }
}
