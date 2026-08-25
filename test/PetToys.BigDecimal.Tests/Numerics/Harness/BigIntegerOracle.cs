using System;
using System.Numerics;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Computes what each operation is required to produce, using nothing but
/// <see cref="BigInteger"/> arithmetic and the package's documented scale and rounding rules.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here consults <see cref="BigDecimal"/> or its internals. An oracle written by reading
/// the code under test agrees with it by construction, including where it is wrong, which is worth
/// nothing. When this disagrees with the implementation, the documented rule decides which one moves
/// — and if the specification is silent, it gets written before either does.
/// </para>
/// <para>
/// Every reduction rounds once, from the full exact input. Rounding to an intermediate scale first
/// and then again can land a unit in the last place away from the correct answer, and the harness
/// would report its own error as the implementation's.
/// </para>
/// </remarks>
public static class BigIntegerOracle
{
    /// <summary>The widest scale the type accepts.</summary>
    public const int MaxScale = 255;

    /// <summary>
    /// The number of significant digits a result that does not fit the mantissa is normalised to.
    /// Every 77-digit value is representable; the 78-digit ones only sometimes are, so a result
    /// that has to be reduced is reduced into the band that always fits.
    /// </summary>
    public const int MaxSignificantDigits = 77;

    /// <summary>The largest magnitude the 256-bit mantissa holds.</summary>
    public static BigInteger MaxMagnitude { get; } = (BigInteger.One << 256) - BigInteger.One;

    /// <summary>Counts the 64-bit words a value occupies.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The word count, zero for zero.</returns>
    public static int WordCount(BigInteger value) => (int)((BigInteger.Abs(value).GetBitLength() + 63) / 64);

    /// <summary>Adds two values, at the wider of their scales.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The required result.</returns>
    public static OracleValue Add(OracleValue left, OracleValue right)
    {
        var scale = Math.Max(left.Scale, right.Scale);

        return Fit(AlignTo(left, scale) + AlignTo(right, scale), scale);
    }

    /// <summary>Subtracts one value from another, at the wider of their scales.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The required result.</returns>
    public static OracleValue Subtract(OracleValue left, OracleValue right)
    {
        var scale = Math.Max(left.Scale, right.Scale);

        return Fit(AlignTo(left, scale) - AlignTo(right, scale), scale);
    }

    /// <summary>Multiplies two values, at the sum of their scales capped at <see cref="MaxScale"/>.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The required result.</returns>
    public static OracleValue Multiply(OracleValue left, OracleValue right) =>
        Fit(left.Unscaled * right.Unscaled, left.Scale + right.Scale);

    /// <summary>
    /// Takes the remainder of a division. When the division does not happen — the dividend is zero,
    /// or smaller in magnitude than the divisor — the dividend comes back untouched, at its own
    /// scale rather than at the wider of the two.
    /// </summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The required result, whose sign follows the dividend.</returns>
    /// <exception cref="DivideByZeroException">The divisor is zero.</exception>
    public static OracleValue Remainder(OracleValue left, OracleValue right)
    {
        if (right.Unscaled.IsZero)
        {
            throw new DivideByZeroException();
        }

        if (left.Unscaled.IsZero)
        {
            return left;
        }

        var scale = Math.Max(left.Scale, right.Scale);
        var dividend = AlignTo(left, scale);
        var divisor = AlignTo(right, scale);

        return BigInteger.Abs(dividend) < BigInteger.Abs(divisor)
            ? left
            : Fit(BigInteger.Remainder(dividend, divisor), scale);
    }

    /// <summary>Divides one value by another at an explicitly requested scale.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <param name="scale">The scale of the result.</param>
    /// <param name="mode">The rounding applied to the digits beyond <paramref name="scale"/>.</param>
    /// <returns>The required result.</returns>
    /// <exception cref="DivideByZeroException">The divisor is zero.</exception>
    public static OracleValue Divide(OracleValue left, OracleValue right, int scale, MidpointRounding mode)
    {
        if (right.Unscaled.IsZero)
        {
            throw new DivideByZeroException();
        }

        var (numerator, denominator) = ExactQuotient(left, right, scale);
        var sign = numerator.Sign * denominator.Sign;

        return Fit(
            sign * DivideRound(BigInteger.Abs(numerator), BigInteger.Abs(denominator), sign, mode),
            scale);
    }

    /// <summary>Rounds a value to a narrower scale. A scale at least as wide leaves it untouched.</summary>
    /// <param name="value">The value to round.</param>
    /// <param name="scale">The scale to round to.</param>
    /// <param name="mode">The rounding to apply.</param>
    /// <returns>The required result.</returns>
    public static OracleValue Round(OracleValue value, int scale, MidpointRounding mode) =>
        scale >= value.Scale ? value : Reduce(value, scale, mode);

    /// <summary>Presents a value at a requested scale, widening exactly or narrowing by rounding.</summary>
    /// <param name="value">The value to present.</param>
    /// <param name="scale">The requested scale.</param>
    /// <param name="mode">The rounding applied when narrowing.</param>
    /// <returns>The required result.</returns>
    /// <exception cref="OverflowException">Widening would push significant digits out of the mantissa.</exception>
    public static OracleValue WithScale(OracleValue value, int scale, MidpointRounding mode)
    {
        if (scale == value.Scale)
        {
            return value;
        }

        if (scale < value.Scale)
        {
            return Reduce(value, scale, mode);
        }

        var widened = value.Unscaled * Pow10(scale - value.Scale);

        // Widening is exact by definition, so a scale the mantissa cannot hold is refused rather
        // than quietly rounded back to one that fits.
        return BigInteger.Abs(widened) > MaxMagnitude
            ? throw new OverflowException()
            : new OracleValue(widened, scale);
    }

    /// <summary>Orders two values numerically, ignoring the scales they carry.</summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>A negative number, zero, or a positive number.</returns>
    public static int Compare(OracleValue left, OracleValue right)
    {
        var scale = Math.Max(left.Scale, right.Scale);

        return AlignTo(left, scale).CompareTo(AlignTo(right, scale));
    }

    /// <summary>
    /// The exact quotient as a fraction, with the requested scale already folded in: the value the
    /// result claims is <c>Numerator / Denominator</c> divided by ten to the power of the scale.
    /// </summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <param name="scale">The scale the quotient is to carry.</param>
    /// <returns>The numerator and denominator of the exact unscaled quotient.</returns>
    public static (BigInteger Numerator, BigInteger Denominator) ExactQuotient(
        OracleValue left,
        OracleValue right,
        int scale)
    {
        // left/right = (L * 10^rs) / (R * 10^ls); the unscaled result at scale S multiplies that
        // by 10^S. Both powers are folded into whichever side keeps them non-negative.
        var shift = right.Scale + scale - left.Scale;

        return shift >= 0
            ? (left.Unscaled * Pow10(shift), right.Unscaled)
            : (left.Unscaled, right.Unscaled * Pow10(-shift));
    }

    /// <summary>Divides two magnitudes and rounds the result in one step.</summary>
    /// <param name="numerator">The numerator, not negative.</param>
    /// <param name="denominator">The denominator, greater than zero.</param>
    /// <param name="sign">The sign the result will carry, which the directed modes depend on.</param>
    /// <param name="mode">The rounding to apply.</param>
    /// <returns>The rounded magnitude.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not a defined value.</exception>
    public static BigInteger DivideRound(
        BigInteger numerator,
        BigInteger denominator,
        int sign,
        MidpointRounding mode)
    {
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (remainder.IsZero)
        {
            return quotient;
        }

        var doubled = remainder * 2;
        var roundAway = mode switch
        {
            MidpointRounding.ToEven => doubled > denominator || (doubled == denominator && !quotient.IsEven),
            MidpointRounding.AwayFromZero => doubled >= denominator,
            MidpointRounding.ToZero => false,
            MidpointRounding.ToNegativeInfinity => sign < 0,
            MidpointRounding.ToPositiveInfinity => sign > 0,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        return roundAway ? quotient + BigInteger.One : quotient;
    }

    /// <summary>
    /// Applies the magnitude-bound rule: an unscaled value that does not fit the mantissa is
    /// normalised to <see cref="MaxSignificantDigits"/> significant digits, a scale beyond
    /// <see cref="MaxScale"/> is brought back to it, the digits given up are rounded half to even,
    /// and the result overflows only when there are no fractional digits left to give up.
    /// </summary>
    /// <param name="unscaled">The exact unscaled result.</param>
    /// <param name="scale">The scale it was computed at.</param>
    /// <returns>The representable result.</returns>
    /// <exception cref="OverflowException">The integer part does not fit the mantissa.</exception>
    public static OracleValue Fit(BigInteger unscaled, int scale)
    {
        var sign = unscaled.Sign;
        var magnitude = BigInteger.Abs(unscaled);

        while (true)
        {
            // Rounding can carry — 999 becomes 1000 — so the fit is rechecked rather than assumed
            // after one reduction.
            if (magnitude <= MaxMagnitude && scale <= MaxScale)
            {
                return new OracleValue(sign < 0 ? -magnitude : magnitude, scale);
            }

            if (scale <= 0)
            {
                throw new OverflowException();
            }

            var wanted = Math.Max(scale - MaxScale, 0);
            if (magnitude > MaxMagnitude)
            {
                // Not the smallest reduction that would fit: a value that does not fit is taken
                // down to the band where every value of that width fits, which costs one further
                // digit when the exact result happens to land just under the mantissa's ceiling.
                wanted = Math.Max(wanted, DigitCount(magnitude) - MaxSignificantDigits);
            }

            var drop = Math.Clamp(wanted <= 0 ? 1 : wanted, 1, scale);
            magnitude = DivideRound(magnitude, Pow10(drop), sign, MidpointRounding.ToEven);
            scale -= drop;
        }
    }

    /// <summary>Ten to a power, as an exact integer.</summary>
    /// <param name="exponent">The exponent, not negative.</param>
    /// <returns>Ten raised to <paramref name="exponent"/>.</returns>
    public static BigInteger Pow10(int exponent) => BigInteger.Pow(10, exponent);

    /// <summary>Counts the decimal digits of a magnitude, with zero counting as one digit.</summary>
    /// <param name="magnitude">The magnitude, not negative.</param>
    /// <returns>The digit count.</returns>
    public static int DigitCount(BigInteger magnitude)
    {
        if (magnitude.IsZero)
        {
            return 1;
        }

        var digits = 1;
        for (var bound = BigInteger.Abs(magnitude) / 10; !bound.IsZero; bound /= 10)
        {
            digits++;
        }

        return digits;
    }

    private static OracleValue Reduce(OracleValue value, int scale, MidpointRounding mode)
    {
        var magnitude = BigInteger.Abs(value.Unscaled);
        var sign = value.Unscaled.Sign;
        var reduced = DivideRound(magnitude, Pow10(value.Scale - scale), sign, mode);

        return Fit(sign < 0 ? -reduced : reduced, scale);
    }

    private static BigInteger AlignTo(OracleValue value, int scale) =>
        value.Unscaled * Pow10(scale - value.Scale);
}
