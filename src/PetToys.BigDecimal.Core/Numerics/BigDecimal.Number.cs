using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PetToys.BigDecimal.Numerics;

public readonly partial struct BigDecimal : INumber<BigDecimal>, ISignedNumber<BigDecimal>, IMinMaxValue<BigDecimal>
{
    static BigDecimal INumberBase<BigDecimal>.One => One;

    static BigDecimal INumberBase<BigDecimal>.Zero => Zero;

    static BigDecimal ISignedNumber<BigDecimal>.NegativeOne => NegativeOne;

    static BigDecimal IMinMaxValue<BigDecimal>.MaxValue => MaxValue;

    static BigDecimal IMinMaxValue<BigDecimal>.MinValue => MinValue;

    static int INumberBase<BigDecimal>.Radix => 10;

    static BigDecimal IAdditiveIdentity<BigDecimal, BigDecimal>.AdditiveIdentity => Zero;

    static BigDecimal IMultiplicativeIdentity<BigDecimal, BigDecimal>.MultiplicativeIdentity => One;

    static BigDecimal INumberBase<BigDecimal>.Abs(BigDecimal value) => Abs(value);

    static bool INumberBase<BigDecimal>.IsCanonical(BigDecimal value) => true;

    static bool INumberBase<BigDecimal>.IsComplexNumber(BigDecimal value) => false;

    static bool INumberBase<BigDecimal>.IsEvenInteger(BigDecimal value) =>
        IsIntegerValue(value) && (Truncate(value) % Two).IsZero;

    static bool INumberBase<BigDecimal>.IsFinite(BigDecimal value) => IsFinite(value);

    static bool INumberBase<BigDecimal>.IsImaginaryNumber(BigDecimal value) => false;

    static bool INumberBase<BigDecimal>.IsInfinity(BigDecimal value) => IsInfinity(value);

    static bool INumberBase<BigDecimal>.IsInteger(BigDecimal value) => IsIntegerValue(value);

    static bool INumberBase<BigDecimal>.IsNaN(BigDecimal value) => IsNaN(value);

    static bool INumberBase<BigDecimal>.IsNegative(BigDecimal value) => value.IsNegative;

    static bool INumberBase<BigDecimal>.IsNegativeInfinity(BigDecimal value) => IsNegativeInfinity(value);

    // An infinity is neither normal nor subnormal, as for double.
    static bool INumberBase<BigDecimal>.IsNormal(BigDecimal value) => IsFinite(value) && !value.IsZero;

    static bool INumberBase<BigDecimal>.IsOddInteger(BigDecimal value) =>
        IsIntegerValue(value) && !(Truncate(value) % Two).IsZero;

    // NaN carries no sign here, so a plain negation would call it positive.
    static bool INumberBase<BigDecimal>.IsPositive(BigDecimal value) => !value.IsNegative && !IsNaN(value);

    static bool INumberBase<BigDecimal>.IsPositiveInfinity(BigDecimal value) => IsPositiveInfinity(value);

    // True for both infinities and false for NaN, as for double.
    static bool INumberBase<BigDecimal>.IsRealNumber(BigDecimal value) => !IsNaN(value);

    static bool INumberBase<BigDecimal>.IsSubnormal(BigDecimal value) => false;

    static bool INumberBase<BigDecimal>.IsZero(BigDecimal value) => value.IsZero;

    static BigDecimal INumberBase<BigDecimal>.MaxMagnitude(BigDecimal x, BigDecimal y) => MaxMagnitude(x, y);

    static BigDecimal INumberBase<BigDecimal>.MaxMagnitudeNumber(BigDecimal x, BigDecimal y) => MaxMagnitudeNumber(x, y);

    static BigDecimal INumberBase<BigDecimal>.MinMagnitude(BigDecimal x, BigDecimal y) => MinMagnitude(x, y);

    static BigDecimal INumberBase<BigDecimal>.MinMagnitudeNumber(BigDecimal x, BigDecimal y) => MinMagnitudeNumber(x, y);

    static BigDecimal INumber<BigDecimal>.MaxNumber(BigDecimal x, BigDecimal y) => MaxNumber(x, y);

    static BigDecimal INumber<BigDecimal>.MinNumber(BigDecimal x, BigDecimal y) => MinNumber(x, y);

    // MaxNative, MinNative and ClampNative stay the .NET 10 defaults: double's asymmetry there is
    // a property of an instruction.

    static BigDecimal INumber<BigDecimal>.Clamp(BigDecimal value, BigDecimal min, BigDecimal max) =>
        Clamp(value, min, max);

    static int INumber<BigDecimal>.Sign(BigDecimal value) => value.Sign;

    /// <summary>Converts a value of another numeric type, refusing anything that does not fit.</summary>
    /// <typeparam name="TOther">The type to convert from.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="OverflowException">
    /// The value is outside the range of <see cref="BigDecimal"/>, or is NaN or an infinity.
    /// </exception>
    /// <exception cref="NotSupportedException">Neither type can convert the other.</exception>
    public static BigDecimal CreateChecked<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        BigDecimal result;
        if (TryFrom(value, saturate: false, out result) || TOther.TryConvertToChecked(value, out result))
        {
            return result;
        }

        throw NotConvertible<TOther>();
    }

    /// <summary>
    /// Converts a value of another numeric type, clamping anything outside the range of
    /// <see cref="BigDecimal"/> to <see cref="MinValue"/> or <see cref="MaxValue"/>. A NaN source
    /// converts to <see cref="Zero"/>, as it does for <see cref="decimal"/>.
    /// </summary>
    /// <typeparam name="TOther">The type to convert from.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value, clamped where it did not fit.</returns>
    /// <exception cref="NotSupportedException">Neither type can convert the other.</exception>
    public static BigDecimal CreateSaturating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        BigDecimal result;
        if (TryFrom(value, saturate: true, out result) || TOther.TryConvertToSaturating(value, out result))
        {
            return result;
        }

        throw NotConvertible<TOther>();
    }

    /// <summary>
    /// Converts a value of another numeric type. This is the same conversion as
    /// <see cref="CreateSaturating{TOther}"/>: truncation is defined on an integer's
    /// two's-complement representation, which a scaled decimal value does not have, so
    /// <see cref="decimal"/> clamps here too.
    /// </summary>
    /// <typeparam name="TOther">The type to convert from.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <returns>The converted value, clamped where it did not fit.</returns>
    /// <exception cref="NotSupportedException">Neither type can convert the other.</exception>
    public static BigDecimal CreateTruncating<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        BigDecimal result;
        if (TryFrom(value, saturate: true, out result) || TOther.TryConvertToTruncating(value, out result))
        {
            return result;
        }

        throw NotConvertible<TOther>();
    }

    static bool INumberBase<BigDecimal>.TryConvertFromChecked<TOther>(TOther value, out BigDecimal result) =>
        TryFrom(value, saturate: false, out result);

    static bool INumberBase<BigDecimal>.TryConvertFromSaturating<TOther>(TOther value, out BigDecimal result) =>
        TryFrom(value, saturate: true, out result);

    static bool INumberBase<BigDecimal>.TryConvertFromTruncating<TOther>(TOther value, out BigDecimal result) =>
        TryFrom(value, saturate: true, out result);

    static bool INumberBase<BigDecimal>.TryConvertToChecked<TOther>(BigDecimal value, out TOther result) =>
        TryTo(value, saturate: false, out result);

    static bool INumberBase<BigDecimal>.TryConvertToSaturating<TOther>(BigDecimal value, out TOther result) =>
        TryTo(value, saturate: true, out result);

    static bool INumberBase<BigDecimal>.TryConvertToTruncating<TOther>(BigDecimal value, out TOther result) =>
        TryTo(value, saturate: true, out result);

    private static BigDecimal Two => new(2, 0, 0, 0, false, 0);

    private static NotSupportedException NotConvertible<TOther>() =>
        new($"Cannot convert {typeof(TOther)} to BigDecimal.");

    // Without the finiteness test, Truncate(Infinity) == Infinity would report an integer.
    private static bool IsIntegerValue(BigDecimal value) => IsFinite(value) && Truncate(value) == value;

    // The plain selectors propagate a NaN operand; the Number ones return the other operand.
    private static BigDecimal MaxMagnitude(BigDecimal x, BigDecimal y) =>
        IsNaN(x) || IsNaN(y) ? NaN : MaxMagnitudeCore(x, y);

    private static BigDecimal MaxMagnitudeNumber(BigDecimal x, BigDecimal y)
    {
        if (IsNaN(x))
        {
            return y;
        }

        return IsNaN(y) ? x : MaxMagnitudeCore(x, y);
    }

    private static BigDecimal MinMagnitude(BigDecimal x, BigDecimal y) =>
        IsNaN(x) || IsNaN(y) ? NaN : MinMagnitudeCore(x, y);

    private static BigDecimal MinMagnitudeNumber(BigDecimal x, BigDecimal y)
    {
        if (IsNaN(x))
        {
            return y;
        }

        return IsNaN(y) ? x : MinMagnitudeCore(x, y);
    }

    private static BigDecimal MaxNumber(BigDecimal x, BigDecimal y)
    {
        if (IsNaN(x))
        {
            return y;
        }

        return IsNaN(y) ? x : Max(x, y);
    }

    private static BigDecimal MinNumber(BigDecimal x, BigDecimal y)
    {
        if (IsNaN(x))
        {
            return y;
        }

        return IsNaN(y) ? x : Min(x, y);
    }

    // Neither operand is NaN here. CompareMagnitude reads the words, which a non-finite value's
    // are zero, so an infinity is answered before it.
    private static BigDecimal MaxMagnitudeCore(BigDecimal x, BigDecimal y)
    {
        if (x.IsNonFinite || y.IsNonFinite)
        {
            if (x.IsNonFinite && y.IsNonFinite)
            {
                return x.IsNegative ? y : x;
            }

            return x.IsNonFinite ? x : y;
        }

        var cmp = CompareMagnitude(Abs(x), Abs(y));
        if (cmp != 0)
        {
            return cmp > 0 ? x : y;
        }

        return x.IsNegative ? y : x;
    }

    private static BigDecimal MinMagnitudeCore(BigDecimal x, BigDecimal y)
    {
        if (x.IsNonFinite || y.IsNonFinite)
        {
            if (x.IsNonFinite && y.IsNonFinite)
            {
                return x.IsNegative ? x : y;
            }

            return x.IsNonFinite ? y : x;
        }

        var cmp = CompareMagnitude(Abs(x), Abs(y));
        if (cmp != 0)
        {
            return cmp < 0 ? x : y;
        }

        return x.IsNegative ? x : y;
    }

    // saturate serves the truncating conversion too: byte.CreateTruncating(300m) is 255 where
    // byte.CreateTruncating(300) is 44, and decimal implements the two with one method.
    private static bool TryFrom<TOther>(TOther value, bool saturate, out BigDecimal result)
        where TOther : INumberBase<TOther>
    {
        switch (value)
        {
            case byte v: result = v; return true;
            case sbyte v: result = v; return true;
            case short v: result = v; return true;
            case ushort v: result = v; return true;
            case int v: result = v; return true;
            case uint v: result = v; return true;
            case long v: result = v; return true;
            case ulong v: result = v; return true;
            case Int128 v: result = v; return true;
            case UInt128 v: result = v; return true;
            case nint v: result = v; return true;
            case nuint v: result = v; return true;
            case char v: result = (ushort)v; return true;
            case decimal v: result = v; return true;
            case double v: result = saturate ? FromFloatSaturating(v) : FromFloatChecked(v); return true;
            case float v: result = saturate ? FromFloatSaturating(v) : FromFloatChecked(v); return true;
            case Half v: result = saturate ? FromFloatSaturating(v) : FromFloatChecked(v); return true;
            case NFloat v: result = saturate ? FromFloatSaturating(v) : FromFloatChecked(v); return true;
            case BigInteger v: result = saturate ? FromBigIntegerSaturating(v) : (BigDecimal)v; return true;
            case BigDecimal v: result = v; return true;
            default: result = default; return false;
        }
    }

    // typeof tests rather than a switch over object: each folds to a constant for a value-type
    // instantiation, where the switch allocated 24 to 32 bytes a call. A target with infinities
    // receives one even when checked, as float.CreateChecked(double.MaxValue) does.
    private static bool TryTo<TOther>(BigDecimal value, bool saturate, out TOther result)
        where TOther : INumberBase<TOther>
    {
        long Signed(long min, long max, ConversionTarget destination) =>
            saturate ? ToInt64Saturating(value, min, max) : ToInt64Checked(value, min, max, destination);

        ulong Unsigned(ulong max, ConversionTarget destination) =>
            saturate ? ToUInt64Saturating(value, max) : ToUInt64Checked(value, max, destination);

        if (typeof(TOther) == typeof(byte))
        {
            result = (TOther)(object)(byte)Unsigned(byte.MaxValue, ConversionTarget.Byte);
            return true;
        }

        if (typeof(TOther) == typeof(sbyte))
        {
            result = (TOther)(object)(sbyte)Signed(sbyte.MinValue, sbyte.MaxValue, ConversionTarget.SByte);
            return true;
        }

        if (typeof(TOther) == typeof(short))
        {
            result = (TOther)(object)(short)Signed(short.MinValue, short.MaxValue, ConversionTarget.Int16);
            return true;
        }

        if (typeof(TOther) == typeof(ushort))
        {
            result = (TOther)(object)(ushort)Unsigned(ushort.MaxValue, ConversionTarget.UInt16);
            return true;
        }

        if (typeof(TOther) == typeof(int))
        {
            result = (TOther)(object)(int)Signed(int.MinValue, int.MaxValue, ConversionTarget.Int32);
            return true;
        }

        if (typeof(TOther) == typeof(uint))
        {
            result = (TOther)(object)(uint)Unsigned(uint.MaxValue, ConversionTarget.UInt32);
            return true;
        }

        if (typeof(TOther) == typeof(long))
        {
            result = (TOther)(object)Signed(long.MinValue, long.MaxValue, ConversionTarget.Int64);
            return true;
        }

        if (typeof(TOther) == typeof(ulong))
        {
            result = (TOther)(object)Unsigned(ulong.MaxValue, ConversionTarget.UInt64);
            return true;
        }

        if (typeof(TOther) == typeof(char))
        {
            result = (TOther)(object)(char)Unsigned(char.MaxValue, ConversionTarget.Char);
            return true;
        }

        if (typeof(TOther) == typeof(nint))
        {
            result = (TOther)(object)(nint)Signed(nint.MinValue, nint.MaxValue, NativeSigned);
            return true;
        }

        if (typeof(TOther) == typeof(nuint))
        {
            result = (TOther)(object)(nuint)Unsigned((ulong)nuint.MaxValue, NativeUnsigned);
            return true;
        }

        if (typeof(TOther) == typeof(Int128))
        {
            result = (TOther)(object)(saturate ? ToInt128Saturating(value) : (Int128)value);
            return true;
        }

        if (typeof(TOther) == typeof(UInt128))
        {
            result = (TOther)(object)(saturate ? ToUInt128Saturating(value) : (UInt128)value);
            return true;
        }

        if (typeof(TOther) == typeof(decimal))
        {
            result = (TOther)(object)(saturate ? ToDecimalSaturating(value) : (decimal)value);
            return true;
        }

        if (typeof(TOther) == typeof(double))
        {
            result = (TOther)(object)(double)value;
            return true;
        }

        if (typeof(TOther) == typeof(float))
        {
            result = (TOther)(object)(float)value;
            return true;
        }

        if (typeof(TOther) == typeof(Half))
        {
            result = (TOther)(object)(Half)(double)value;
            return true;
        }

        if (typeof(TOther) == typeof(NFloat))
        {
            result = (TOther)(object)(NFloat)(double)value;
            return true;
        }

        if (typeof(TOther) == typeof(BigInteger))
        {
            // NaN saturates to zero; an infinity throws either way, having no extreme to clamp
            // to, as BigInteger.CreateSaturating does for an infinite double.
            result = (TOther)(object)(saturate && IsNaN(value) ? BigInteger.Zero : (BigInteger)value);
            return true;
        }

        if (typeof(TOther) == typeof(BigDecimal))
        {
            result = (TOther)(object)value;
            return true;
        }

        result = default!;
        return false;
    }

    static BigDecimal INumberBase<BigDecimal>.Parse(string s, NumberStyles style, IFormatProvider? provider) =>
        Parse(s, style, provider);

    static BigDecimal INumberBase<BigDecimal>.Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) =>
        Parse(s, style, provider);

    static bool INumberBase<BigDecimal>.TryParse(
        string? s,
        NumberStyles style,
        IFormatProvider? provider,
        out BigDecimal result) => TryParse(s, style, provider, out result);

    static bool INumberBase<BigDecimal>.TryParse(
        ReadOnlySpan<char> s,
        NumberStyles style,
        IFormatProvider? provider,
        out BigDecimal result) => TryParse(s, style, provider, out result);
}
