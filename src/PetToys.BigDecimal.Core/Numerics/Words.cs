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

    /// <summary>
    /// Counts the trailing decimal zeros of a magnitude, up to <paramref name="limit"/> and to at
    /// most <see cref="MaxZerosPerPass"/>, without dividing it.
    /// </summary>
    /// <remarks>
    /// The count is the smaller of the twos and the fives in the factorisation, and neither needs a
    /// division of the magnitude. The twos are a trailing-zero count on its low non-zero word, so a
    /// value with none is answered without touching the rest of it. The fives come from a single
    /// remainder pass by 5^27, the largest power of five a word holds: when that remainder is zero
    /// the value carries at least 27 fives, which is past the cap, and when it is not, the fives of
    /// the value are the fives of the remainder, countable on one word.
    /// </remarks>
    /// <param name="value">The magnitude.</param>
    /// <param name="length">The number of significant words in <paramref name="value"/>.</param>
    /// <param name="limit">The largest count the caller can use.</param>
    /// <returns>The number of trailing decimal zeros, capped by <paramref name="limit"/>.</returns>
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

        // The twos and the caller's limit already bound the answer, so the fives are only counted
        // as far as that bound: a value with one trailing zero pays one test, not the search for
        // nineteen.
        var cap = Math.Min(twos, limit);
        var remainder = RemSmall(value, length, Pow5Values[MaxFivesPerWord]);
        return remainder == 0 ? cap : CountFives(remainder, cap);
    }

    /// <summary>
    /// Reports whether a magnitude is a power of two times a power of five, and if it is, returns
    /// the number of decimal places at which a division by it comes out exactly.
    /// </summary>
    /// <remarks>
    /// A quotient a / b is exact in decimal at max(x, y) places when b is 2^x times 5^y, because
    /// a * 10^max(x,y) / b is then a * 2^(max-x) * 5^(max-y). Knowing that up front is what lets a
    /// division lift its dividend by a few places instead of by everything the mantissa holds. The
    /// twos come out with a shift and the fives with one remainder pass in the common case, and a
    /// divisor carrying any other factor is rejected on that same pass.
    /// </remarks>
    /// <param name="value">The divisor's magnitude. It is consumed, so pass a copy.</param>
    /// <param name="length">The number of significant words in <paramref name="value"/>.</param>
    /// <param name="places">The number of decimal places at which a division by it is exact.</param>
    /// <returns><see langword="true"/> when the magnitude is 2^x times 5^y.</returns>
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
            var remainder = RemSmall(value, length, Pow5Values[MaxFivesPerWord]);
            if (remainder == 0)
            {
                length = DivRemSmall(value, length, Pow5Values[MaxFivesPerWord], out _);
                fives += MaxFivesPerWord;
                continue;
            }

            var extra = CountFives(remainder, MaxFivesPerWord);
            if (extra > 0)
            {
                length = DivRemSmall(value, length, Pow5Values[extra], out _);
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

    /// <summary>Returns the remainder of a magnitude divided by a single word, leaving it unchanged.</summary>
    /// <param name="value">The magnitude.</param>
    /// <param name="length">The number of significant words in <paramref name="value"/>.</param>
    /// <param name="divisor">The divisor, which must not be zero.</param>
    /// <returns>The remainder.</returns>
    internal static ulong RemSmall(ReadOnlySpan<ulong> value, int length, ulong divisor)
    {
        Debug.Assert(divisor != 0, "divisor must be non-zero");

        unchecked
        {
            ulong rem = 0;
            for (var i = length - 1; i >= 0; i--)
            {
                rem = (ulong)(new UInt128(rem, value[i]) % divisor);
            }

            return rem;
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

    /// <summary>Counts the factors of five in a single word, up to a cap.</summary>
    /// <remarks>
    /// A binary search over the powers of five a word holds, not a division per five. This is
    /// called on values that end in many fives by construction, so counting them one at a time put
    /// a cost proportional to the trailing zeros straight back into a strip that had just had one
    /// taken out of it. The cap is the caller's own bound on the answer, and it shortens the search
    /// as well as the result.
    /// </remarks>
    private static int CountFives(ulong value, int cap)
    {
        var low = 0;
        var high = Math.Min(cap, MaxFivesPerWord);

        // The cap is usually the answer. A value widened to a column's scale carries exactly as
        // many fives as the zeros it was given, and the twos that set the cap came from the same
        // widening, so one test settles it and the search below never runs.
        if (high <= 0 || value % Pow5Values[high] == 0)
        {
            return high;
        }

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

    internal static int DivRemSmall(Span<ulong> acc, int accLen, ulong divisor, out ulong remainder)
    {
        unchecked
        {
            ulong rem = 0;
            for (var i = accLen - 1; i >= 0; i--)
            {
                var cur = new UInt128(rem, acc[i]);
                acc[i] = (ulong)(cur / divisor);
                rem = (ulong)(cur % divisor);
            }

            remainder = rem;
            return Normalize(acc[..accLen]);
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
        while (remaining > 0)
        {
            var chunk = Math.Min(remaining, 19);
            accLen = DivRemSmall(acc, accLen, Pow10[chunk], out var rem);
            sticky |= rem != 0;
            remaining -= chunk;
        }

        accLen = DivRemSmall(acc, accLen, 10, out var lastDigit);

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
        power.Clear();
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
            var d = divisor[0];
            unchecked
            {
                ulong rem = 0;
                for (var i = numLen - 1; i >= 0; i--)
                {
                    var cur = new UInt128(rem, numerator[i]);
                    quotient[i] = (ulong)(cur / d);
                    rem = (ulong)(cur % d);
                }

                numerator[..numLen].Clear();
                numerator[0] = rem;
                remainderLen = rem == 0 ? 0 : 1;
                return Normalize(quotient[..numLen]);
            }
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

            for (var j = qLen - 1; j >= 0; j--)
            {
                var top = new UInt128(un[j + divLen], un[j + divLen - 1]);
                var wide = top / vHigh;
                var qhat = wide > ulong.MaxValue ? ulong.MaxValue : (ulong)wide;

                // The estimate saturates exactly when the running remainder's leading word equals
                // the divisor's, and its partial remainder then needs more than 64 bits. It stays
                // wide so that the correction below reads the remainder it was given: truncated to
                // a ulong it looks small, the correction fires on an estimate that was already
                // right, and the algorithm has no repair for one that came out too small. The
                // estimate itself stays a ulong, so both products below are widening 64-by-64
                // multiplications rather than the far dearer 128-bit kind.
                var rhat = top - ((UInt128)qhat * vHigh);

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
}
