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

    /// <summary>The value <see cref="Poison"/> writes.</summary>
    /// <remarks>
    /// Every <see cref="ulong"/> is a valid magnitude word, so this cannot be an invalid value and
    /// is not one. It is a sentinel: recognisable on sight in a debugger, and far enough from
    /// anything the suite generates that a value carrying it is wrong by an obvious margin rather
    /// than by a digit.
    /// </remarks>
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

    // Prepared once, at type initialisation, so no division pays for a reciprocal it could be
    // handed. Static readonly arrays behind span-returning properties, never a collection
    // expression in an expression-bodied property: that form is not cached and allocates on every
    // access, which once made the "never allocates" claim false. Both are filled end to end -
    // under 1.2 kB together - so there is no question of which indices are hot.
    private static readonly Divisor[] Pow10DivisorValues = BuildDivisors(Pow10Values);

    private static readonly Divisor[] Pow5DivisorValues = BuildDivisors(Pow5Values);

    /// <summary>The powers of ten, each prepared for <see cref="DivRem2By1"/>.</summary>
    internal static ReadOnlySpan<Divisor> Pow10Divisors => Pow10DivisorValues;

    /// <summary>
    /// <see cref="TenPow19"/> prepared for <see cref="DivRem2By1"/>, the largest power of ten a
    /// word holds. Named rather than indexed, so a caller peeling nineteen digits at a time cannot
    /// pick up whichever entry another constant happens to point at.
    /// </summary>
    internal static ref readonly Divisor TenPow19Divisor => ref Pow10DivisorValues[MaxZerosPerPass];

    /// <summary>The powers of five, each prepared for <see cref="DivRem2By1"/>.</summary>
    internal static ReadOnlySpan<Divisor> Pow5Divisors => Pow5DivisorValues;

    /// <summary>
    /// Counts the trailing decimal zeros of a magnitude, up to <paramref name="limit"/> and to at
    /// most <see cref="MaxZerosPerPass"/>, without dividing it.
    /// </summary>
    /// <remarks>
    /// The smaller of the twos and the fives, neither of which needs a division: the twos are a
    /// trailing-zero count on the low non-zero word, and the fives come from one remainder pass by
    /// 5^27 - zero means at least 27, past the cap, and otherwise the fives of the remainder are
    /// the fives of the value and fit one word.
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

        // The twos and the caller's limit already bound the answer, so a value with one trailing
        // zero pays one test rather than the search for nineteen.
        var cap = Math.Min(twos, limit);
        var remainder = RemSmall(value, length, Pow5Divisors[MaxFivesPerWord]);
        return remainder == 0 ? cap : CountFives(remainder, cap);
    }

    /// <summary>
    /// Reports whether a magnitude is a power of two times a power of five, and if it is, returns
    /// the number of decimal places at which a division by it comes out exactly.
    /// </summary>
    /// <remarks>
    /// a / b is exact at max(x, y) places when b is 2^x times 5^y, since a * 10^max(x,y) / b is
    /// then a * 2^(max-x) * 5^(max-y). Knowing it up front lets a division lift its dividend by a
    /// few places instead of by the whole mantissa. One shift and one remainder pass, on which any
    /// other prime factor is rejected.
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
    /// The quotient always lies between 2^64 and 2^65, so subtracting 2^64 is keeping the low 64
    /// bits and the result fits one word. Written as the plain division rather than a Newton
    /// refinement because it runs once per divisor, never once per word - that is the loop that
    /// had to be made cheap.
    /// </remarks>
    /// <param name="divisorNormalized">The divisor, whose most significant bit must be set.</param>
    /// <returns>The reciprocal to pass to <see cref="DivRem2By1"/> alongside that divisor.</returns>
    internal static ulong Reciprocal(ulong divisorNormalized)
    {
        Debug.Assert(divisorNormalized >> 63 != 0, "divisor must be normalized");

        // The conversion discards the high bit, which is the subtraction of 2^64. A Debug build
        // compiles with CheckForOverflowUnderflow, where discarding it throws instead.
        return unchecked((ulong)(UInt128.MaxValue / divisorNormalized));
    }

    /// <summary>Divides a 128-bit value by a single normalised word.</summary>
    /// <remarks>
    /// The estimate-and-correct division of Moller and Granlund. It replaces a
    /// <see cref="UInt128"/> over a <see cref="ulong"/>, which the runtime lowers to a hardware
    /// divide only while the high half is zero. Every step is modulo 2^64 by design - the
    /// corrections read the wraparound of a subtraction that went too far - so the body is
    /// <c>unchecked</c> and must stay so, or a Debug build throws on the first correction.
    /// </remarks>
    /// <param name="high">
    /// The high half of the dividend, which must be below <paramref name="divisorNormalized"/>. The
    /// quotient is a single word, so a caller whose high half reaches the divisor has to decide that
    /// case for itself before calling.
    /// </param>
    /// <param name="low">The low half of the dividend.</param>
    /// <param name="divisorNormalized">The divisor, whose most significant bit must be set.</param>
    /// <param name="reciprocal">
    /// The value <see cref="Reciprocal"/> returns for that divisor. Passing one belonging to another
    /// divisor produces a wrong quotient rather than a failure.
    /// </param>
    /// <param name="remainder">Receives the remainder, which is below the divisor.</param>
    /// <returns>The quotient.</returns>
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
            // (reciprocal * high) + the dividend + 2^64: its high half is the quotient to within
            // one either way, and the remainder below is what tells the two corrections apart.
            var estimate = Math.BigMul(reciprocal, high, out var estimateLow);
            estimateLow += low;
            if (estimateLow < low)
            {
                estimate++;
            }

            estimate += high;
            estimate++;

            var rest = low - (estimate * divisorNormalized);

            // Wrapping past the low half of the estimate is what an overshoot looks like from here.
            if (rest > estimateLow)
            {
                estimate--;
                rest += divisorNormalized;
            }

            // Repairs an estimate one short: rarer, about one in a thousand on random operands, and
            // the only step that moves the quotient up, so nothing else covers for it.
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
    /// The loop of <see cref="DivideBySmall"/> without the quotient stores. Written out rather
    /// than shared: sharing would put a "is there a quotient to write" test inside the word loop,
    /// on the one path that exists to answer a divisibility question without producing one.
    /// </remarks>
    /// <param name="value">The magnitude.</param>
    /// <param name="length">The number of significant words in <paramref name="value"/>.</param>
    /// <param name="divisor">The prepared divisor.</param>
    /// <returns>The remainder.</returns>
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
        var bound = Math.Min(cap, MaxFivesPerWord);

        // The cap is usually the answer: a value widened to a column's scale carries as many fives
        // as the zeros it was given, so one test settles it and the search below never runs.
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
    /// Words beyond a buffer's length are not readable state and are not zeroed on any hot path.
    /// A helper that read past its length would find whatever zero the runtime left there, pass
    /// the whole suite, and be wrong only for operands wide enough to reach those words; this
    /// takes the zero away in Debug so it fails instead, and compiles to nothing in Release.
    /// The write-side half is <see cref="DivRem"/>, which fills every quotient word it reports and
    /// normalises the numerator down to its own, which is what lets <c>Divide</c> reuse one pair
    /// of buffers three times without clearing them.
    /// </remarks>
    /// <param name="value">The words to poison.</param>
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

    /// <summary>Divides a magnitude by a single word in place.</summary>
    /// <remarks>
    /// The divisor arrives prepared because preparing costs a division of its own: the callers
    /// here divide by one of the two tables, and one holding an arbitrary divisor prepares it once
    /// with <see cref="Divisor.For"/> rather than once per word.
    /// </remarks>
    /// <param name="acc">The magnitude, replaced by the quotient.</param>
    /// <param name="accLen">The number of significant words in <paramref name="acc"/>.</param>
    /// <param name="divisor">The prepared divisor.</param>
    /// <param name="remainder">Receives the remainder.</param>
    /// <returns>The number of significant words in the quotient.</returns>
    internal static int DivRemSmall(Span<ulong> acc, int accLen, in Divisor divisor, out ulong remainder)
    {
        remainder = DivideBySmall(acc, accLen, divisor, acc);

        return Normalize(acc[..accLen]);
    }

    /// <summary>Divides a magnitude by a single prepared word, writing the quotient word by word.</summary>
    /// <remarks>
    /// The one place the shifted loop is written, so a correction to the shifting cannot reach one
    /// caller and miss the other. Words go in shifted by the divisor's normalising shift and the
    /// remainder is shifted back on the way out; the quotient needs no undoing, both sides having
    /// been scaled alike. <paramref name="quotient"/> may be <paramref name="source"/> itself -
    /// the loop reads the word below the one it writes - but no other overlap is safe.
    /// </remarks>
    /// <param name="source">The magnitude.</param>
    /// <param name="length">The number of significant words in <paramref name="source"/>.</param>
    /// <param name="divisor">The prepared divisor.</param>
    /// <param name="quotient">Receives the quotient, and may be <paramref name="source"/> itself.</param>
    /// <returns>The remainder.</returns>
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

            // Shifting the magnitude left can push bits out of its top word. They are not lost:
            // they are the first dividend's high half, which is zero when nothing was shifted.
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
        while (remaining > 0)
        {
            var chunk = Math.Min(remaining, 19);
            accLen = DivRemSmall(acc, accLen, Pow10Divisors[chunk], out var rem);
            sticky |= rem != 0;
            remaining -= chunk;
        }

        accLen = DivRemSmall(acc, accLen, Pow10Divisors[1], out var lastDigit);

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
    /// Every quotient word this reports is one it wrote, and it reads none above that, so the
    /// caller does not have to clear the quotient buffer first and nothing above the returned
    /// length means anything. The numerator is consumed: the remainder is written over its low
    /// words and the rest are cleared.
    /// </remarks>
    /// <param name="numerator">The dividend, overwritten with the remainder.</param>
    /// <param name="numLen">The number of significant words in <paramref name="numerator"/>.</param>
    /// <param name="divisor">The divisor, normalized and non-zero.</param>
    /// <param name="divLen">The number of significant words in <paramref name="divisor"/>.</param>
    /// <param name="quotient">Receives the quotient. It need not be cleared.</param>
    /// <param name="remainderLen">The number of significant words of the remainder.</param>
    /// <returns>The number of significant words in <paramref name="quotient"/>.</returns>
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
                // One word over one word is a hardware divide and nothing else. Preparing the
                // divisor costs a 128-bit division with nothing to amortise it over: 1.1x on
                // net10.0 and 6.9x on net8.0. From two words up it wins 2x to 20x, hence the cut.
                var single = numLen == 0 ? 0UL : numerator[0];
                rem = single % divisor[0];
                if (numLen == 1)
                {
                    quotient[0] = single / divisor[0];
                }
            }
            else
            {
                // The divisor is the caller's, so it is prepared here rather than read from a
                // table: once per call, amortised over every word of the dividend.
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

            // The divisor was normalised above, which is exactly the precondition the primitive
            // wants, so one reciprocal serves every quotient word of this division.
            var vReciprocal = Reciprocal(vHigh);

            for (var j = qLen - 1; j >= 0; j--)
            {
                var topHigh = un[j + divLen];
                var topLow = un[j + divLen - 1];
                ulong qhat;
                UInt128 rhat;

                // The true quotient is 2^64 exactly when the remainder's leading word equals the
                // divisor's, which no single word holds, so the primitive cannot answer it. The
                // partial remainder stays a UInt128: truncated to a ulong it looks small and the
                // correction below fires on an estimate that was already right.
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
    /// A divisor already in the form <see cref="DivRem2By1"/> needs it: shifted so that its most
    /// significant bit is set, with the shift that produced it and the reciprocal of the result.
    /// </summary>
    /// <remarks>
    /// The shift is carried here rather than at the call site because the dividend is fed in
    /// shifted by the same amount and the remainder comes back needing it undone.
    /// </remarks>
    /// <param name="Normalized">The divisor shifted so that its most significant bit is set.</param>
    /// <param name="Reciprocal">The reciprocal of <paramref name="Normalized"/>.</param>
    /// <param name="Shift">The number of places the divisor was shifted left, from 0 to 63.</param>
    internal readonly record struct Divisor(ulong Normalized, ulong Reciprocal, int Shift)
    {
        /// <summary>Prepares a divisor.</summary>
        /// <param name="value">The divisor, which must not be zero.</param>
        /// <returns>The prepared divisor.</returns>
        internal static Divisor For(ulong value)
        {
            Debug.Assert(value != 0, "divisor must be non-zero");

            var shift = BitOperations.LeadingZeroCount(value);
            var normalized = value << shift;

            return new Divisor(normalized, Words.Reciprocal(normalized), shift);
        }
    }
}
