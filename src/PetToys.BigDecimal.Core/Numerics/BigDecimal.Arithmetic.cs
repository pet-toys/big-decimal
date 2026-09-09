using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace PetToys.BigDecimal.Numerics;

public readonly partial struct BigDecimal
{
    internal const int WorkWords = 24;

    private const int DivideWorkWords = 32;

    // The width the power's chain runs at, and it is not WorkWords: that constant is sized for a
    // lifted dividend, this one is sized from an error budget. Five words is the floor, being what
    // holds any representable result plus the carry AddOne needs, and it is what the exactness
    // guarantee requires. The other three are guard digits. At most 62 multiplications are
    // reachable - 31 squarings and 31 conditional multiplies, for the widest exponent an int
    // carries - each giving up at most half a unit in the last place of a 154-digit value, so the
    // accumulated relative error stays below 1e-151 against a result stated to 77 digits. A tie
    // closer than that to a rounding boundary is misrounded and nothing else is. Wider would buy
    // digits nobody can observe and cost a quadratic multiplication for each of them.
    private const int PowWorkWords = 8;

    // Every 154-digit value fits eight words and only some 155-digit ones do, which is the same
    // relation MaxDigits has to WordCount and the reason the reduction takes both numbers.
    private const int PowWorkDigits = 154;

    // The floor the chain's scale saturates at. It goes below zero when a value has no fractional
    // digits left and is still too wide, which records how many digits were given up rather than
    // failing, and it doubles on every squaring, so an exponent near int.MaxValue would take it
    // past what an int holds. Below this floor the outcome is already settled and cannot change
    // back: a value that reached it carries the full working width, so it is at least 1e562, which
    // no result can hold and whose reciprocal is below the floor of the range. Saturating there
    // therefore decides nothing the arithmetic had not already decided.
    private const int PowMinScale = -(MaxScale + PowWorkDigits);

    // The ceiling the chain's scale saturates at, and the mirror of the floor. It is not MaxScale:
    // capping a working value where a result is capped rounds it to the result's scale while it is
    // still an intermediate, so packing it rounds a second time, and what packing receives has
    // already been reduced at a width where 78 digits are inside the band - which is exactly the
    // 78-digit result the reduction rule says cannot happen. The two extra digits are what put the
    // cap out of reach of a result: a value that has just been reduced here carries at most
    // PowWorkDigits + 1 digits, so at this scale it is below 1.4e-257, which is under half a unit
    // in the last place of MaxScale and packs to zero. The accumulator and the squared factor are
    // powers of one base and move together, so a chain that reached this scale only gets smaller.
    private const int PowMaxScale = MaxScale + PowWorkDigits + 2;

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
    /// <returns>The negated value, at the same scale. NaN negates to itself; an infinity negates to the other one.</returns>
    public static BigDecimal Negate(BigDecimal value)
    {
        if (value.IsNonFinite)
        {
            return IsNaN(value) ? value : (value.IsNegative ? PositiveInfinity : NegativeInfinity);
        }

        return new(value.RawL0, value.RawL1, value.RawL2, value.RawL3, !value.IsNegative, value.Scale);
    }

    /// <summary>Returns the magnitude of a value, dropping its sign.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The absolute value, at the same scale. NaN is returned as itself and either infinity as the positive one.</returns>
    public static BigDecimal Abs(BigDecimal value)
    {
        if (value.IsNonFinite)
        {
            return IsNaN(value) ? value : PositiveInfinity;
        }

        return new(value.RawL0, value.RawL1, value.RawL2, value.RawL3, false, value.Scale);
    }

    internal ulong RawL0 => _l0;

    internal ulong RawL1 => _l1;

    internal ulong RawL2 => _l2;

    internal ulong RawL3 => _l3;

    private static BigDecimal AddCore(BigDecimal left, BigDecimal right, bool rightNegative)
    {
        // The guard sits here rather than in Add and Subtract because those two are one-line
        // forwarders that the caller inlines. A branch and a second call in their bodies stopped
        // that: the operator then called the forwarder, which called this, and two forty-byte
        // structs were copied across a frame that had not existed. Measured at 4.3 ns on the
        // worst shape, against the two flag tests below costing nothing readable.
        if (left.IsNonFinite || right.IsNonFinite)
        {
            return NonFiniteSum(left, right, rightNegative);
        }

        Span<ulong> a = stackalloc ulong[WorkWords];
        Span<ulong> b = stackalloc ulong[WorkWords];

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
        if (left.IsNonFinite || right.IsNonFinite)
        {
            return NonFiniteResult(left, right, NonFiniteOp.Multiply);
        }

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
    /// below the difference of the operands' scales - or below zero, when that difference is
    /// negative - which mirrors <see cref="decimal"/>.
    /// </remarks>
    /// <param name="left">The dividend.</param>
    /// <param name="right">The divisor.</param>
    /// <returns>The quotient.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is zero.</exception>
    /// <exception cref="OverflowException">The integer part of the quotient does not fit the 256-bit magnitude.</exception>
    public static BigDecimal Divide(BigDecimal left, BigDecimal right)
    {
        if (left.IsNonFinite || right.IsNonFinite)
        {
            return NonFiniteResult(left, right, NonFiniteOp.Divide);
        }

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
        Span<ulong> quotient = stackalloc ulong[WorkWords];
        var denLen = right.CopyMagnitude(den);

        var scale = left.Scale - right.Scale;
        var floorLift = Math.Max(-scale, 0);
        var floorScale = scale + floorLift;

        // Look for an exact quotient before lifting the dividend to full precision. Lifting first
        // and stripping afterwards produces the same value, but it manufactures one trailing zero
        // per lifted digit and then pays a division to remove every nineteen of them, which is the
        // whole reason an exact division used to cost several times an inexact one.
        //
        // A trial division at the scale difference answers first. It is the cheapest thing that can
        // be tried, it lifts nothing and leaves nothing to strip, and the specification already
        // forbids reducing an exact quotient below that scale, so a zero remainder is the whole
        // answer.
        //
        // Each depth of the search starts over from the dividend rather than continuing from the
        // previous depth's remainder. Continuing is an identity - with N * 10^f = q * D + r, the
        // quotient at f + k is q * 10^k + (r * 10^k) / D - and it was written, measured and
        // removed on 2026-09-08: it trades one lift of the dividend for a lift of the quotient
        // and a lift of the remainder, which cost more than the trial division it saves. The
        // dividend is four words and copying it is not what this path pays for.
        if (TryDivideExactly(left, num, den, denLen, quotient, floorLift, out var exactLen))
        {
            return Pack(quotient, exactLen, negative, floorScale);
        }

        // Failing that, the divisor's own factors: one that is 2^x times 5^y divides exactly at
        // max(x, y) places whatever the dividend is, so the dividend is lifted by that much rather
        // than by everything the mantissa would hold. The test costs one remainder pass, and a
        // divisor carrying any other prime is rejected on it.
        Span<ulong> factors = stackalloc ulong[WordCount];
        den[..denLen].CopyTo(factors);
        if (Words.TryDecimalDivisorExponent(factors, denLen, out var places))
        {
            var exactLift = Math.Max(places, floorLift);
            if (scale + exactLift <= MaxScale
                && TryDivideExactly(left, num, den, denLen, quotient, exactLift, out exactLen))
            {
                var exactScale = scale + exactLift;
                exactLen = StripTrailingZeros(quotient, exactLen, ref exactScale, floorScale);
                return Pack(quotient, exactLen, negative, exactScale);
            }
        }

        var numLen = left.CopyMagnitude(num);
        var numDigits = Words.DecimalDigitCount(num, numLen);
        var denDigits = Words.DecimalDigitCount(den, denLen);
        var lift = MaxDigits - 1 - numDigits + denDigits;
        lift = Math.Max(lift, -scale);
        lift = Math.Min(lift, MaxScale - scale);
        lift = Math.Max(lift, 0);

        if (lift > 0)
        {
            numLen = Words.ScaleUp(num, numLen, lift);
        }

        scale += lift;

        Words.Poison(quotient);
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

        if (left.IsNonFinite || right.IsNonFinite)
        {
            // The requested scale is dropped rather than applied: a non-finite result carries
            // scale 0 and has no digits to round.
            return NonFiniteResult(left, right, NonFiniteOp.Divide);
        }

        if (right.IsZero)
        {
            throw new DivideByZeroException();
        }

        var negative = left.IsNegative ^ right.IsNegative;

        Span<ulong> num = stackalloc ulong[DivideWorkWords];
        Span<ulong> den = stackalloc ulong[WorkWords];
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
        Words.Poison(quotient);
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
        Words.Poison(twice);
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
        if (left.IsNonFinite || right.IsNonFinite)
        {
            return NonFiniteResult(left, right, NonFiniteOp.Remainder);
        }

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
        var aLen = left.CopyMagnitude(a);
        var bLen = right.CopyMagnitude(b);
        var scale = AlignScales(a, ref aLen, left.Scale, b, ref bLen, right.Scale);

        if (Words.Compare(a, aLen, b, bLen) < 0)
        {
            return left;
        }

        Span<ulong> quotient = stackalloc ulong[WorkWords];
        Words.Poison(quotient);
        Words.DivRem(a, aLen, b, bLen, quotient, out var remLen);
        return Pack(a, remLen, left.IsNegative, scale);
    }

    /// <summary>Raises a value to an integer power.</summary>
    /// <remarks>
    /// The exact power is returned whenever the exact power is representable, and a caller can
    /// decide whether that holds from the operands alone: it holds when the unscaled magnitude
    /// raised to the exponent fits the 256-bit mantissa and the value's scale multiplied by the
    /// exponent is at most <see cref="MaxScale"/>. Where it does not hold, the excess fractional
    /// digits are rounded half to even and <see cref="OverflowException"/> is thrown only when no
    /// fractional digits remain to give up, which is the same rule multiplication answers to. The
    /// result's scale is the value's scale multiplied by the exponent, capped at
    /// <see cref="MaxScale"/>, so trailing zeros are preserved exactly as multiplication preserves
    /// them. The digits are given up once, from the exact power rather than from an intermediate
    /// standing in for it, and a result that had to give any up is reduced into the same 77-digit
    /// band a product that does not fit is reduced into, for as long as it has fractional digits
    /// to give. A reduction that reaches scale 0 stops there, so a power with no fraction left can
    /// still come back at the 78 digits the mantissa holds.
    /// <para>
    /// A negative exponent is the reciprocal of the positive power, to the full precision the
    /// magnitude allows and rounded to nearest with ties to even, under the contract
    /// <see cref="Divide(BigDecimal, BigDecimal)"/> states. It is computed rather than composed, so
    /// a result the type can represent is returned even where the positive power it is the
    /// reciprocal of cannot be: <c>Pow(2, -300)</c> answers, though <c>2^300</c> does not fit.
    /// </para>
    /// <para>
    /// <see cref="OverflowException"/> is raised for the result and never for an intermediate, so
    /// whether this operation throws can be decided from the operands and the exponent alone. The
    /// two edges of a negative exponent are each other's mirror: a positive power too small to
    /// represent has a reciprocal too large to represent and throws, and one too large to represent
    /// has a reciprocal below the floor of the range and returns zero.
    /// </para>
    /// <para>
    /// An exponent of zero returns <see cref="One"/> for every value including
    /// <see cref="NaN"/>, which is the one place in this type where a NaN operand does not
    /// propagate; <c>x^0</c> does not read <c>x</c>. <c>Pow(Zero, -1)</c> is
    /// <see cref="double.PositiveInfinity"/> in <see cref="Math.Pow"/> and throws
    /// <see cref="DivideByZeroException"/> here, because no finite operand of this type produces a
    /// non-finite value.
    /// </para>
    /// </remarks>
    /// <param name="value">The value to raise.</param>
    /// <param name="exponent">The exponent.</param>
    /// <returns>The value raised to the exponent.</returns>
    /// <exception cref="DivideByZeroException"><paramref name="value"/> is zero and <paramref name="exponent"/> is negative.</exception>
    /// <exception cref="OverflowException">The integer part of the result does not fit the 256-bit magnitude, or <paramref name="exponent"/> is negative and the positive power is too small to represent, so its reciprocal is too large.</exception>
    public static BigDecimal Pow(BigDecimal value, int exponent)
    {
        // Before everything, including the non-finite check: x^0 does not read x, so it is One for
        // NaN too. That is System.Double's answer and IEEE 754's.
        if (exponent == 0)
        {
            return One;
        }

        if (value.IsNonFinite)
        {
            return NonFinitePower(value, exponent);
        }

        if (exponent == 1)
        {
            return value;
        }

        // The magnitude of the exponent does not fit an int when the exponent is int.MinValue.
        var count = exponent < 0 ? -(long)exponent : exponent;
        var negative = value.IsNegative && (count & 1) != 0;

        if (value.IsZero)
        {
            if (exponent < 0)
            {
                throw new DivideByZeroException();
            }

            Span<ulong> zero = stackalloc ulong[WordCount];
            zero.Clear();
            return Pack(zero, 0, false, (int)Math.Min((long)value.Scale * count, MaxScale));
        }

        Span<ulong> acc = stackalloc ulong[PowWorkWords + 1];
        Span<ulong> factor = stackalloc ulong[PowWorkWords + 1];
        Span<ulong> product = stackalloc ulong[(PowWorkWords * 2) + 1];

        Words.Poison(acc);
        acc[0] = 1;
        var accLen = 1;
        var accScale = 0;

        var factorLen = value.CopyMagnitude(factor);
        var factorScale = value.Scale;

        // Square and multiply. The exactness guarantee rests on the shape of this loop rather than
        // on the width of the accumulator: every intermediate it holds is the unscaled magnitude
        // raised to some power no greater than the exponent, on the accumulator and on the squared
        // factor alike, because the final squaring is skipped once the exponent is exhausted. So no
        // intermediate has more digits or a wider scale than the exact result, and a result that is
        // representable is reached without any of them having been reduced. Reordering the loop so
        // that it squares one more time would break the guarantee without failing a test that names
        // it.
        for (var remaining = count; ;)
        {
            if ((remaining & 1) != 0)
            {
                accLen = MultiplyReduced(acc, accLen, ref accScale, factor, factorLen, factorScale, product, negative);
            }

            remaining >>= 1;
            if (remaining == 0)
            {
                break;
            }

            factorLen = MultiplyReduced(factor, factorLen, ref factorScale, factor, factorLen, factorScale, product, negative);
        }

        return exponent < 0
            ? Reciprocal(acc, accLen, accScale, negative)
            : Pack(acc, accLen, negative, accScale);
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

        // A non-finite value reports scale 0 and this operation only narrows, so it leaves
        // through the check below rather than through a guard of its own. Floor, Ceiling and
        // Truncate all come through here and inherit that.
        if (value.Scale <= scale)
        {
            return value;
        }

        Span<ulong> magnitude = stackalloc ulong[WorkWords];
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

        if (IsNonFinite)
        {
            return this;
        }

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
    /// <remarks>NaN on either side wins, as it does for <see cref="double"/>. Use
    /// <c>INumber&lt;BigDecimal&gt;.MinNumber</c> for the variant that ignores it.</remarks>
    public static BigDecimal Min(BigDecimal left, BigDecimal right)
    {
        if (IsNaN(left) || IsNaN(right))
        {
            return NaN;
        }

        return left <= right ? left : right;
    }

    /// <summary>Returns the larger of two values.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns>The larger value, with its own scale intact.</returns>
    /// <remarks>NaN on either side wins, as it does for <see cref="double"/>. Use
    /// <c>INumber&lt;BigDecimal&gt;.MaxNumber</c> for the variant that ignores it.</remarks>
    public static BigDecimal Max(BigDecimal left, BigDecimal right)
    {
        if (IsNaN(left) || IsNaN(right))
        {
            return NaN;
        }

        return left >= right ? left : right;
    }

    /// <summary>Constrains a value to a closed range.</summary>
    /// <param name="value">The value to clamp.</param>
    /// <param name="min">The lower bound.</param>
    /// <param name="max">The upper bound.</param>
    /// <returns><paramref name="min"/>, <paramref name="value"/> or <paramref name="max"/>.</returns>
    /// <remarks>
    /// NaN anywhere gives NaN, in a bound as well as in the value. A NaN bound leaves
    /// <c>min &gt; max</c> false, so it raises nothing on its own and would otherwise hand the
    /// value back as if the range had been checked.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="min"/> is greater than <paramref name="max"/>.</exception>
    public static BigDecimal Clamp(BigDecimal value, BigDecimal min, BigDecimal max)
    {
        if (min > max)
        {
            throw new ArgumentException("min cannot be greater than max.", nameof(min));
        }

        if (IsNaN(value) || IsNaN(min) || IsNaN(max))
        {
            return NaN;
        }

        return value < min ? min : (value > max ? max : value);
    }

    // Addition and subtraction over a non-finite operand. They differ only in the sign the right
    // operand is taken with, which is what AddCore already receives, so one helper serves both.
    // Never inlined: it is called from AddCore, whose stackalloc body is not inlined either, and
    // keeping it out means a caller reading this file does not have to wonder whether the cold
    // path is what put the frame back. Measured against System.Double rather than recalled.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BigDecimal NonFiniteSum(BigDecimal left, BigDecimal right, bool rightNegative)
    {
        if (IsNaN(left) || IsNaN(right))
        {
            return NaN;
        }

        if (!right.IsNonFinite)
        {
            return left;
        }

        if (!left.IsNonFinite)
        {
            return rightNegative ? NegativeInfinity : PositiveInfinity;
        }

        // Two infinities: the same sign gives that infinity, opposite signs give NaN.
        return left.IsNegative == rightNegative ? left : NaN;
    }

    private enum NonFiniteOp
    {
        Multiply,
        Divide,
        Remainder,
    }

    // Multiplication, division and remainder over a non-finite operand, answered before any
    // scale work and before the IsZero fast paths, which is what keeps 0 * Infinity from taking
    // the zero shortcut. Measured against System.Double rather than recalled; the only
    // divergence is that 1 / -Infinity is Zero here, because zero carries no sign in this type.
    //
    // Never inlined, for the reason BASELINE.md records under runs F to I: guarding an operation
    // is not free by inspection, and where the guard is written decides what it costs. These
    // three callers have bodies too large to inline either way, so the attribute changes nothing
    // for them; it is here so that a caller which is small does not inherit the problem silently.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BigDecimal NonFiniteResult(BigDecimal left, BigDecimal right, NonFiniteOp op)
    {
        if (IsNaN(left) || IsNaN(right))
        {
            return NaN;
        }

        var leftInfinite = left.IsNonFinite;
        var rightInfinite = right.IsNonFinite;
        var negative = left.IsNegative ^ right.IsNegative;

        switch (op)
        {
            case NonFiniteOp.Multiply:
                return left.IsZero || right.IsZero
                    ? NaN
                    : (negative ? NegativeInfinity : PositiveInfinity);
            case NonFiniteOp.Divide:
                if (leftInfinite && rightInfinite)
                {
                    return NaN;
                }

                // An infinity over zero is an infinity, not a DivideByZeroException: the divisor
                // never reaches the check below, which is deliberate and is what double does.
                return leftInfinite
                    ? (negative ? NegativeInfinity : PositiveInfinity)
                    : Zero;
            default:
                // An infinite dividend has no remainder; a finite one modulo an infinity is itself.
                return leftInfinite ? NaN : left;
        }
    }

    // The three values raised to a power. The exponent is never zero here: that case is answered
    // before the value is read at all, because x^0 does not depend on x, which is why it is the one
    // place in this type where a NaN operand does not propagate.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static BigDecimal NonFinitePower(BigDecimal value, int exponent)
    {
        Debug.Assert(exponent != 0, "an exponent of zero is answered before the value is read");

        if (IsNaN(value))
        {
            return NaN;
        }

        if (exponent < 0)
        {
            // Math.Pow gives -0 for (-Infinity)^-1. Zero carries no sign in this type, which is the
            // same divergence 1 / -Infinity already carries and is documented as.
            return Zero;
        }

        return value.IsNegative && (exponent & 1) != 0 ? NegativeInfinity : PositiveInfinity;
    }

    /// <summary>
    /// Multiplies the accumulator by a factor at the power's working width, reducing the product
    /// back to that width when it outgrows it.
    /// </summary>
    /// <remarks>
    /// The reduction is <c>TryReduce</c>, the same rule that packs a result, so the accumulator
    /// gives up digits the way the mantissa does and cannot drift from it. It is reached only when
    /// the exact power does not fit, and never on the way to one that does.
    /// <para>
    /// The width it states is the chain's own, <c>PowMaxScale</c> included. A working value is not
    /// a result and is not rounded like one: reducing it to <see cref="MaxScale"/> here would spend
    /// the one rounding the result is entitled to, and packing would then round what came out of it
    /// again. Everything that shapes a representable result is therefore the single reduction
    /// <c>TryPack</c> performs, which is where the digit band belongs.
    /// </para>
    /// <para>
    /// Nothing here refuses. A step that has no fractional digits left to give up goes below scale
    /// 0 rather than reporting overflow, which is what lets the whole operation throw for the one
    /// reason a caller can check - the result does not fit - instead of for an intermediate the
    /// caller never asked for. It is also what makes a reciprocal answerable where the power it is
    /// the reciprocal of is not representable at all.
    /// </para>
    /// </remarks>
    private static int MultiplyReduced(
        Span<ulong> accumulator,
        int accumulatorLength,
        ref int accumulatorScale,
        ReadOnlySpan<ulong> factor,
        int factorLength,
        int factorScale,
        Span<ulong> product,
        bool isNegative)
    {
        var length = Words.Mul(accumulator, accumulatorLength, factor, factorLength, product);
        var scale = accumulatorScale + factorScale;

        var reduced = TryReduce(product, ref length, ref scale, PowWorkWords, PowWorkDigits, PowMaxScale, isNegative, allowNegativeScale: true);
        Debug.Assert(reduced, "a reduction allowed to go below scale 0 has nothing left to refuse on");

        product[..length].CopyTo(accumulator);
        accumulatorScale = Math.Max(scale, PowMinScale);
        return length;
    }

    /// <summary>Divides one by a power held at the working width, to full precision.</summary>
    /// <remarks>
    /// Taken from the accumulator rather than from a packed value on purpose. Composing the answer
    /// as <c>One / Pow(value, -exponent)</c> throws for every result whose positive power does not
    /// fit the mantissa, and those are results this type can hold perfectly well: <c>2^-300</c> is
    /// about 4.9e-91 and comfortably inside the range while <c>2^300</c> is not.
    /// <para>
    /// With the power held as an unscaled magnitude <c>U</c> at scale <c>S</c>, the answer is
    /// <c>10^S / U</c>, produced as <c>10^(S+t) / U</c> at scale <c>t</c> with <c>t</c> chosen so
    /// the quotient carries the full significant-digit capacity. The rounding is the code that
    /// rounds a division rather than a second implementation of it, so an exact reciprocal comes
    /// back at its shortest scale exactly as an exact quotient does.
    /// </para>
    /// <para>
    /// <c>S</c> reaches <c>PowMaxScale</c> rather than <see cref="MaxScale"/>, since the chain is
    /// capped where the cap decides nothing. Both bounds this rests on still hold. <c>t</c> clamps
    /// to 0 as soon as <c>S</c> passes <c>MaxDigits - 2</c> plus the digit count, so the lifted
    /// numerator is at most <c>10^PowMaxScale</c> and fits the 32-word buffer, which holds a little
    /// over 616 digits. And every power at a scale above <see cref="MaxScale"/> has a reciprocal of
    /// at least 1e256 over the working width, about 7.4e101, so all of them overflow - the same
    /// answer they reached before by underflowing to a zero power, arrived at from the result.
    /// </para>
    /// </remarks>
    private static BigDecimal Reciprocal(ReadOnlySpan<ulong> power, int powerLength, int powerScale, bool isNegative)
    {
        if (powerLength == 0)
        {
            // The positive power underflowed to zero. The fault is that the reciprocal is too large
            // to represent, not that anything was divided by zero, and reporting the latter would
            // send the caller looking at the wrong operand.
            ThrowMantissaOverflow();
        }

        var digits = Words.DecimalDigitCount(power, powerLength);
        var scale = Math.Clamp(MaxDigits - 2 + digits - powerScale, 0, MaxScale);
        var lift = powerScale + scale;

        if (lift < 0)
        {
            // The power is so large that even at the widest scale the type carries, its reciprocal
            // rounds to nothing. That is the underflow rule and not an overflow: the answer is
            // representable and it is zero.
            Span<ulong> underflow = stackalloc ulong[WordCount];
            underflow.Clear();
            return Pack(underflow, 0, false, MaxScale);
        }

        Span<ulong> num = stackalloc ulong[DivideWorkWords];
        Span<ulong> quotient = stackalloc ulong[DivideWorkWords];

        Words.Poison(num);
        num[0] = 1;
        var numLen = Words.ScaleUp(num, 1, lift);

        Words.Poison(quotient);
        var quotientLen = Words.DivRem(num, numLen, power, powerLength, quotient, out var remainderLen);

        if (remainderLen == 0)
        {
            // The floor is zero, which is what Divide uses for a dividend of One at scale zero.
            quotientLen = StripTrailingZeros(quotient, quotientLen, ref scale, 0);
            return Pack(quotient, quotientLen, isNegative, scale);
        }

        quotientLen = RoundByRemainder(
            quotient,
            quotientLen,
            num,
            remainderLen,
            power,
            powerLength,
            isNegative,
            MidpointRounding.ToEven);

        return Pack(quotient, quotientLen, isNegative, scale);
    }

    /// <summary>
    /// Divides the dividend by the divisor after lifting it by <paramref name="lift"/> decimal
    /// places, and reports whether the quotient came out exactly.
    /// </summary>
    /// <remarks>
    /// The buffers are the caller's, and both are left in an undefined state when the division is
    /// not exact: the primitive writes the remainder over the dividend. A caller that goes on to
    /// the next depth therefore copies the dividend again, which is four words and was measured to
    /// be the cheaper half of the trade; the comment on the operator overload says what continuing
    /// from the remainder instead would cost.
    /// </remarks>
    private static bool TryDivideExactly(
        BigDecimal dividend,
        Span<ulong> num,
        ReadOnlySpan<ulong> den,
        int denLen,
        Span<ulong> quotient,
        int lift,
        out int quotientLength)
    {
        var numLen = dividend.CopyMagnitude(num);
        if (lift > 0)
        {
            numLen = Words.ScaleUp(num, numLen, lift);
        }

        Words.Poison(quotient);
        quotientLength = Words.DivRem(num, numLen, den, denLen, quotient, out var remainderLength);

        return remainderLength == 0;
    }

    private static int StripTrailingZeros(Span<ulong> magnitude, int length, ref int scale, int floorScale)
    {
        floorScale = Math.Max(floorScale, 0);
        while (scale > floorScale && length > 0)
        {
            // Count first, divide once. Counting costs at most one remainder pass and nothing at
            // all when the value ends in a non-zero digit, where dividing by ten only to multiply
            // it back used to be the whole cost of the call.
            var zeros = Words.TrailingDecimalZeros(magnitude, length, scale - floorScale);
            if (zeros == 0)
            {
                break;
            }

            length = Words.DivRemSmall(magnitude, length, Words.Pow10Divisors[zeros], out var remainder);
            Debug.Assert(remainder == 0, "the counted zeros divide the magnitude exactly");
            scale -= zeros;

            if (zeros < Words.MaxZerosPerPass)
            {
                break;
            }
        }

        return length;
    }
}
