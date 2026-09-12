using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PetToys.BigDecimal.Numerics;

public readonly partial struct BigDecimal
{
    // 2^96 - 1 stops partway through the 29th digit, so 29 digits fit only sometimes.
    private const int DecimalMaxScale = 28;

    private const int DecimalMaxDigits = 29;

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
    /// <c>(double)(BigDecimal)value</c> returns <c>value</c> for every finite <see cref="double"/>
    /// whose shortest form has at most 77 integer digits and a scale of at most
    /// <see cref="MaxScale"/>; a larger value throws and a smaller one is rounded at
    /// <see cref="MaxScale"/>. This departs from <see cref="decimal"/>, whose own conversion
    /// rounds to 15 significant digits. A non-finite source converts to the matching value.
    /// </remarks>
    /// <exception cref="OverflowException">The value is too large for the 256-bit magnitude.</exception>
    public static explicit operator BigDecimal(double value) => FromFloatChecked(value);

    /// <summary>
    /// Converts a <see cref="float"/> to a <see cref="BigDecimal"/>, taking the shortest decimal
    /// form that round-trips through <see cref="float"/>.
    /// </summary>
    /// <remarks>
    /// <c>(float)(BigDecimal)value</c> returns <c>value</c> for every finite <see cref="float"/>:
    /// the whole finite range fits. This departs from <see cref="decimal"/>, whose own conversion
    /// rounds to 7 significant digits. A non-finite source converts to the matching value.
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
    /// Converts a <see cref="BigDecimal"/> to the nearest <see cref="decimal"/>, giving up the
    /// fewest digits that leave a representable value and rounding them, in one step, to nearest
    /// with ties to even.
    /// </summary>
    /// <exception cref="OverflowException">The value is outside the range of <see cref="decimal"/>, or is NaN or an infinity, which <see cref="decimal"/> cannot represent.</exception>
    public static explicit operator decimal(BigDecimal value)
    {
        if (value.IsNonFinite)
        {
            ThrowNonFiniteUnrepresentable(ConversionTarget.Decimal);
        }

        Span<ulong> magnitude = stackalloc ulong[WordCount];
        var len = value.CopyMagnitude(magnitude);
        var scale = value.Scale;

        if (scale > DecimalMaxScale || !FitsDecimalMantissa(magnitude, len))
        {
            // The narrowest reduction that can fit; two turns at most, 29 digits then 28, each
            // rounded from the value itself rather than from the previous turn.
            var drop = Math.Max(
                Math.Max(scale - DecimalMaxScale, Words.DecimalDigitCount(magnitude, len) - DecimalMaxDigits),
                1);

            while (true)
            {
                if (drop > scale)
                {
                    ThrowDestinationOverflow(ConversionTarget.Decimal);
                }

                len = value.CopyMagnitude(magnitude);
                len = Words.DivPow10Round(magnitude, len, drop, value.IsNegative, MidpointRounding.ToEven);
                if (FitsDecimalMantissa(magnitude, len))
                {
                    scale -= drop;

                    break;
                }

                drop++;
            }
        }

        unchecked
        {
            var low = len > 0 ? magnitude[0] : 0;
            var high = len > 1 ? (uint)magnitude[1] : 0;
            return new decimal((int)(uint)low, (int)(uint)(low >> 32), (int)high, value.IsNegative, (byte)scale);
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
            ThrowNonFiniteUnrepresentable(ConversionTarget.BigInteger);
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
    public static explicit operator long(BigDecimal value) => (long)ToInt64Checked(value, long.MinValue, long.MaxValue, ConversionTarget.Int64);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="int"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="int"/>.</exception>
    public static explicit operator int(BigDecimal value) => (int)ToInt64Checked(value, int.MinValue, int.MaxValue, ConversionTarget.Int32);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="short"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="short"/>.</exception>
    public static explicit operator short(BigDecimal value) => (short)ToInt64Checked(value, short.MinValue, short.MaxValue, ConversionTarget.Int16);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="sbyte"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="sbyte"/>.</exception>
    public static explicit operator sbyte(BigDecimal value) => (sbyte)ToInt64Checked(value, sbyte.MinValue, sbyte.MaxValue, ConversionTarget.SByte);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="ulong"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="ulong"/>.</exception>
    public static explicit operator ulong(BigDecimal value) => ToUInt64Checked(value, ulong.MaxValue, ConversionTarget.UInt64);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="uint"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="uint"/>.</exception>
    public static explicit operator uint(BigDecimal value) => (uint)ToUInt64Checked(value, uint.MaxValue, ConversionTarget.UInt32);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="ushort"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="ushort"/>.</exception>
    public static explicit operator ushort(BigDecimal value) => (ushort)ToUInt64Checked(value, ushort.MaxValue, ConversionTarget.UInt16);

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="byte"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="byte"/>.</exception>
    public static explicit operator byte(BigDecimal value) => (byte)ToUInt64Checked(value, byte.MaxValue, ConversionTarget.Byte);

    /// <summary>Converts a <see cref="BigDecimal"/> to an <see cref="Int128"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The integral part is outside the range of <see cref="Int128"/>.</exception>
    public static explicit operator Int128(BigDecimal value)
    {
        unchecked
        {
            var magnitude = ToUInt128Magnitude(value, ConversionTarget.Int128, out var negative);
            if (negative)
            {
                if (magnitude > (UInt128)Int128.MaxValue + 1)
                {
                    ThrowDestinationOverflow(ConversionTarget.Int128);
                }

                return magnitude == (UInt128)Int128.MaxValue + 1 ? Int128.MinValue : -(Int128)magnitude;
            }

            if (magnitude > (UInt128)Int128.MaxValue)
            {
                ThrowDestinationOverflow(ConversionTarget.Int128);
            }

            return (Int128)magnitude;
        }
    }

    /// <summary>Converts a <see cref="BigDecimal"/> to a <see cref="UInt128"/>, discarding the fraction towards zero.</summary>
    /// <exception cref="OverflowException">The value is negative or its integral part is outside the range of <see cref="UInt128"/>.</exception>
    public static explicit operator UInt128(BigDecimal value)
    {
        var magnitude = ToUInt128Magnitude(value, ConversionTarget.UInt128, out var negative);
        if (negative && magnitude != UInt128.Zero)
        {
            ThrowDestinationOverflow(ConversionTarget.UInt128);
        }

        return magnitude;
    }

    // Through the shortest round-trippable form, which the default format produces: "G15" loses
    // the value and "G17" writes digits the caller never had.
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

    // Checked and saturating agree: a non-finite source is representable here.
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
        // 24 characters is the longest shortest-form double. Asserted, because the saturating
        // caller reads a false as "does not fit" and would clamp over a buffer merely too small.
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

    // A word and a half: 2^96 - 1.
    private static bool FitsDecimalMantissa(ReadOnlySpan<ulong> magnitude, int length) =>
        length < 2 || (length == 2 && magnitude[1] <= uint.MaxValue);

    private static decimal ToDecimalSaturating(BigDecimal value)
    {
        // NaN is unordered, so it would fall through both comparisons to the cast and throw.
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

    // Unchecked: each cast discards the half the other keeps, which a Debug build otherwise throws on.
    private static BigDecimal FromUInt128(UInt128 value, bool negative, int scale)
    {
        unchecked
        {
            return new BigDecimal((ulong)value, (ulong)(value >> 64), 0, 0, negative, scale);
        }
    }

    private static long ToInt64Checked(BigDecimal value, long min, long max, ConversionTarget destination)
    {
        unchecked
        {
            var magnitude = ToUInt128Magnitude(value, destination, out var negative);
            var limit = negative ? (UInt128)(ulong)(-(min + 1)) + 1 : (UInt128)max;
            if (magnitude > limit)
            {
                ThrowDestinationOverflow(destination);
            }

            if (!negative)
            {
                return (long)magnitude;
            }

            return magnitude == limit ? min : -(long)magnitude;
        }
    }

    private static ulong ToUInt64Checked(BigDecimal value, ulong max, ConversionTarget destination)
    {
        var magnitude = ToUInt128Magnitude(value, destination, out var negative);
        if ((negative && magnitude != UInt128.Zero) || magnitude > max)
        {
            ThrowDestinationOverflow(destination);
        }

        return (ulong)magnitude;
    }

    private static long ToInt64Saturating(BigDecimal value, long min, long max)
    {
        var integral = ToInt128Saturating(value);
        if (integral < min)
        {
            return min;
        }

        return integral > max ? max : (long)integral;
    }

    private static ulong ToUInt64Saturating(BigDecimal value, ulong max)
    {
        var integral = ToUInt128Saturating(value);
        return integral > max ? max : (ulong)integral;
    }

    private static Int128 ToInt128Saturating(BigDecimal value)
    {
        // NaN saturates to zero and an infinity to the extreme, as double does into an integer.
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

    // Every bounded integer destination passes through here, so a magnitude wider than 128 bits
    // is refused here, before either range check.
    private static UInt128 ToUInt128Magnitude(BigDecimal value, ConversionTarget destination, out bool negative)
    {
        if (value.IsNonFinite)
        {
            ThrowNonFiniteUnrepresentable(destination);
        }

        if (!TryToUInt128Magnitude(value, out var magnitude, out negative))
        {
            ThrowDestinationOverflow(destination);
        }

        return magnitude;
    }

    // The destination of a conversion out, for the message. nint and nuint have no member of
    // their own: NativeSigned and NativeUnsigned pick the one matching the process width.
    private enum ConversionTarget
    {
        Decimal,
        Int64,
        UInt64,
        Int32,
        UInt32,
        Int16,
        UInt16,
        Byte,
        SByte,
        Char,
        Int128,
        UInt128,
        BigInteger,
    }

    // nint is checked against the process's range, so the name follows the width: on 32 bits a
    // message naming Int64 would refuse a value Int64 holds.
    private static ConversionTarget NativeSigned =>
        IntPtr.Size == sizeof(long) ? ConversionTarget.Int64 : ConversionTarget.Int32;

    private static ConversionTarget NativeUnsigned =>
        IntPtr.Size == sizeof(long) ? ConversionTarget.UInt64 : ConversionTarget.UInt32;

    // The base class library's wording per destination, article included: "an Int64", "a UInt64".
    private static (string Article, string Name) Describe(ConversionTarget destination) =>
        destination switch
        {
            ConversionTarget.Decimal => ("a", "Decimal"),
            ConversionTarget.Int64 => ("an", "Int64"),
            ConversionTarget.UInt64 => ("a", "UInt64"),
            ConversionTarget.Int32 => ("an", "Int32"),
            ConversionTarget.UInt32 => ("a", "UInt32"),
            ConversionTarget.Int16 => ("an", "Int16"),
            ConversionTarget.UInt16 => ("a", "UInt16"),
            ConversionTarget.Byte => ("an", "unsigned byte"),
            ConversionTarget.SByte => ("a", "signed byte"),
            ConversionTarget.Char => ("a", "character"),
            ConversionTarget.Int128 => ("an", "Int128"),
            ConversionTarget.UInt128 => ("a", "UInt128"),
            ConversionTarget.BigInteger => ("a", "BigInteger"),
            _ => throw new UnreachableException($"No wording for {destination}."),
        };

    // Names the destination: the source is a BigDecimal by construction, so naming it says nothing.
    [DoesNotReturn]
    private static void ThrowDestinationOverflow(ConversionTarget destination)
    {
        var (article, name) = Describe(destination);

        throw new OverflowException($"Value was either too large or too small for {article} {name}.");
    }

    [DoesNotReturn]
    private static void ThrowNonFiniteUnrepresentable(ConversionTarget destination) =>
        throw new OverflowException($"NaN and infinity have no {Describe(destination).Name} representation.");

    // The integral part, truncated towards zero. The sign is reported even when the magnitude
    // does not fit, because a saturating conversion clamps by it.
    private static bool TryToUInt128Magnitude(BigDecimal value, out UInt128 magnitude, out bool negative)
    {
        if (value.IsNonFinite)
        {
            // Its four words are zero, so an infinity would otherwise convert to 0.
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
