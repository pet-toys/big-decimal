using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PetToys.BigDecimal.Numerics;

public readonly partial struct BigDecimal
{
    private static readonly BigDecimal DecimalMaxValue = decimal.MaxValue;

    private static readonly BigDecimal DecimalMinValue = decimal.MinValue;

    /// <summary>Converts a <see cref="sbyte"/> to a <see cref="BigDecimal"/> at scale 0. The conversion is exact.</summary>
    public static implicit operator BigDecimal(sbyte value) => FromInt64(value);

    /// <summary>Converts a <see cref="byte"/> to a <see cref="BigDecimal"/> at scale 0. The conversion is exact.</summary>
    public static implicit operator BigDecimal(byte value) => FromUInt64(value, false);

    /// <summary>Converts a <see cref="short"/> to a <see cref="BigDecimal"/> at scale 0. The conversion is exact.</summary>
    public static implicit operator BigDecimal(short value) => FromInt64(value);

    /// <summary>Converts a <see cref="ushort"/> to a <see cref="BigDecimal"/> at scale 0. The conversion is exact.</summary>
    public static implicit operator BigDecimal(ushort value) => FromUInt64(value, false);

    /// <summary>Converts a <see cref="int"/> to a <see cref="BigDecimal"/> at scale 0. The conversion is exact.</summary>
    public static implicit operator BigDecimal(int value) => FromInt64(value);

    /// <summary>Converts a <see cref="uint"/> to a <see cref="BigDecimal"/> at scale 0. The conversion is exact.</summary>
    public static implicit operator BigDecimal(uint value) => FromUInt64(value, false);

    /// <summary>Converts a <see cref="long"/> to a <see cref="BigDecimal"/> at scale 0. The conversion is exact.</summary>
    public static implicit operator BigDecimal(long value) => FromInt64(value);

    /// <summary>Converts a <see cref="ulong"/> to a <see cref="BigDecimal"/> at scale 0. The conversion is exact.</summary>
    public static implicit operator BigDecimal(ulong value) => FromUInt64(value, false);

    /// <summary>Converts a <see cref="Int128"/> to a <see cref="BigDecimal"/> at scale 0. The conversion is exact.</summary>
    public static implicit operator BigDecimal(Int128 value)
    {
        unchecked
        {
            var negative = Int128.IsNegative(value);
            var magnitude = negative ? (UInt128)(-value) : (UInt128)value;
            return FromUInt128(magnitude, negative, 0);
        }
    }

    /// <summary>Converts a <see cref="UInt128"/> to a <see cref="BigDecimal"/> at scale 0. The conversion is exact.</summary>
    public static implicit operator BigDecimal(UInt128 value) => FromUInt128(value, false, 0);

    /// <summary>
    /// Converts a <see cref="decimal"/> to a <see cref="BigDecimal"/>. The conversion is exact and
    /// keeps the source scale, so <c>1.00m</c> arrives with a scale of 2.
    /// </summary>
    public static implicit operator BigDecimal(decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        unchecked
        {
            var low = ((ulong)(uint)bits[1] << 32) | (uint)bits[0];
            ulong high = (uint)bits[2];
            var scale = (bits[3] >> 16) & 0xFF;
            var negative = bits[3] < 0;
            return new BigDecimal(low, high, 0, 0, negative, scale);
        }
    }

    /// <summary>
    /// Converts a <see cref="double"/> to a <see cref="BigDecimal"/>, taking the shortest decimal
    /// form that round-trips through <see cref="double"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>(double)(BigDecimal)value</c> returns <c>value</c> for every finite <see cref="double"/>
    /// whose shortest form has at most 77 integer digits and needs a scale of at most
    /// <see cref="MaxScale"/>. Outside that window the ordinary rules apply: a larger value throws
    /// and a smaller one is rounded at <see cref="MaxScale"/>, which may reach zero. This departs
    /// from <see cref="decimal"/>, whose own conversion rounds to 15 significant digits, because
    /// this type has the digits to hand the value back unchanged.
    /// </para>
    /// <para>
    /// A non-finite source converts to the matching value: <see cref="NaN"/>,
    /// <see cref="PositiveInfinity"/> or <see cref="NegativeInfinity"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="OverflowException">The value is too large for the 256-bit magnitude.</exception>
    public static explicit operator BigDecimal(double value) => FromFloatChecked(value);

    /// <summary>
    /// Converts a <see cref="float"/> to a <see cref="BigDecimal"/>, taking the shortest decimal
    /// form that round-trips through <see cref="float"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>(float)(BigDecimal)value</c> returns <c>value</c> for every finite <see cref="float"/>,
    /// without exception: the whole finite range fits. This departs from <see cref="decimal"/>,
    /// whose own conversion rounds to 7 significant digits, so that <c>(decimal)1.0000001f</c> is
    /// 1 where this conversion keeps 1.0000001.
    /// </para>
    /// <para>
    /// A non-finite source converts to the matching value: <see cref="NaN"/>,
    /// <see cref="PositiveInfinity"/> or <see cref="NegativeInfinity"/>.
    /// </para>
    /// </remarks>
    public static explicit operator BigDecimal(float value) => FromFloatChecked(value);

    /// <summary>Converts a <see cref="BigInteger"/> to a <see cref="BigDecimal"/> at scale 0.</summary>
    /// <exception cref="OverflowException">The value does not fit the 256-bit magnitude.</exception>
    public static explicit operator BigDecimal(BigInteger value)
    {
        if (!TryFromBigInteger(value, out var result))
        {
            ThrowMantissaOverflow();
        }

        return result;
    }

    /// <summary>Builds a value from an unscaled mantissa and a scale.</summary>
    /// <param name="mantissa">The unscaled value, sign included.</param>
    /// <param name="scale">The scale to apply, from 0 to <see cref="MaxScale"/> inclusive.</param>
    /// <returns>The value <paramref name="mantissa"/> multiplied by 10 to the power of minus <paramref name="scale"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is outside 0 to <see cref="MaxScale"/>.</exception>
    /// <exception cref="OverflowException"><paramref name="mantissa"/> does not fit the 256-bit magnitude.</exception>
    public static BigDecimal FromScaled(BigInteger mantissa, int scale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scale, MaxScale);

        var integral = (BigDecimal)mantissa;
        return new BigDecimal(integral._l0, integral._l1, integral._l2, integral._l3, integral.IsNegative, scale);
    }

    /// <summary>Returns the unscaled mantissa, sign included.</summary>
    /// <remarks>The value is this mantissa divided by 10 to the power of <see cref="Scale"/>.</remarks>
    /// <returns>The signed mantissa.</returns>
    public BigInteger GetMantissa()
    {
        if (IsNonFinite)
        {
            ThrowNonFiniteHasNoMagnitude();
        }

        Span<ulong> magnitude = stackalloc ulong[WordCount];
        var len = CopyMagnitude(magnitude);
        if (len == 0)
        {
            return BigInteger.Zero;
        }

        BigInteger result = new(MemoryMarshal.AsBytes(magnitude[..len]), isUnsigned: true, isBigEndian: false);
        return IsNegative ? -result : result;
    }

    /// <summary>
    /// Converts a <see cref="BigDecimal"/> to a <see cref="decimal"/>, rounding a scale wider than
    /// 28 to nearest with ties to even.
    /// </summary>
    /// <exception cref="OverflowException">The value is outside the range of <see cref="decimal"/>, or is NaN or an infinity, which <see cref="decimal"/> cannot represent.</exception>
    public static explicit operator decimal(BigDecimal value)
    {
        if (value.IsNonFinite)
        {
            ThrowNonFiniteUnrepresentable("decimal");
        }

        var source = value.Scale > 28 ? Round(value, 28, MidpointRounding.ToEven) : value;

        Span<ulong> magnitude = stackalloc ulong[WordCount];
        var len = source.CopyMagnitude(magnitude);
        var scale = source.Scale;

        while (len > 2 || (len == 2 && magnitude[1] > uint.MaxValue))
        {
            if (scale == 0)
            {
                ThrowMantissaOverflow();
            }

            len = Words.DivPow10Round(magnitude, len, 1, source.IsNegative, MidpointRounding.ToEven);
            scale--;
        }

        unchecked
        {
            var low = len > 0 ? magnitude[0] : 0;
            var high = len > 1 ? (uint)magnitude[1] : 0;
            return new decimal((int)(uint)low, (int)(uint)(low >> 32), (int)high, source.IsNegative, (byte)scale);
        }
    }

    /// <summary>Converts a <see cref="BigDecimal"/> to the nearest <see cref="double"/>, which may lose precision.</summary>
    public static explicit operator double(BigDecimal value)
    {
        Span<char> buffer = stackalloc char[MaxCharsPlain];
        return value.TryFormatInvariant(buffer, out var written)
            ? double.Parse(buffer[..written], NumberStyles.Float, CultureInfo.InvariantCulture)
            : double.Parse(value.ToString(null, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Converts a <see cref="BigDecimal"/> to the nearest <see cref="float"/>, which may lose precision.</summary>
    public static explicit operator float(BigDecimal value) => (float)(double)value;

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="BigInteger"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The value is NaN or an infinity, which <see cref="BigInteger"/> cannot represent.</exception>
    public static explicit operator BigInteger(BigDecimal value)
    {
        if (value.IsNonFinite)
        {
            ThrowNonFiniteUnrepresentable(nameof(BigInteger));
        }

        var whole = Truncate(value);
        Span<ulong> magnitude = stackalloc ulong[WordCount];
        var len = whole.CopyMagnitude(magnitude);
        if (len == 0)
        {
            return BigInteger.Zero;
        }

        var bytes = MemoryMarshal.AsBytes(magnitude[..len]);
        BigInteger result = new(bytes, isUnsigned: true, isBigEndian: false);
        return whole.IsNegative ? -result : result;
    }

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="long"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="long"/>.</exception>
    public static explicit operator long(BigDecimal value) => (long)ToInt64Checked(value, long.MinValue, long.MaxValue);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="int"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="int"/>.</exception>
    public static explicit operator int(BigDecimal value) => (int)ToInt64Checked(value, int.MinValue, int.MaxValue);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="short"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="short"/>.</exception>
    public static explicit operator short(BigDecimal value) => (short)ToInt64Checked(value, short.MinValue, short.MaxValue);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="sbyte"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="sbyte"/>.</exception>
    public static explicit operator sbyte(BigDecimal value) => (sbyte)ToInt64Checked(value, sbyte.MinValue, sbyte.MaxValue);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="ulong"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="ulong"/>.</exception>
    public static explicit operator ulong(BigDecimal value) => ToUInt64Checked(value, ulong.MaxValue);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="uint"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="uint"/>.</exception>
    public static explicit operator uint(BigDecimal value) => (uint)ToUInt64Checked(value, uint.MaxValue);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="ushort"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="ushort"/>.</exception>
    public static explicit operator ushort(BigDecimal value) => (ushort)ToUInt64Checked(value, ushort.MaxValue);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="byte"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="byte"/>.</exception>
    public static explicit operator byte(BigDecimal value) => (byte)ToUInt64Checked(value, byte.MaxValue);

    /// <summary>Converts a <see cref="BigDecimal"/> to an <see cref="Int128"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="Int128"/>.</exception>
    public static explicit operator Int128(BigDecimal value)
    {
        unchecked
        {
            var magnitude = ToUInt128Magnitude(value, out var negative);
            if (negative)
            {
                if (magnitude > (UInt128)Int128.MaxValue + 1)
                {
                    ThrowMantissaOverflow();
                }

                return magnitude == (UInt128)Int128.MaxValue + 1 ? Int128.MinValue : -(Int128)magnitude;
            }

            if (magnitude > (UInt128)Int128.MaxValue)
            {
                ThrowMantissaOverflow();
            }

            return (Int128)magnitude;
        }
    }

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="UInt128"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The value is negative or its integral part is outside the range of <see cref="UInt128"/>.</exception>
    public static explicit operator UInt128(BigDecimal value)
    {
        var magnitude = ToUInt128Magnitude(value, out var negative);
        if (negative && magnitude != UInt128.Zero)
        {
            ThrowMantissaOverflow();
        }

        return magnitude;
    }

    /// <summary>
    /// Converts a binary floating-point value through its shortest round-trippable form, throwing
    /// where the value has no counterpart here.
    /// </summary>
    /// <remarks>
    /// The shortest form is what the default format produces, and it is what makes the conversion
    /// reversible: a fixed digit count either loses the value, as <c>"G15"</c> did, or writes out
    /// digits the caller never had, as <c>"G17"</c> does.
    /// </remarks>
    private static BigDecimal FromFloatChecked<TFloat>(TFloat value)
        where TFloat : IBinaryFloatingPointIeee754<TFloat>
    {
        if (!TFloat.IsFinite(value))
        {
            return FromNonFinite(value);
        }

        if (!TryFromFloat(value, out var result))
        {
            ThrowMantissaOverflow();
        }

        return result;
    }

    /// <summary>
    /// Converts a binary floating-point value, clamping anything without a counterpart here to the
    /// nearest extreme, as <see cref="decimal"/> does.
    /// </summary>
    private static BigDecimal FromFloatSaturating<TFloat>(TFloat value)
        where TFloat : IBinaryFloatingPointIeee754<TFloat>
    {
        if (!TFloat.IsFinite(value))
        {
            return FromNonFinite(value);
        }

        return TryFromFloat(value, out var result)
            ? result
            : (TFloat.IsNegative(value) ? MinValue : MaxValue);
    }

    // All three contracts agree for a non-finite source now that the value is representable:
    // none of them has anything left to refuse. Before this change the checked one threw and
    // the other two flattened NaN to zero and an infinity to MaxValue, which is what decimal
    // still does because decimal has no such value to convert to.
    private static BigDecimal FromNonFinite<TFloat>(TFloat value)
        where TFloat : IBinaryFloatingPointIeee754<TFloat>
    {
        if (TFloat.IsNaN(value))
        {
            return NaN;
        }

        return TFloat.IsNegative(value) ? NegativeInfinity : PositiveInfinity;
    }

    private static bool TryFromFloat<TFloat>(TFloat value, out BigDecimal result)
        where TFloat : IBinaryFloatingPointIeee754<TFloat>
    {
        // 24 characters is the longest shortest-form double: a sign, a digit, a point, sixteen
        // digits and a five-character exponent. Asserted rather than only handled, because the
        // saturating caller reads a false as "does not fit" and would clamp to MaxValue for a
        // buffer that was merely too small.
        Span<char> buffer = stackalloc char[32];
        if (!value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture))
        {
            Debug.Fail("32 characters holds the shortest form of every type this is called with.");
            result = default;
            return false;
        }

        return TryParse(buffer[..written], NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryFromBigInteger(BigInteger value, out BigDecimal result)
    {
        var negative = value.Sign < 0;
        var magnitude = negative ? -value : value;

        Span<byte> bytes = stackalloc byte[(WordCount * 8) + 1];
        if (!magnitude.TryWriteBytes(bytes, out var written, isUnsigned: true, isBigEndian: false)
            || written > WordCount * 8)
        {
            result = default;
            return false;
        }

        bytes[written..].Clear();
        Span<ulong> words = stackalloc ulong[WordCount];
        MemoryMarshal.Cast<byte, ulong>(bytes[..(WordCount * 8)]).CopyTo(words);
        result = FromWords(words, negative, 0);
        return true;
    }

    private static BigDecimal FromBigIntegerSaturating(BigInteger value) =>
        TryFromBigInteger(value, out var result) ? result : (value.Sign < 0 ? MinValue : MaxValue);

    private static decimal ToDecimalSaturating(BigDecimal value)
    {
        // NaN first: both comparisons below are false against it, because the relational
        // operators leave NaN unordered, so without this it falls through to the cast and
        // throws. A saturating conversion that throws is not a saturating conversion, and
        // decimal.CreateSaturating(double.NaN) is zero.
        if (IsNaN(value))
        {
            return decimal.Zero;
        }

        if (value > DecimalMaxValue)
        {
            return decimal.MaxValue;
        }

        return value < DecimalMinValue ? decimal.MinValue : (decimal)value;
    }

    private static BigDecimal FromInt64(long value)
    {
        unchecked
        {
            var negative = value < 0;
            var magnitude = negative ? (ulong)(-(value + 1)) + 1 : (ulong)value;
            return FromUInt64(magnitude, negative);
        }
    }

    private static BigDecimal FromUInt64(ulong value, bool negative) => new(value, 0, 0, 0, negative, 0);

    // Unchecked because both casts discard the half of the value the other one keeps. Without it a
    // Debug build, which compiles with CheckForOverflowUnderflow, throws on any Int128 or UInt128
    // past 64 bits: (BigDecimal)Int128.MaxValue failed where the Release build was correct.
    private static BigDecimal FromUInt128(UInt128 value, bool negative, int scale)
    {
        unchecked
        {
            return new BigDecimal((ulong)value, (ulong)(value >> 64), 0, 0, negative, scale);
        }
    }

    private static long ToInt64Checked(BigDecimal value, long min, long max)
    {
        unchecked
        {
            var magnitude = ToUInt128Magnitude(value, out var negative);
            var limit = negative ? (UInt128)(ulong)(-(min + 1)) + 1 : (UInt128)max;
            if (magnitude > limit)
            {
                ThrowMantissaOverflow();
            }

            if (!negative)
            {
                return (long)magnitude;
            }

            return magnitude == limit ? min : -(long)magnitude;
        }
    }

    private static ulong ToUInt64Checked(BigDecimal value, ulong max)
    {
        var magnitude = ToUInt128Magnitude(value, out var negative);
        if ((negative && magnitude != UInt128.Zero) || magnitude > max)
        {
            ThrowMantissaOverflow();
        }

        return (ulong)magnitude;
    }

    /// <summary>Clamps the integral part into a signed 64-bit range instead of throwing.</summary>
    /// <remarks>
    /// Every bounded signed target reaches its own range through this one, so the reduction from a
    /// 256-bit magnitude is written once rather than once per target and variant.
    /// </remarks>
    private static long ToInt64Saturating(BigDecimal value, long min, long max)
    {
        var integral = ToInt128Saturating(value);
        if (integral < min)
        {
            return min;
        }

        return integral > max ? max : (long)integral;
    }

    /// <summary>Clamps the integral part into an unsigned 64-bit range instead of throwing.</summary>
    private static ulong ToUInt64Saturating(BigDecimal value, ulong max)
    {
        var integral = ToUInt128Saturating(value);
        return integral > max ? max : (ulong)integral;
    }

    private static Int128 ToInt128Saturating(BigDecimal value)
    {
        // NaN saturates to zero and an infinity to the destination's extreme, which is what the
        // base class library does converting double to an integer type. Measured. The checked
        // contract throws instead; a saturating conversion that throws would not be one.
        if (IsNaN(value))
        {
            return Int128.Zero;
        }

        unchecked
        {
            if (!TryToUInt128Magnitude(value, out var magnitude, out var negative))
            {
                return negative ? Int128.MinValue : Int128.MaxValue;
            }

            if (!negative)
            {
                return magnitude > (UInt128)Int128.MaxValue ? Int128.MaxValue : (Int128)magnitude;
            }

            return magnitude >= (UInt128)Int128.MaxValue + 1 ? Int128.MinValue : -(Int128)magnitude;
        }
    }

    private static UInt128 ToUInt128Saturating(BigDecimal value)
    {
        if (IsNaN(value))
        {
            return UInt128.Zero;
        }

        if (!TryToUInt128Magnitude(value, out var magnitude, out var negative))
        {
            return negative ? UInt128.Zero : UInt128.MaxValue;
        }

        return negative && magnitude != UInt128.Zero ? UInt128.Zero : magnitude;
    }

    private static UInt128 ToUInt128Magnitude(BigDecimal value, out bool negative)
    {
        if (value.IsNonFinite)
        {
            ThrowNonFiniteUnrepresentable("integer");
        }

        if (!TryToUInt128Magnitude(value, out var magnitude, out negative))
        {
            ThrowMantissaOverflow();
        }

        return magnitude;
    }

    [DoesNotReturn]
    private static void ThrowNonFiniteUnrepresentable(string destination) =>
        throw new OverflowException($"NaN and infinity have no {destination} representation.");

    /// <summary>
    /// Reduces the value to its integral part, truncated towards zero, as a magnitude and a sign.
    /// </summary>
    /// <remarks>
    /// The sign is reported even when the magnitude does not fit, because a saturating conversion
    /// needs it to choose which extreme to clamp to.
    /// </remarks>
    /// <returns><see langword="false"/> when the integral part is wider than 128 bits.</returns>
    private static bool TryToUInt128Magnitude(BigDecimal value, out UInt128 magnitude, out bool negative)
    {
        if (value.IsNonFinite)
        {
            // Its four words are zero, so without this it would report a magnitude of zero and
            // an infinity would convert to 0 rather than to an extreme. The sign is still
            // reported, because that is what the saturating caller clamps by.
            magnitude = UInt128.Zero;
            negative = value.IsNegative;
            return false;
        }

        var whole = Truncate(value);
        negative = whole.IsNegative;
        Span<ulong> words = stackalloc ulong[WordCount];
        var len = whole.CopyMagnitude(words);
        if (len > 2)
        {
            magnitude = UInt128.Zero;
            return false;
        }

        var low = len > 0 ? words[0] : 0;
        var high = len > 1 ? words[1] : 0;
        magnitude = new UInt128(high, low);
        return true;
    }
}
