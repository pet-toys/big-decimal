using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PetToys.BigDecimal.Numerics;

public readonly partial struct BigDecimal
{
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
    /// Converts a <see cref="double"/> to a <see cref="BigDecimal"/>, taking the value to 15
    /// significant decimal digits.
    /// </summary>
    /// <exception cref="OverflowException">The value is NaN, an infinity, or too large for the 256-bit magnitude.</exception>
    public static explicit operator BigDecimal(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new OverflowException("NaN and infinity have no BigDecimal representation.");
        }

        Span<char> buffer = stackalloc char[32];
        if (!value.TryFormat(buffer, out var written, "G15", CultureInfo.InvariantCulture))
        {
            ThrowMantissaOverflow();
        }

        return Parse(buffer[..written], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Converts a <see cref="float"/> to a <see cref="BigDecimal"/>, taking the value to 7
    /// significant decimal digits.
    /// </summary>
    /// <exception cref="OverflowException">The value is NaN, an infinity, or too large for the 256-bit magnitude.</exception>
    public static explicit operator BigDecimal(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new OverflowException("NaN and infinity have no BigDecimal representation.");
        }

        Span<char> buffer = stackalloc char[32];
        if (!value.TryFormat(buffer, out var written, "G7", CultureInfo.InvariantCulture))
        {
            ThrowMantissaOverflow();
        }

        return Parse(buffer[..written], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Converts a <see cref="BigInteger"/> to a <see cref="BigDecimal"/> at scale 0.</summary>
    /// <exception cref="OverflowException">The value does not fit the 256-bit magnitude.</exception>
    public static explicit operator BigDecimal(BigInteger value)
    {
        var negative = value.Sign < 0;
        var magnitude = negative ? -value : value;

        Span<byte> bytes = stackalloc byte[(WordCount * 8) + 1];
        if (!magnitude.TryWriteBytes(bytes, out var written, isUnsigned: true, isBigEndian: false))
        {
            ThrowMantissaOverflow();
        }

        if (written > WordCount * 8)
        {
            ThrowMantissaOverflow();
        }

        bytes[written..].Clear();
        Span<ulong> words = stackalloc ulong[WordCount];
        MemoryMarshal.Cast<byte, ulong>(bytes[..(WordCount * 8)]).CopyTo(words);
        return FromWords(words, negative, 0);
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
    /// <exception cref="OverflowException">The value is outside the range of <see cref="decimal"/>.</exception>
    public static explicit operator decimal(BigDecimal value)
    {
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
    public static explicit operator BigInteger(BigDecimal value)
    {
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

    private static BigDecimal FromUInt128(UInt128 value, bool negative, int scale) =>
        new((ulong)value, (ulong)(value >> 64), 0, 0, negative, scale);

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

    private static UInt128 ToUInt128Magnitude(BigDecimal value, out bool negative)
    {
        var whole = Truncate(value);
        negative = whole.IsNegative;
        Span<ulong> magnitude = stackalloc ulong[WordCount];
        var len = whole.CopyMagnitude(magnitude);
        if (len > 2)
        {
            ThrowMantissaOverflow();
        }

        var low = len > 0 ? magnitude[0] : 0;
        var high = len > 1 ? magnitude[1] : 0;
        return new UInt128(high, low);
    }
}
