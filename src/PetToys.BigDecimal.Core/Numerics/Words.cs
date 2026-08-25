using System;
using System.Diagnostics;
using System.Numerics;

namespace PetToys.BigDecimal.Numerics;

internal static class Words
{
    internal const ulong TenPow19 = 10_000_000_000_000_000_000UL;

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
                var qhat = top / vHigh > ulong.MaxValue ? ulong.MaxValue : (ulong)(top / vHigh);
                var rhat = (ulong)(top - ((UInt128)qhat * vHigh));

                while (true)
                {
                    if (qhat == 0)
                    {
                        break;
                    }

                    var lhs = (UInt128)qhat * vNext;
                    var rhs = new UInt128(rhat, un[j + divLen - 2]);
                    if (lhs <= rhs)
                    {
                        break;
                    }

                    qhat--;
                    var newRhat = rhat + vHigh;
                    if (newRhat < rhat)
                    {
                        break;
                    }

                    rhat = newRhat;
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
