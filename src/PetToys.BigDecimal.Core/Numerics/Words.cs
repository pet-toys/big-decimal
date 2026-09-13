using System;
using System.Diagnostics;
using System.Numerics;

namespace PetToys.BigDecimal.Numerics;

internal static class Words
{
    internal const ulong TenPow19 = 10_000_000_000_000_000_000UL;

    /// <summary>
    /// The most decimal zeros a single division can strip, bounded by the largest power of ten a
    /// word holds.
    /// </summary>
    internal const int MaxZerosPerPass = 19;

    /// <summary>The largest exponent of five a single word holds.</summary>
    private const int MaxFivesPerWord = 27;

    // The sentinel Poison writes, recognisable in a debugger.
    private const ulong PoisonWord = 0xDEAD_BEEF_DEAD_BEEFUL;

    private static readonly ulong[] Pow5Values =
    [
        1UL,
        5UL,
        25UL,
        125UL,
        625UL,
        3_125UL,
        15_625UL,
        78_125UL,
        390_625UL,
        1_953_125UL,
        9_765_625UL,
        48_828_125UL,
        244_140_625UL,
        1_220_703_125UL,
        6_103_515_625UL,
        30_517_578_125UL,
        152_587_890_625UL,
        762_939_453_125UL,
        3_814_697_265_625UL,
        19_073_486_328_125UL,
        95_367_431_640_625UL,
        476_837_158_203_125UL,
        2_384_185_791_015_625UL,
        11_920_928_955_078_125UL,
        59_604_644_775_390_625UL,
        298_023_223_876_953_125UL,
        1_490_116_119_384_765_625UL,
        7_450_580_596_923_828_125UL,
    ];

    private static readonly ulong[] Pow10Values =
    [
        1UL,
        10UL,
        100UL,
        1_000UL,
        10_000UL,
        100_000UL,
        1_000_000UL,
        10_000_000UL,
        100_000_000UL,
        1_000_000_000UL,
        10_000_000_000UL,
        100_000_000_000UL,
        1_000_000_000_000UL,
        10_000_000_000_000UL,
        100_000_000_000_000UL,
        1_000_000_000_000_000UL,
        10_000_000_000_000_000UL,
        100_000_000_000_000_000UL,
        1_000_000_000_000_000_000UL,
        TenPow19,
    ];

    internal static ReadOnlySpan<ulong> Pow10 => Pow10Values;

    // Static readonly arrays behind span-returning properties, never a collection expression in
    // an expression-bodied property: that form allocates on every access.
    private static readonly Divisor[] Pow10DivisorValues = BuildDivisors(Pow10Values);

    private static readonly Divisor[] Pow5DivisorValues = BuildDivisors(Pow5Values);

    /// <summary>The powers of ten, each prepared for <see cref="DivRem2By1"/>.</summary>
    internal static ReadOnlySpan<Divisor> Pow10Divisors => Pow10DivisorValues;

    /// <summary><see cref="TenPow19"/> prepared for <see cref="DivRem2By1"/>.</summary>
    internal static ref readonly Divisor TenPow19Divisor => ref Pow10DivisorValues[MaxZerosPerPass];

    /// <summary>The powers of five, each prepared for <see cref="DivRem2By1"/>.</summary>
    internal static ReadOnlySpan<Divisor> Pow5Divisors => Pow5DivisorValues;

    /// <summary>
    /// Counts the trailing decimal zeros of a magnitude, up to <paramref name="limit"/> and to at
    /// most <see cref="MaxZerosPerPass"/>, without dividing it.
    /// </summary>
    /// <remarks>
    /// The smaller of the twos and the fives: the twos are a trailing-zero count, and the fives
    /// come from one remainder pass by 5^27, whose remainder carries the value's fives.
    /// </remarks>
    internal static int TrailingDecimalZeros(ReadOnlySpan<ulong> value, int length, int limit)
    {
        limit = Math.Min(limit, MaxZerosPerPass);
        if (limit <= 0 || length <= 0)
        {
            return 0;
        }

        var twos = TrailingBinaryZeros(value, length);
        if (twos == 0)
        {
            return 0;
        }

        var cap = Math.Min(twos, limit);
        var remainder = RemSmall(value, length, Pow5Divisors[MaxFivesPerWord]);
        return remainder == 0 ? cap : CountFives(remainder, cap);
    }

    /// <summary>
    /// Reports whether a magnitude is a power of two times a power of five, and if it is, returns
    /// the number of decimal places at which a division by it comes out exactly.
    /// </summary>
    /// <remarks>
    /// a / b is exact at max(x, y) places when b is 2^x times 5^y. One shift and one remainder
    /// pass, on which any other prime factor is rejected. The magnitude is consumed.
    /// </remarks>
    internal static bool TryDecimalDivisorExponent(Span<ulong> value, int length, out int places)
    {
        places = 0;
        if (length <= 0)
        {
            return false;
        }

        var twos = TrailingBinaryZeros(value, length);
        length = ShiftRightInPlace(value, length, twos);

        var fives = 0;
        while (length > 0)
        {
            var remainder = RemSmall(value, length, Pow5Divisors[MaxFivesPerWord]);
            if (remainder == 0)
            {
                length = DivRemSmall(value, length, Pow5Divisors[MaxFivesPerWord], out _);
                fives += MaxFivesPerWord;
                continue;
            }

            var extra = CountFives(remainder, MaxFivesPerWord);
            if (extra > 0)
            {
                length = DivRemSmall(value, length, Pow5Divisors[extra], out _);
                fives += extra;
            }

            break;
        }

        if (length != 1 || value[0] != 1)
        {
            return false;
        }

        places = Math.Max(twos, fives);
        return true;
    }

    /// <summary>
    /// Computes the reciprocal a 128-by-64 division needs: the largest 128-bit value divided by a
    /// normalised divisor, less 2^64.
    /// </summary>
    /// <remarks>
    /// A plain division rather than a Newton refinement: it runs once per divisor, not per word.
    /// </remarks>
    internal static ulong Reciprocal(ulong divisorNormalized)
    {
        Debug.Assert(divisorNormalized >> 63 != 0, "divisor must be normalized");

        // The conversion discards the high bit, which is the subtraction of 2^64.
        return unchecked((ulong)(UInt128.MaxValue / divisorNormalized));
    }

    /// <summary>Divides a 128-bit value by a single normalised word.</summary>
    /// <remarks>
    /// The estimate-and-correct division of Moller and Granlund, replacing a <see cref="UInt128"/>
    /// over a <see cref="ulong"/> that the runtime lowers to hardware only while the high half is
    /// zero. Every step is modulo 2^64 by design, so the body is <c>unchecked</c> and must stay
    /// so. <paramref name="high"/> must be below the divisor and <paramref name="reciprocal"/>
    /// must be the divisor's own, or the quotient is wrong rather than a failure.
    /// </remarks>
    internal static ulong DivRem2By1(
        ulong high,
        ulong low,
        ulong divisorNormalized,
        ulong reciprocal,
        out ulong remainder)
    {
        Debug.Assert(divisorNormalized >> 63 != 0, "divisor must be normalized");
        Debug.Assert(high < divisorNormalized, "the quotient must fit a single word");

        unchecked
        {
            // The high half of reciprocal * high + dividend + 2^64 is the quotient to within one.
            var estimate = Math.BigMul(reciprocal, high, out var estimateLow);
            estimateLow += low;
            if (estimateLow < low)
            {
                estimate++;
            }

            estimate += high;
            estimate++;

            var rest = low - (estimate * divisorNormalized);

            // An overshoot wraps past the low half of the estimate.
            if (rest > estimateLow)
            {
                estimate--;
                rest += divisorNormalized;
            }

            // An estimate one short: about one in a thousand on random operands.
            if (rest >= divisorNormalized)
            {
                estimate++;
                rest -= divisorNormalized;
            }

            remainder = rest;

            return estimate;
        }
    }

    /// <summary>Returns the remainder of a magnitude divided by a single word, leaving it unchanged.</summary>
    /// <remarks>
    /// The loop of <see cref="DivideBySmall"/> without the quotient stores, written out so the
    /// word loop carries no "is there a quotient" test.
    /// </remarks>
    internal static ulong RemSmall(ReadOnlySpan<ulong> value, int length, in Divisor divisor)
    {
        unchecked
        {
            var shift = divisor.Shift;
            var normalized = divisor.Normalized;
            var reciprocal = divisor.Reciprocal;

            ulong rem = 0;
            if (shift != 0 && length > 0)
            {
                rem = value[length - 1] >> (64 - shift);
            }

            for (var i = length - 1; i >= 0; i--)
            {
                var below = i > 0 ? value[i - 1] : 0UL;
                var low = shift == 0 ? value[i] : (value[i] << shift) | (below >> (64 - shift));
                DivRem2By1(rem, low, normalized, reciprocal, out rem);
            }

            return rem >> shift;
        }
    }

    private static int ShiftRightInPlace(Span<ulong> value, int length, int shift)
    {
        if (shift <= 0 || length <= 0)
        {
            return length;
        }

        var words = shift / 64;
        var bits = shift % 64;
        if (words >= length)
        {
            value[..length].Clear();
            return 0;
        }

        unchecked
        {
            var remaining = length - words;
            for (var i = 0; i < remaining; i++)
            {
                var low = value[i + words] >> bits;
                value[i] = bits != 0 && i + 1 < remaining ? low | (value[i + words + 1] << (64 - bits)) : low;
            }

            value[remaining..length].Clear();
            return Normalize(value[..remaining]);
        }
    }

    private static int TrailingBinaryZeros(ReadOnlySpan<ulong> value, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (value[i] != 0)
            {
                return (i * 64) + BitOperations.TrailingZeroCount(value[i]);
            }
        }

        return 0;
    }

    // A binary search over the powers of five a word holds, not a division per five: the values
    // this sees end in many fives by construction.
    private static int CountFives(ulong value, int cap)
    {
        var bound = Math.Min(cap, MaxFivesPerWord);

        // The cap is usually the answer: a value widened to a column's scale carries that many.
        if (bound <= 0 || value % Pow5Values[bound] == 0)
        {
            return bound;
        }

        var low = 0;
        var high = bound - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (value % Pow5Values[middle] == 0)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>Fills words that nothing is allowed to read with a sentinel, so that reading one shows.</summary>
    /// <remarks>
    /// Words beyond a buffer's length are not zeroed on any hot path. A helper that read past its
    /// length would find the runtime's zero and pass; this makes it fail in Debug instead, and
    /// compiles to nothing in Release.
    /// </remarks>
    [Conditional("DEBUG")]
    internal static void Poison(Span<ulong> value) => value.Fill(PoisonWord);

    internal static int Normalize(ReadOnlySpan<ulong> value)
    {
        var len = value.Length;
        while (len > 0 && value[len - 1] == 0)
        {
            len--;
        }

        return len;
    }

    internal static int Compare(ReadOnlySpan<ulong> left, int leftLen, ReadOnlySpan<ulong> right, int rightLen)
    {
        if (leftLen != rightLen)
        {
            return leftLen < rightLen ? -1 : 1;
        }

        for (var i = leftLen - 1; i >= 0; i--)
        {
            if (left[i] != right[i])
            {
                return left[i] < right[i] ? -1 : 1;
            }
        }

        return 0;
    }

    internal static int AddInto(Span<ulong> acc, int accLen, ReadOnlySpan<ulong> right, int rightLen)
    {
        unchecked
        {
            Debug.Assert(acc.Length >= Math.Max(accLen, rightLen) + 1, "acc needs carry headroom");
            var max = Math.Max(accLen, rightLen);
            ulong carry = 0;
            for (var i = 0; i < max; i++)
            {
                var a = i < accLen ? acc[i] : 0;
                var b = i < rightLen ? right[i] : 0;
                var sum = a + b;
                var c1 = sum < a ? 1UL : 0UL;
                var sum2 = sum + carry;
                var c2 = sum2 < sum ? 1UL : 0UL;
                acc[i] = sum2;
                carry = c1 | c2;
            }

            if (carry != 0)
            {
                acc[max] = carry;
                return max + 1;
            }

            return max;
        }
    }

    internal static int SubInto(Span<ulong> acc, int accLen, ReadOnlySpan<ulong> right, int rightLen)
    {
        unchecked
        {
            Debug.Assert(rightLen <= accLen, "minuend must not be shorter");
            ulong borrow = 0;
            for (var i = 0; i < accLen; i++)
            {
                var a = acc[i];
                var b = i < rightLen ? right[i] : 0;
                var diff = a - b;
                var b1 = a < b ? 1UL : 0UL;
                var diff2 = diff - borrow;
                var b2 = diff < borrow ? 1UL : 0UL;
                acc[i] = diff2;
                borrow = b1 | b2;
            }

            Debug.Assert(borrow == 0, "caller must guarantee acc >= right");
            return Normalize(acc[..accLen]);
        }
    }

    internal static int MulAddSmall(Span<ulong> acc, int accLen, ulong multiplier, ulong addend)
    {
        unchecked
        {
            var carry = addend;
            for (var i = 0; i < accLen; i++)
            {
                var high = Math.BigMul(acc[i], multiplier, out var low);
                var sum = low + carry;
                if (sum < low)
                {
                    high++;
                }

                acc[i] = sum;
                carry = high;
            }

            if (carry != 0)
            {
                Debug.Assert(acc.Length > accLen, "acc needs headroom for the carry word");
                acc[accLen] = carry;
                return accLen + 1;
            }

            return Normalize(acc[..accLen]);
        }
    }

    /// <summary>Divides a magnitude by a single prepared word in place, returning the new length.</summary>
    internal static int DivRemSmall(Span<ulong> acc, int accLen, in Divisor divisor, out ulong remainder)
    {
        remainder = DivideBySmall(acc, accLen, divisor, acc);

        return Normalize(acc[..accLen]);
    }

    // Words go in shifted by the divisor's normalising shift and the remainder is shifted back;
    // the quotient needs no undoing. quotient may be source itself, but no other overlap is safe.
    private static ulong DivideBySmall(
        ReadOnlySpan<ulong> source,
        int length,
        in Divisor divisor,
        Span<ulong> quotient)
    {
        unchecked
        {
            var shift = divisor.Shift;
            var normalized = divisor.Normalized;
            var reciprocal = divisor.Reciprocal;

            // The bits the shift pushes out of the top word are the first dividend's high half.
            ulong rem = 0;
            if (shift != 0 && length > 0)
            {
                rem = source[length - 1] >> (64 - shift);
            }

            for (var i = length - 1; i >= 0; i--)
            {
                var below = i > 0 ? source[i - 1] : 0UL;
                var low = shift == 0 ? source[i] : (source[i] << shift) | (below >> (64 - shift));
                quotient[i] = DivRem2By1(rem, low, normalized, reciprocal, out rem);
            }

            return rem >> shift;
        }
    }

    internal static int Mul(
        ReadOnlySpan<ulong> left,
        int leftLen,
        ReadOnlySpan<ulong> right,
        int rightLen,
        Span<ulong> destination)
    {
        unchecked
        {
            destination[..(leftLen + rightLen)].Clear();
            for (var i = 0; i < leftLen; i++)
            {
                var li = left[i];
                if (li == 0)
                {
                    continue;
                }

                ulong carry = 0;
                for (var j = 0; j < rightLen; j++)
                {
                    var high = Math.BigMul(li, right[j], out var low);
                    var sum = low + carry;
                    if (sum < low)
                    {
                        high++;
                    }

                    var dst = destination[i + j];
                    var sum2 = dst + sum;
                    if (sum2 < dst)
                    {
                        high++;
                    }

                    destination[i + j] = sum2;
                    carry = high;
                }

                destination[i + rightLen] += carry;
            }

            return Normalize(destination[..(leftLen + rightLen)]);
        }
    }

    internal static int ScaleUp(Span<ulong> acc, int accLen, int power)
    {
        Debug.Assert(power >= 0, "power must be non-negative");
        while (power > 0)
        {
            var chunk = Math.Min(power, 19);
            accLen = MulAddSmall(acc, accLen, Pow10[chunk], 0);
            power -= chunk;
        }

        return accLen;
    }

    internal static int DivPow10Round(Span<ulong> acc, int accLen, int power, bool isNegative, MidpointRounding mode)
    {
        Debug.Assert(power > 0, "power must be positive");

        var sticky = false;
        var remaining = power - 1;
        while (remaining > 0 && accLen > 0)
        {
            var chunk = Math.Min(remaining, 19);
            accLen = DivRemSmall(acc, accLen, Pow10Divisors[chunk], out var rem);
            sticky |= rem != 0;
            remaining -= chunk;
        }

        // An exhausted magnitude divides to zero however many positions are left, so the digit the
        // rounding reads is zero and the sticky flag already gathered is the whole of the rest.
        ulong lastDigit = 0;
        if (accLen > 0)
        {
            accLen = DivRemSmall(acc, accLen, Pow10Divisors[1], out lastDigit);
        }

        var roundUp = mode switch
        {
            MidpointRounding.ToEven => lastDigit > 5 || (lastDigit == 5 && (sticky || (accLen > 0 && (acc[0] & 1) != 0))),
            MidpointRounding.AwayFromZero => lastDigit >= 5,
            MidpointRounding.ToZero => false,
            MidpointRounding.ToNegativeInfinity => isNegative && (lastDigit != 0 || sticky),
            MidpointRounding.ToPositiveInfinity => !isNegative && (lastDigit != 0 || sticky),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };

        if (roundUp)
        {
            accLen = AddOne(acc, accLen);
        }

        return accLen;
    }

    internal static int AddOne(Span<ulong> acc, int accLen)
    {
        unchecked
        {
            for (var i = 0; i < accLen; i++)
            {
                if (++acc[i] != 0)
                {
                    return accLen;
                }
            }

            Debug.Assert(acc.Length > accLen, "acc needs headroom for the carry word");
            acc[accLen] = 1;
            return accLen + 1;
        }
    }

    internal static int DecimalDigitCount(ReadOnlySpan<ulong> value, int len)
    {
        if (len == 0)
        {
            return 1;
        }

        if (len == 1)
        {
            return DecimalDigitCount(value[0]);
        }

        var bits = ((len - 1) * 64) + (64 - BitOperations.LeadingZeroCount(value[len - 1]));
        var estimate = (int)((bits * 19728L) >> 16) + 1;

        Span<ulong> power = stackalloc ulong[len + 2];
        Poison(power);
        power[0] = 1;
        var powerLen = ScaleUp(power, 1, estimate - 1);
        if (Compare(value, len, power, powerLen) < 0)
        {
            return estimate - 1;
        }

        powerLen = MulAddSmall(power, powerLen, 10, 0);
        return Compare(value, len, power, powerLen) >= 0 ? estimate + 1 : estimate;
    }

    internal static int DecimalDigitCount(ulong value)
    {
        var digits = 1;
        while (digits < 20 && value >= Pow10[digits])
        {
            digits++;
        }

        return digits;
    }

    /// <summary>Divides one magnitude by another, leaving the remainder in the numerator.</summary>
    /// <remarks>
    /// Every quotient word reported is one this wrote, so the quotient buffer need not be cleared
    /// first. The numerator is consumed: the remainder is written over its low words and the rest
    /// are cleared.
    /// </remarks>
    internal static int DivRem(
        Span<ulong> numerator,
        int numLen,
        ReadOnlySpan<ulong> divisor,
        int divLen,
        Span<ulong> quotient,
        out int remainderLen)
    {
        Debug.Assert(divLen > 0 && divisor[divLen - 1] != 0, "divisor must be normalized and non-zero");

        if (divLen == 1)
        {
            ulong rem;
            if (numLen <= 1)
            {
                // A hardware divide: preparing the divisor costs a 128-bit division with nothing
                // to amortise it over, 6.9x on net8.0. From two words up it wins 2x to 20x.
                var single = numLen == 0 ? 0UL : numerator[0];
                rem = single % divisor[0];
                if (numLen == 1)
                {
                    quotient[0] = single / divisor[0];
                }
            }
            else
            {
                rem = DivideBySmall(numerator, numLen, Divisor.For(divisor[0]), quotient);
            }

            numerator[..numLen].Clear();
            numerator[0] = rem;
            remainderLen = rem == 0 ? 0 : 1;

            return Normalize(quotient[..numLen]);
        }

        var cmp = Compare(numerator, numLen, divisor, divLen);
        if (cmp < 0)
        {
            remainderLen = numLen;
            return 0;
        }

        unchecked
        {
            var shift = BitOperations.LeadingZeroCount(divisor[divLen - 1]);
            Span<ulong> vn = stackalloc ulong[divLen];
            Span<ulong> un = stackalloc ulong[numLen + 1];
            ShiftLeft(divisor, divLen, shift, vn);
            un[numLen] = shift == 0 ? 0 : numerator[numLen - 1] >> (64 - shift);
            ShiftLeft(numerator, numLen, shift, un);

            var qLen = numLen - divLen + 1;
            var vHigh = vn[divLen - 1];
            var vNext = vn[divLen - 2];

            // One reciprocal serves every quotient word of this division.
            var vReciprocal = Reciprocal(vHigh);

            for (var j = qLen - 1; j >= 0; j--)
            {
                var topHigh = un[j + divLen];
                var topLow = un[j + divLen - 1];
                ulong qhat;
                UInt128 rhat;

                // A leading word equal to the divisor's means a quotient of 2^64, which no word
                // holds, so the primitive cannot answer it. The partial remainder stays a UInt128.
                if (topHigh >= vHigh)
                {
                    qhat = ulong.MaxValue;
                    rhat = new UInt128(topHigh, topLow) - ((UInt128)qhat * vHigh);
                }
                else
                {
                    qhat = DivRem2By1(topHigh, topLow, vHigh, vReciprocal, out var partial);
                    rhat = partial;
                }

                while (qhat != 0 && rhat <= ulong.MaxValue)
                {
                    if ((UInt128)qhat * vNext <= new UInt128((ulong)rhat, un[j + divLen - 2]))
                    {
                        break;
                    }

                    qhat--;
                    rhat += vHigh;
                }

                ulong borrow = 0;
                ulong mulCarry = 0;
                for (var i = 0; i < divLen; i++)
                {
                    var high = Math.BigMul(qhat, vn[i], out var low);
                    var p = low + mulCarry;
                    if (p < low)
                    {
                        high++;
                    }

                    var cur = un[j + i];
                    var diff = cur - p;
                    var b1 = cur < p ? 1UL : 0UL;
                    var diff2 = diff - borrow;
                    var b2 = diff < borrow ? 1UL : 0UL;
                    un[j + i] = diff2;
                    borrow = b1 | b2;
                    mulCarry = high;
                }

                var topCur = un[j + divLen];
                var topSub = mulCarry;
                var topDiff = topCur - topSub;
                var topBorrow = topCur < topSub ? 1UL : 0UL;
                var topDiff2 = topDiff - borrow;
                topBorrow |= topDiff < borrow ? 1UL : 0UL;
                un[j + divLen] = topDiff2;

                if (topBorrow != 0)
                {
                    qhat--;
                    ulong carry = 0;
                    for (var i = 0; i < divLen; i++)
                    {
                        var a = un[j + i];
                        var sum = a + vn[i];
                        var c1 = sum < a ? 1UL : 0UL;
                        var sum2 = sum + carry;
                        var c2 = sum2 < sum ? 1UL : 0UL;
                        un[j + i] = sum2;
                        carry = c1 | c2;
                    }

                    un[j + divLen] += carry;
                }

                quotient[j] = qhat;
            }

            ShiftRight(un, divLen, shift, numerator);
            numerator[divLen..numLen].Clear();
            remainderLen = Normalize(numerator[..divLen]);
            return Normalize(quotient[..qLen]);
        }
    }

    private static void ShiftLeft(ReadOnlySpan<ulong> source, int len, int shift, Span<ulong> destination)
    {
        unchecked
        {
            if (shift == 0)
            {
                source[..len].CopyTo(destination);
                return;
            }

            for (var i = len - 1; i > 0; i--)
            {
                destination[i] = (source[i] << shift) | (source[i - 1] >> (64 - shift));
            }

            destination[0] = source[0] << shift;
        }
    }

    private static void ShiftRight(ReadOnlySpan<ulong> source, int len, int shift, Span<ulong> destination)
    {
        unchecked
        {
            if (shift == 0)
            {
                source[..len].CopyTo(destination);
                return;
            }

            for (var i = 0; i < len - 1; i++)
            {
                destination[i] = (source[i] >> shift) | (source[i + 1] << (64 - shift));
            }

            destination[len - 1] = source[len - 1] >> shift;
        }
    }

    private static Divisor[] BuildDivisors(ulong[] values)
    {
        var divisors = new Divisor[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            divisors[i] = Divisor.For(values[i]);
        }

        return divisors;
    }

    /// <summary>
    /// A divisor in the form <see cref="DivRem2By1"/> needs it: shifted so that its most
    /// significant bit is set, with the shift that produced it and the reciprocal of the result.
    /// </summary>
    internal readonly record struct Divisor(ulong Normalized, ulong Reciprocal, int Shift)
    {
        internal static Divisor For(ulong value)
        {
            Debug.Assert(value != 0, "divisor must be non-zero");

            var shift = BitOperations.LeadingZeroCount(value);
            var normalized = value << shift;

            return new Divisor(normalized, Words.Reciprocal(normalized), shift);
        }
    }
}
