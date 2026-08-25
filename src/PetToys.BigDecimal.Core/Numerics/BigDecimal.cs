using System;
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
/// only hard limit: when a value's significant digits do not fit, the scale is reduced — the
/// fraction rounded away — as far as needed, and only an integer part that still does not fit
/// throws <see cref="OverflowException"/>. There is no wrapping mode and no silent truncation.
/// Zero is the one deliberate divergence from <see cref="decimal"/>: it never carries a sign.
/// </para>
/// <para>
/// A value occupies four 64-bit magnitude words in little-endian order followed by a packed
/// 32-bit field holding the sign in bit 31 and the scale in bits 0 through 7. Bits 8 through 30
/// are reserved, are zero in every value the type produces, and are held for the NaN and infinity
/// encodings a later version will add. Until then NaN and the infinities are not representable,
/// which makes them the only PostgreSQL <c>numeric</c> values with no counterpart here.
/// </para>
/// </remarks>
[JsonConverter(typeof(BigDecimalJsonConverter))]
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

    /// <summary>The value zero, at scale 0.</summary>
    public static BigDecimal Zero => default;

    /// <summary>The value one, at scale 0.</summary>
    public static BigDecimal One => new(1, 0, 0, 0, false, 0);

    /// <summary>The value minus one, at scale 0.</summary>
    public static BigDecimal NegativeOne => new(1, 0, 0, 0, true, 0);

    /// <summary>
    /// The largest representable value, 2^256-1 at scale 0 — a 78-digit integer.
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
    /// The scale is part of the value's representational identity, not of its numeric value:
    /// <c>1.0</c> and <c>1.00</c> are equal and hash alike but report 1 and 2 here, and the
    /// difference survives formatting and the database wire formats.
    /// </remarks>
    public int Scale => (int)(_flags & ScaleMask);

    /// <summary>
    /// Whether the value is less than zero. Always <see langword="false"/> for zero, which never
    /// carries a sign.
    /// </summary>
    public bool IsNegative => (_flags & SignMask) != 0;

    /// <summary>Whether the magnitude is zero, whatever the scale.</summary>
    public bool IsZero => (_l0 | _l1 | _l2 | _l3) == 0;

    /// <summary>
    /// -1 for a negative value, 0 for zero, 1 for a positive value.
    /// </summary>
    public int Sign => IsZero ? 0 : (IsNegative ? -1 : 1);

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
    public int GetWords(Span<ulong> destination, out bool isNegative, out int scale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, WordCount);

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
        if (destination.Length > WordCount)
        {
            destination[WordCount..].Clear();
        }

        return Words.Normalize(destination[..WordCount]);
    }

    internal static BigDecimal Pack(Span<ulong> magnitude, int length, bool isNegative, int scale)
    {
        length = Words.Normalize(magnitude[..Math.Max(length, 0)]);
        if (length == 0)
        {
            return new BigDecimal(0, 0, 0, 0, false, Math.Clamp(scale, 0, MaxScale));
        }

        if (scale < 0)
        {
            var power = -scale;
            if (Words.DecimalDigitCount(magnitude, length) + power > (magnitude.Length - 1) * 19)
            {
                ThrowMantissaOverflow();
            }

            length = Words.ScaleUp(magnitude, length, power);
            if (length > WordCount)
            {
                ThrowMantissaOverflow();
            }

            scale = 0;
        }

        while (length > WordCount || scale > MaxScale)
        {
            if (scale <= 0)
            {
                ThrowMantissaOverflow();
            }

            var excess = Math.Max(scale - MaxScale, 0);
            if (length > WordCount)
            {
                excess = Math.Max(excess, Words.DecimalDigitCount(magnitude, length) - MaxDigits);
            }

            excess = Math.Clamp(excess <= 0 ? 1 : excess, 1, scale);
            length = Words.DivPow10Round(magnitude, length, excess, isNegative, MidpointRounding.ToEven);
            scale -= excess;
        }

        return new BigDecimal(
            length > 0 ? magnitude[0] : 0,
            length > 1 ? magnitude[1] : 0,
            length > 2 ? magnitude[2] : 0,
            length > 3 ? magnitude[3] : 0,
            isNegative,
            scale);
    }

    [DoesNotReturn]
    internal static void ThrowMantissaOverflow() =>
        throw new OverflowException("Value was either too large or too small for a BigDecimal.");
}
