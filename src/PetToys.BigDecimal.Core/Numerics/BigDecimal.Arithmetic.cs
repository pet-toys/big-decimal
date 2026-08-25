using System;

namespace PetToys.BigDecimal.Numerics;

public readonly partial struct BigDecimal
{
    internal const int WorkWords = 24;

    private const int DivideWorkWords = 32;

    /// <summary>Adds two values.</summary>
    /// <remarks>
    /// The result carries the wider of the two scales. An integer part that does not fit throws
    /// <see cref="OverflowException"/>; fractional digits that do not fit are rounded to nearest,
    /// ties to even.
    /// </remarks>
    public static BigDecimal operator +(BigDecimal left, BigDecimal right) => Add(left, right);

    /// <summary>Subtracts one value from another.</summary>
    /// <remarks>
    /// The result carries the wider of the two scales. An integer part that does not fit throws
    /// <see cref="OverflowException"/>; fractional digits that do not fit are rounded to nearest,
    /// ties to even.
    /// </remarks>
    public static BigDecimal operator -(BigDecimal left, BigDecimal right) => Subtract(left, right);

    /// <summary>Multiplies two values.</summary>
    public static BigDecimal operator *(BigDecimal left, BigDecimal right) => Multiply(left, right);

    /// <summary>Divides one value by another.</summary>
    public static BigDecimal operator /(BigDecimal left, BigDecimal right) => Divide(left, right);

    /// <summary>Returns the remainder of dividing one value by another.</summary>
    public static BigDecimal operator %(BigDecimal left, BigDecimal right) => Remainder(left, right);

    /// <summary>Returns the value unchanged.</summary>
    public static BigDecimal operator +(BigDecimal value) => value;

    /// <summary>Returns the negation of the value.</summary>
    public static BigDecimal operator -(BigDecimal value) => Negate(value);

    /// <summary>Adds one to the value.</summary>
    public static BigDecimal operator ++(BigDecimal value) => value + One;

    /// <summary>Subtracts one from the value.</summary>
    public static BigDecimal operator --(BigDecimal value) => value - One;

    /// <summary>Adds two values, keeping the wider of their scales.</summary>
    /// <param name="left">The first addend.</param>
    /// <param name="right">The second addend.</param>
    /// <returns>The sum.</returns>
    /// <exception cref="OverflowException">The integer part of the sum does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Add(BigDecimal left, BigDecimal right) => AddCore(left, right, right.IsNegative);

    /// <summary>Subtracts one value from another, keeping the wider of their scales.</summary>
    /// <param name="left">The minuend.</param>
    /// <param name="right">The subtrahend.</param>
    /// <returns>The difference.</returns>
    /// <exception cref="OverflowException">The integer part of the difference does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Subtract(BigDecimal left, BigDecimal right) => AddCore(left, right, !right.IsNegative);

    /// <summary>Returns the negation of a value. Zero is returned unsigned.</summary>
    /// <param name="value">The value to negate.</param>
    /// <returns>The negated value, at the same scale.</returns>
    public static BigDecimal Negate(BigDecimal value) =>
        new(value.RawL0, value.RawL1, value.RawL2, value.RawL3, !value.IsNegative, value.Scale);

    /// <summary>Returns the magnitude of a value, dropping its sign.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The absolute value, at the same scale.</returns>
    public static BigDecimal Abs(BigDecimal value) =>
        new(value.RawL0, value.RawL1, value.RawL2, value.RawL3, false, value.Scale);

    internal ulong RawL0 => _l0;

    internal ulong RawL1 => _l1;

    internal ulong RawL2 => _l2;

    internal ulong RawL3 => _l3;

    private static BigDecimal AddCore(BigDecimal left, BigDecimal right, bool rightNegative)
    {
        Span<ulong> a = stackalloc ulong[WorkWords];
        Span<ulong> b = stackalloc ulong[WorkWords];
        a.Clear();
        b.Clear();

        var aLen = left.CopyMagnitude(a);
        var bLen = right.CopyMagnitude(b);
        var scale = AlignScales(a, ref aLen, left.Scale, b, ref bLen, right.Scale);

        var leftNegative = left.IsNegative;
        if (leftNegative == rightNegative)
        {
            aLen = Words.AddInto(a, aLen, b, bLen);
            return Pack(a, aLen, leftNegative, scale);
        }

        var cmp = Words.Compare(a, aLen, b, bLen);
        if (cmp == 0)
        {
            Span<ulong> zero = stackalloc ulong[WordCount];
            zero.Clear();
            return Pack(zero, 0, false, scale);
        }

        if (cmp > 0)
        {
            aLen = Words.SubInto(a, aLen, b, bLen);
            return Pack(a, aLen, leftNegative, scale);
        }

        bLen = Words.SubInto(b, bLen, a, aLen);
        return Pack(b, bLen, rightNegative, scale);
    }

    private static int AlignScales(
        Span<ulong> a,
        ref int aLen,
        int aScale,
        Span<ulong> b,
        ref int bLen,
        int bScale)
    {
        if (aScale == bScale)
        {
            return aScale;
        }

        var target = Math.Max(aScale, bScale);
        if (aScale < target && aLen > 0)
        {
            aLen = Words.ScaleUp(a, aLen, target - aScale);
        }
        else if (bScale < target && bLen > 0)
        {
            bLen = Words.ScaleUp(b, bLen, target - bScale);
        }

        return target;
    }

    /// <summary>Multiplies two values.</summary>
    /// <remarks>
    /// The product's scale is the sum of the operands' scales, reduced by rounding when that sum
    /// exceeds <see cref="MaxScale"/> or when the significant digits do not fit the magnitude. Two
    /// values small enough that their exact product falls below the floor of the range therefore
    /// multiply to zero.
    /// </remarks>
    /// <param name="left">The first factor.</param>
    /// <param name="right">The second factor.</param>
    /// <returns>The product.</returns>
    /// <exception cref="OverflowException">The integer part of the product does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Multiply(BigDecimal left, BigDecimal right)
    {
        Span<ulong> a = stackalloc ulong[WordCount];
        Span<ulong> b = stackalloc ulong[WordCount];
        var aLen = left.CopyMagnitude(a);
        var bLen = right.CopyMagnitude(b);
        var negative = left.IsNegative ^ right.IsNegative;

        if (aLen == 0 || bLen == 0)
        {
            Span<ulong> zero = stackalloc ulong[WordCount];
            zero.Clear();
            return Pack(zero, 0, false, Math.Min(left.Scale + right.Scale, MaxScale));
        }

        Span<ulong> product = stackalloc ulong[(WordCount * 2) + 1];
        var len = Words.Mul(a, aLen, b, bLen, product);
        return Pack(product, len, negative, left.Scale + right.Scale);
    }

    /// <summary>Divides one value by another.</summary>
    /// <remarks>
    /// The quotient is produced to the full precision the magnitude allows and rounded to nearest,
    /// ties to even. A quotient that divides exactly is reduced to its shortest scale, but never
    /// below the difference of the operands' scales — or below zero, when that difference is
    /// negative — which mirrors <see cref="decimal"/>.
    /// </remarks>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The quotient.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is zero.</exception>
    /// <exception cref="OverflowException">The integer part of the quotient does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Divide(BigDecimal left, BigDecimal right)
    {
        if (right.IsZero)
        {
            throw new DivideByZeroException();
        }

        var negative = left.IsNegative ^ right.IsNegative;
        if (left.IsZero)
        {
            Span<ulong> zeroBuffer = stackalloc ulong[WordCount];
            zeroBuffer.Clear();
            return Pack(zeroBuffer, 0, false, Math.Clamp(left.Scale - right.Scale, 0, MaxScale));
        }

        Span<ulong> num = stackalloc ulong[WorkWords];
        Span<ulong> den = stackalloc ulong[WordCount];
        num.Clear();
        den.Clear();
        var numLen = left.CopyMagnitude(num);
        var denLen = right.CopyMagnitude(den);

        var numDigits = Words.DecimalDigitCount(num, numLen);
        var denDigits = Words.DecimalDigitCount(den, denLen);
        var scale = left.Scale - right.Scale;
        var lift = MaxDigits - 1 - numDigits + denDigits;
        lift = Math.Max(lift, -scale);
        lift = Math.Min(lift, MaxScale - scale);
        lift = Math.Max(lift, 0);

        if (lift > 0)
        {
            numLen = Words.ScaleUp(num, numLen, lift);
        }

        scale += lift;

        Span<ulong> quotient = stackalloc ulong[WorkWords];
        quotient.Clear();
        var qLen = Words.DivRem(num, numLen, den, denLen, quotient, out var remLen);

        if (remLen == 0)
        {
            qLen = StripTrailingZeros(quotient, qLen, ref scale, Math.Max(left.Scale - right.Scale, 0));
            return Pack(quotient, qLen, negative, scale);
        }

        qLen = RoundByRemainder(quotient, qLen, num, remLen, den, denLen, negative, MidpointRounding.ToEven);
        return Pack(quotient, qLen, negative, scale);
    }

    /// <summary>Divides one value by another and rounds the quotient to a requested scale.</summary>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <param name="scale">The scale of the result, from 0 to <see cref="MaxScale"/> inclusive.</param>
    /// <param name="mode">The rounding mode applied to the digits beyond <paramref name="scale"/>.</param>
    /// <returns>The quotient at the requested scale.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is outside 0 to <see cref="MaxScale"/>, or <paramref name="mode"/> is not a defined <see cref="MidpointRounding"/> value.</exception>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is zero.</exception>
    /// <exception cref="OverflowException">The integer part of the quotient does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Divide(BigDecimal left, BigDecimal right, int scale, MidpointRounding mode)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, MaxScale);

        if (right.IsZero)
        {
            throw new DivideByZeroException();
        }

        var negative = left.IsNegative ^ right.IsNegative;

        Span<ulong> num = stackalloc ulong[DivideWorkWords];
        Span<ulong> den = stackalloc ulong[WorkWords];
        num.Clear();
        den.Clear();
        var numLen = left.CopyMagnitude(num);
        var denLen = right.CopyMagnitude(den);

        if (numLen == 0)
        {
            return Pack(num, 0, false, scale);
        }

        var lift = scale + right.Scale - left.Scale;
        if (lift > 0)
        {
            numLen = Words.ScaleUp(num, numLen, lift);
        }
        else if (lift < 0)
        {
            denLen = Words.ScaleUp(den, denLen, -lift);
        }

        Span<ulong> quotient = stackalloc ulong[DivideWorkWords];
        quotient.Clear();
        var qLen = Words.DivRem(num, numLen, den, denLen, quotient, out var remLen);

        if (remLen != 0)
        {
            qLen = RoundByRemainder(quotient, qLen, num, remLen, den, denLen, negative, mode);
        }

        return Pack(quotient, qLen, negative, scale);
    }

    private static int RoundByRemainder(
        Span<ulong> quotient,
        int quotientLength,
        ReadOnlySpan<ulong> remainder,
        int remainderLength,
        ReadOnlySpan<ulong> divisor,
        int divisorLength,
        bool isNegative,
        MidpointRounding mode)
    {
        Span<ulong> twice = stackalloc ulong[WorkWords];
        twice.Clear();
        remainder[..remainderLength].CopyTo(twice);
        var twiceLen = Words.MulAddSmall(twice, remainderLength, 2, 0);
        var cmp = Words.Compare(twice, twiceLen, divisor, divisorLength);

        var roundUp = mode switch
        {
            MidpointRounding.ToEven => cmp > 0 || (cmp == 0 && quotientLength > 0 && (quotient[0] & 1) != 0),
            MidpointRounding.AwayFromZero => cmp >= 0,
            MidpointRounding.ToZero => false,
            MidpointRounding.ToNegativeInfinity => isNegative,
            MidpointRounding.ToPositiveInfinity => !isNegative,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        return roundUp ? Words.AddOne(quotient, quotientLength) : quotientLength;
    }

    /// <summary>Returns the remainder of dividing one value by another.</summary>
    /// <remarks>The remainder takes the sign of the dividend, as it does for <see cref="decimal"/>.</remarks>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The remainder.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is zero.</exception>
    public static BigDecimal Remainder(BigDecimal left, BigDecimal right)
    {
        if (right.IsZero)
        {
            throw new DivideByZeroException();
        }

        if (left.IsZero)
        {
            return left;
        }

        Span<ulong> a = stackalloc ulong[WorkWords];
        Span<ulong> b = stackalloc ulong[WorkWords];
        a.Clear();
        b.Clear();
        var aLen = left.CopyMagnitude(a);
        var bLen = right.CopyMagnitude(b);
        var scale = AlignScales(a, ref aLen, left.Scale, b, ref bLen, right.Scale);

        if (Words.Compare(a, aLen, b, bLen) < 0)
        {
            return left;
        }

        Span<ulong> quotient = stackalloc ulong[WorkWords];
        quotient.Clear();
        Words.DivRem(a, aLen, b, bLen, quotient, out var remLen);
        return Pack(a, remLen, left.IsNegative, scale);
    }

    /// <summary>Rounds a value to a narrower scale, to nearest with ties to even.</summary>
    /// <param name="value">The value to round.</param>
    /// <param name="scale">The scale to round to, from 0 to <see cref="MaxScale"/> inclusive.</param>
    /// <returns>The rounded value, or <paramref name="value"/> unchanged when it is already no wider.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is outside 0 to <see cref="MaxScale"/>.</exception>
    public static BigDecimal Round(BigDecimal value, int scale) => Round(value, scale, MidpointRounding.ToEven);

    /// <summary>Rounds a value to an integer with the given rounding mode.</summary>
    /// <param name="value">The value to round.</param>
    /// <param name="mode">The rounding mode.</param>
    /// <returns>The rounded value, at scale 0.</returns>
    public static BigDecimal Round(BigDecimal value, MidpointRounding mode) => Round(value, 0, mode);

    /// <summary>Rounds a value to an integer, to nearest with ties to even.</summary>
    /// <param name="value">The value to round.</param>
    /// <returns>The rounded value, at scale 0.</returns>
    public static BigDecimal Round(BigDecimal value) => Round(value, 0, MidpointRounding.ToEven);

    /// <summary>Rounds a value to a narrower scale with the given rounding mode.</summary>
    /// <remarks>
    /// This operation only narrows: when <paramref name="scale"/> is greater than or equal to the
    /// value's own scale the value is returned unchanged, never padded. Use
    /// <see cref="WithScale(int, MidpointRounding)"/> to widen.
    /// </remarks>
    /// <param name="value">The value to round.</param>
    /// <param name="scale">The scale to round to, from 0 to <see cref="MaxScale"/> inclusive.</param>
    /// <param name="mode">The rounding mode.</param>
    /// <returns>The rounded value, or <paramref name="value"/> unchanged when it is already no wider.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is outside 0 to <see cref="MaxScale"/>, or <paramref name="mode"/> is not a defined <see cref="MidpointRounding"/> value.</exception>
    public static BigDecimal Round(BigDecimal value, int scale, MidpointRounding mode)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, MaxScale);

        if (value.Scale <= scale)
        {
            return value;
        }

        Span<ulong> magnitude = stackalloc ulong[WorkWords];
        magnitude.Clear();
        var len = value.CopyMagnitude(magnitude);
        if (len > 0)
        {
            len = Words.DivPow10Round(magnitude, len, value.Scale - scale, value.IsNegative, mode);
        }

        return Pack(magnitude, len, value.IsNegative, scale);
    }

    /// <summary>
    /// Returns this value expressed with the requested <paramref name="scale"/>, padding with
    /// zeros when the requested scale is wider and rounding to nearest, ties to even, when it is
    /// narrower.
    /// </summary>
    /// <param name="scale">The requested scale, from 0 to <see cref="MaxScale"/> inclusive.</param>
    /// <returns>A value numerically equal to this one when widening, or the rounded value when narrowing.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is negative or greater than <see cref="MaxScale"/>.</exception>
    /// <exception cref="OverflowException">Widening would push significant digits out of the 256-bit magnitude.</exception>
    public BigDecimal WithScale(int scale) => WithScale(scale, MidpointRounding.ToEven);

    /// <summary>
    /// Returns this value expressed with the requested <paramref name="scale"/>, padding with
    /// zeros when the requested scale is wider and rounding with <paramref name="mode"/> when it
    /// is narrower.
    /// </summary>
    /// <remarks>
    /// This is the widening counterpart of <see cref="Round(BigDecimal, int, MidpointRounding)"/>,
    /// which never adds digits. Widening is exact by definition, so a scale the magnitude cannot
    /// hold is rejected rather than rounded: the operation exists to present a value at the scale
    /// a database column declares, and a silently narrower result would not serve that.
    /// </remarks>
    /// <param name="scale">The requested scale, from 0 to <see cref="MaxScale"/> inclusive.</param>
    /// <param name="mode">The rounding mode applied when <paramref name="scale"/> is narrower than the current scale.</param>
    /// <returns>A value numerically equal to this one when widening, or the rounded value when narrowing.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is negative or greater than <see cref="MaxScale"/>; or rounding is required and <paramref name="mode"/> is not a defined <see cref="MidpointRounding"/> value.</exception>
    /// <exception cref="OverflowException">Widening would push significant digits out of the 256-bit magnitude.</exception>
    public BigDecimal WithScale(int scale, MidpointRounding mode)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, MaxScale);

        var current = Scale;
        if (scale == current)
        {
            return this;
        }

        if (scale < current)
        {
            return Round(this, scale, mode);
        }

        if (IsZero)
        {
            return new BigDecimal(0, 0, 0, 0, false, scale);
        }

        Span<ulong> magnitude = stackalloc ulong[WorkWords];
        magnitude.Clear();
        var len = CopyMagnitude(magnitude);
        len = Words.ScaleUp(magnitude, len, scale - current);
        if (len > WordCount)
        {
            ThrowMantissaOverflow();
        }

        return new BigDecimal(magnitude[0], magnitude[1], magnitude[2], magnitude[3], IsNegative, scale);
    }

    /// <summary>Returns the largest integer no greater than the value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The floor, at scale 0.</returns>
    public static BigDecimal Floor(BigDecimal value) => Round(value, 0, MidpointRounding.ToNegativeInfinity);

    /// <summary>Returns the smallest integer no less than the value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The ceiling, at scale 0.</returns>
    public static BigDecimal Ceiling(BigDecimal value) => Round(value, 0, MidpointRounding.ToPositiveInfinity);

    /// <summary>Discards the fractional digits of a value, rounding towards zero.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The integral part, at scale 0.</returns>
    public static BigDecimal Truncate(BigDecimal value) => Round(value, 0, MidpointRounding.ToZero);

    /// <summary>Returns the smaller of two values.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The smaller value, with its own scale intact.</returns>
    public static BigDecimal Min(BigDecimal left, BigDecimal right) => left <= right ? left : right;

    /// <summary>Returns the larger of two values.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The larger value, with its own scale intact.</returns>
    public static BigDecimal Max(BigDecimal left, BigDecimal right) => left >= right ? left : right;

    /// <summary>Constrains a value to a closed range.</summary>
    /// <param name="value">The value to clamp.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <returns><paramref name="min"/>, <paramref name="value"/> or <paramref name="max"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="min"/> is greater than <paramref name="max"/>.</exception>
    public static BigDecimal Clamp(BigDecimal value, BigDecimal min, BigDecimal max)
    {
        if (min > max)
        {
            throw new ArgumentException("min cannot be greater than max.", nameof(min));
        }

        return value < min ? min : (value > max ? max : value);
    }

    private static int StripTrailingZeros(Span<ulong> magnitude, int length, ref int scale, int floorScale)
    {
        floorScale = Math.Max(floorScale, 0);
        while (scale > floorScale && length > 0)
        {
            length = Words.DivRemSmall(magnitude, length, 10, out var remainder);
            if (remainder != 0)
            {
                length = Words.MulAddSmall(magnitude, length, 10, remainder);
                break;
            }

            scale--;
        }

        return length;
    }
}
