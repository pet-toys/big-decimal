using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// A fixed-width decimal value: a 256-bit unsigned magnitude with a sign and a decimal scale,
/// denoting <c>(-1)^sign * magnitude * 10^(-scale)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The magnitude spans 0 to 2^256-1 and the scale 0 to <see cref="MaxScale"/>. Every value of at
/// most 77 significant digits is therefore representable, the largest representable magnitude has
/// 78 digits, and the range runs from 1e-255 to approximately 1.157e77. That covers every
/// ClickHouse <c>Decimal32</c>, <c>Decimal64</c>, <c>Decimal128</c> and <c>Decimal256</c> value,
/// and every PostgreSQL <c>numeric(p, s)</c> up to 77 digits of precision.
/// </para>
/// <para>
/// Semantics follow <see cref="decimal"/> inside <see cref="decimal"/>'s own domain: trailing
/// zeros are preserved, equality is numeric so that <c>1.0</c> equals <c>1.00</c>, and fractional
/// digits that do not fit are rounded to nearest with ties to even. The 256-bit magnitude is the
/// only hard limit: when a value's significant digits do not fit, the scale is reduced - the
/// fraction rounded away - as far as needed, and only an integer part that still does not fit
/// throws <see cref="OverflowException"/>. There is no wrapping mode and no silent truncation.
/// Zero is the one deliberate divergence from <see cref="decimal"/>: it never carries a sign.
/// </para>
/// <para>
/// A value occupies four 64-bit magnitude words in little-endian order followed by a packed
/// 32-bit field: the sign in bit 31, the scale in bits 0 through 7, and the non-finite encoding
/// in bits 8 and 9 - bit 8 marks a value that is not finite, bit 9 tells <see cref="NaN"/> from
/// an infinity, and the sign bit says which infinity. A non-finite value carries scale 0 and
/// four zero magnitude words. That makes a value 40 bytes wide against <see cref="decimal"/>'s
/// 16, passed by value; nothing reaches the heap, and a division, a parse or a
/// <see cref="ToString()"/> takes between one and one and a half kilobytes of stack.
/// </para>
/// </remarks>
[JsonConverter(typeof(BigDecimalJsonConverter))]
[TypeConverter(typeof(BigDecimalTypeConverter))]
public readonly partial struct BigDecimal
{
    internal const int WordCount = 4;

    internal const int MaxDigits = 77;

    /// <summary>
    /// The largest scale a value can carry: 255 fractional digits, which puts the floor of the
    /// range at 1e-255.
    /// </summary>
    public const int MaxScale = 255;

    private const uint SignMask = 0x8000_0000u;
    private const uint ScaleMask = 0x0000_00FFu;

    // Never set on a finite value, so default is still Zero.
    private const uint NonFiniteMask = 0x0000_0100u;
    private const uint NaNMask = 0x0000_0200u;

    private readonly ulong _l0;
    private readonly ulong _l1;
    private readonly ulong _l2;
    private readonly ulong _l3;
    private readonly uint _flags;

    private BigDecimal(ulong l0, ulong l1, ulong l2, ulong l3, bool isNegative, int scale)
    {
        Debug.Assert((uint)scale <= MaxScale, "scale must be within 0..255");
        _l0 = l0;
        _l1 = l1;
        _l2 = l2;
        _l3 = l3;

        var negative = isNegative && (l0 | l1 | l2 | l3) != 0;
        _flags = (uint)scale | (negative ? SignMask : 0u);
    }

    private BigDecimal(uint nonFiniteFlags)
    {
        Debug.Assert((nonFiniteFlags & NonFiniteMask) != 0, "the encoding bit must be set");
        Debug.Assert((nonFiniteFlags & ScaleMask) == 0, "a non-finite value carries scale 0");
        _l0 = 0;
        _l1 = 0;
        _l2 = 0;
        _l3 = 0;
        _flags = nonFiniteFlags;
    }

    /// <summary>The value zero, at scale 0.</summary>
    public static BigDecimal Zero => default;

    /// <summary>The value one, at scale 0.</summary>
    public static BigDecimal One => new(1, 0, 0, 0, false, 0);

    /// <summary>The value minus one, at scale 0.</summary>
    public static BigDecimal NegativeOne => new(1, 0, 0, 0, true, 0);

    /// <summary>The value that is not a number.</summary>
    /// <remarks>
    /// There is one NaN, with no payload and no sign. It exists because a PostgreSQL
    /// <c>numeric</c> column can hold one; no operation over finite operands yields it.
    /// </remarks>
    public static BigDecimal NaN => new(NonFiniteMask | NaNMask);

    /// <summary>Positive infinity.</summary>
    /// <remarks>
    /// Reachable from a PostgreSQL <c>numeric</c> column, from parsing and from a conversion,
    /// never from arithmetic over finite operands: division by zero throws
    /// <see cref="DivideByZeroException"/> and an integer part that does not fit throws
    /// <see cref="OverflowException"/>, as they do for <see cref="decimal"/>.
    /// </remarks>
    public static BigDecimal PositiveInfinity => new(NonFiniteMask);

    /// <summary>Negative infinity.</summary>
    /// <remarks>The negation of <see cref="PositiveInfinity"/>, reachable the same ways.</remarks>
    public static BigDecimal NegativeInfinity => new(NonFiniteMask | SignMask);

    /// <summary>
    /// The largest representable value, 2^256-1 at scale 0 - a 78-digit integer.
    /// </summary>
    public static BigDecimal MaxValue => new(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, false, 0);

    /// <summary>
    /// The smallest representable value, the negation of <see cref="MaxValue"/>.
    /// </summary>
    public static BigDecimal MinValue => new(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, true, 0);

    /// <summary>
    /// The number of fractional digits this value carries, from 0 to <see cref="MaxScale"/>.
    /// </summary>
    /// <remarks>
    /// The scale is part of the value's representation, not of its numeric value: <c>1.0</c> and
    /// <c>1.00</c> are equal and hash alike but report 1 and 2 here, and the difference survives
    /// formatting and the database wire formats. A non-finite value reports 0.
    /// </remarks>
    public int Scale => (int)(_flags & ScaleMask);

    /// <summary>
    /// Whether the value is less than zero. Always <see langword="false"/> for zero, which never
    /// carries a sign.
    /// </summary>
    public bool IsNegative => (_flags & SignMask) != 0;

    /// <summary>Whether the magnitude is zero, whatever the scale.</summary>
    /// <remarks>
    /// A non-finite value shares its four zero words with zero, so the encoding is tested too.
    /// </remarks>
    public bool IsZero => (_l0 | _l1 | _l2 | _l3) == 0 && !IsNonFinite;

    /// <summary>Whether the value is <see cref="NaN"/>.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is NaN.</returns>
    public static bool IsNaN(BigDecimal value) => (value._flags & NaNMask) != 0;

    /// <summary>Whether the value is either infinity.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is positive or negative infinity.</returns>
    public static bool IsInfinity(BigDecimal value) =>
        (value._flags & (NonFiniteMask | NaNMask)) == NonFiniteMask;

    /// <summary>Whether the value is <see cref="PositiveInfinity"/>.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is positive infinity.</returns>
    public static bool IsPositiveInfinity(BigDecimal value) =>
        (value._flags & (NonFiniteMask | NaNMask | SignMask)) == NonFiniteMask;

    /// <summary>Whether the value is <see cref="NegativeInfinity"/>.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is negative infinity.</returns>
    public static bool IsNegativeInfinity(BigDecimal value) =>
        (value._flags & (NonFiniteMask | NaNMask | SignMask)) == (NonFiniteMask | SignMask);

    /// <summary>Whether the value is neither NaN nor an infinity.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for every value but the three non-finite ones.</returns>
    public static bool IsFinite(BigDecimal value) => (value._flags & NonFiniteMask) == 0;

    /// <summary>
    /// -1 for a negative value, 0 for zero, 1 for a positive value; 1 and -1 for the two
    /// infinities.
    /// </summary>
    /// <exception cref="ArithmeticException">
    /// The value is <see cref="NaN"/>, which has no sign, as <see cref="Math.Sign(double)"/>
    /// throws for <see cref="double.NaN"/>.
    /// </exception>
    public int Sign
    {
        get
        {
            if (IsNaN(this))
            {
                ThrowNaNHasNoSign();
            }

            return FiniteSign;
        }
    }

    internal bool IsNonFinite => (_flags & NonFiniteMask) != 0;

    // The sign without the NaN check, for callers that have already answered the non-finite cases.
    internal int FiniteSign => IsZero ? 0 : (IsNegative ? -1 : 1);

    /// <summary>
    /// The number of decimal digits in the unscaled mantissa, from 1 upwards for a finite
    /// value and 0 for a non-finite one.
    /// </summary>
    /// <remarks>
    /// Counted on the mantissa, trailing zeros included, as a database column counts stored
    /// digits: <c>1.00</c> reports 3, <c>1</c> reports 1, and <c>0.001</c> reports 1 because the
    /// scale carries the leading zeros. Together with <see cref="Scale"/> this decides whether a
    /// value fits a <c>numeric(p,s)</c> or a <c>Decimal128(S)</c> column. Zero reports 1 at every
    /// scale, and <see cref="MaxValue"/> reports 78.
    /// </remarks>
    public int Precision
    {
        get
        {
            if (IsNonFinite)
            {
                return 0;
            }

            Span<ulong> magnitude = stackalloc ulong[WordCount];
            var length = CopyMagnitude(magnitude);
            return Words.DecimalDigitCount(magnitude, length);
        }
    }

    /// <summary>
    /// Builds a value from a little-endian magnitude, a sign and a scale.
    /// </summary>
    /// <param name="words">
    /// The magnitude, least significant word first. Longer than four words is accepted as long as
    /// every word above the fourth is zero; an empty span is zero.
    /// </param>
    /// <param name="isNegative">The sign to apply. Ignored when the magnitude is zero.</param>
    /// <param name="scale">The scale to apply, from 0 to <see cref="MaxScale"/> inclusive.</param>
    /// <returns>The value the arguments describe.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is negative or greater than <see cref="MaxScale"/>.</exception>
    /// <exception cref="OverflowException"><paramref name="words"/> carries a magnitude wider than four words.</exception>
    public static BigDecimal FromWords(ReadOnlySpan<ulong> words, bool isNegative, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, MaxScale);

        var len = Words.Normalize(words);
        if (len > WordCount)
        {
            ThrowMantissaOverflow();
        }

        return new BigDecimal(
            len > 0 ? words[0] : 0,
            len > 1 ? words[1] : 0,
            len > 2 ? words[2] : 0,
            len > 3 ? words[3] : 0,
            isNegative,
            scale);
    }

    /// <summary>
    /// Copies the magnitude, sign and scale out of this value.
    /// </summary>
    /// <param name="destination">
    /// Receives the magnitude least significant word first. Must hold at least four words, all of
    /// which are written: those above the significant ones are set to zero.
    /// </param>
    /// <param name="isNegative">Receives the sign. Always <see langword="false"/> for zero.</param>
    /// <param name="scale">Receives the scale.</param>
    /// <returns>The number of significant words written, from 0 for zero to 4.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is shorter than four words.</exception>
    /// <exception cref="InvalidOperationException">
    /// The value is NaN or an infinity, which has no magnitude to copy. Test
    /// <see cref="IsFinite"/> first.
    /// </exception>
    public int GetWords(Span<ulong> destination, out bool isNegative, out int scale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, WordCount);

        if (IsNonFinite)
        {
            ThrowNonFiniteHasNoMagnitude();
        }

        destination[0] = _l0;
        destination[1] = _l1;
        destination[2] = _l2;
        destination[3] = _l3;
        isNegative = IsNegative;
        scale = Scale;
        return Words.Normalize(destination[..WordCount]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int CopyMagnitude(Span<ulong> destination)
    {
        destination[0] = _l0;
        destination[1] = _l1;
        destination[2] = _l2;
        destination[3] = _l3;

        // Nothing reads above the length a helper is given, so the rest is not zeroed; Poison
        // makes a Debug build fail if that ever stops being true.
        Words.Poison(destination[WordCount..]);

        return Words.Normalize(destination[..WordCount]);
    }

    internal static BigDecimal Pack(Span<ulong> magnitude, int length, bool isNegative, int scale)
    {
        if (!TryPack(magnitude, length, isNegative, scale, out var result))
        {
            ThrowMantissaOverflow();
        }

        return result;
    }

    /// <summary>Packs a magnitude and a scale into a value, reporting overflow rather than throwing.</summary>
    /// <remarks>
    /// For <c>TryParse</c>, which allocates nothing and so cannot catch. The magnitude is consumed
    /// either way.
    /// </remarks>
    internal static bool TryPack(Span<ulong> magnitude, int length, bool isNegative, int scale, out BigDecimal result)
    {
        result = default;
        length = Words.Normalize(magnitude[..Math.Max(length, 0)]);
        if (length == 0)
        {
            result = new BigDecimal(0, 0, 0, 0, false, Math.Clamp(scale, 0, MaxScale));
            return true;
        }

        if (scale < 0)
        {
            var power = -scale;
            if (Words.DecimalDigitCount(magnitude, length) + power > (magnitude.Length - 1) * 19)
            {
                return false;
            }

            length = Words.ScaleUp(magnitude, length, power);
            if (length > WordCount)
            {
                return false;
            }

            scale = 0;
        }

        if (!TryReduce(magnitude, ref length, ref scale, WordCount, MaxDigits, MaxScale, isNegative, allowNegativeScale: false))
        {
            return false;
        }

        result = new BigDecimal(
            length > 0 ? magnitude[0] : 0,
            length > 1 ? magnitude[1] : 0,
            length > 2 ? magnitude[2] : 0,
            length > 3 ? magnitude[3] : 0,
            isNegative,
            scale);
        return true;
    }

    /// <summary>Gives up fractional digits until a magnitude and its scale fit a stated width.</summary>
    /// <remarks>
    /// The reduction rule of the type, in one place: excess digits are fractional, they round half
    /// to even, and having none left to give is a failure rather than a truncated integer part.
    /// <see cref="TryPack"/> calls it at the mantissa's width and <see cref="Pow"/> at its
    /// accumulator's, where <paramref name="allowNegativeScale"/> lets a working value go below
    /// scale 0 instead of failing. Every value no wider than <paramref name="maxWords"/> has at
    /// most <paramref name="maxDigits"/> + 1 digits.
    /// </remarks>
    private static bool TryReduce(
        Span<ulong> magnitude,
        ref int length,
        ref int scale,
        int maxWords,
        int maxDigits,
        int maxScale,
        bool isNegative,
        bool allowNegativeScale)
    {
        if (length == 0)
        {
            scale = Math.Min(scale, maxScale);
            return true;
        }

        while (length > maxWords || scale > maxScale)
        {
            var excess = Math.Max(scale - maxScale, 0);
            if (length > maxWords)
            {
                excess = Math.Max(excess, Words.DecimalDigitCount(magnitude, length) - maxDigits);
            }

            excess = Math.Max(excess, 1);
            if (!allowNegativeScale)
            {
                if (scale <= 0)
                {
                    return false;
                }

                excess = Math.Min(excess, scale);
            }

            length = Words.DivPow10Round(magnitude, length, excess, isNegative, MidpointRounding.ToEven);
            scale -= excess;
        }

        return true;
    }

    [DoesNotReturn]
    internal static void ThrowMantissaOverflow() =>
        throw new OverflowException("Value was either too large or too small for a BigDecimal.");

    [DoesNotReturn]
    internal static void ThrowNaNHasNoSign() =>
        throw new ArithmeticException("Function does not accept Not-a-Number values.");

    // Not an OverflowException: the value has no magnitude at all, and the fix is to test IsFinite.
    [DoesNotReturn]
    internal static void ThrowNonFiniteHasNoMagnitude() =>
        throw new InvalidOperationException("NaN and the infinities have no magnitude. Test IsFinite first.");
}
