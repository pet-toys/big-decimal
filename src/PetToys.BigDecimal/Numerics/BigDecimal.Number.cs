using System;
using System.Globalization;
using System.Numerics;

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

    static bool INumberBase<BigDecimal>.IsFinite(BigDecimal value) => true;

    static bool INumberBase<BigDecimal>.IsImaginaryNumber(BigDecimal value) => false;

    static bool INumberBase<BigDecimal>.IsInfinity(BigDecimal value) => false;

    static bool INumberBase<BigDecimal>.IsInteger(BigDecimal value) => IsIntegerValue(value);

    static bool INumberBase<BigDecimal>.IsNaN(BigDecimal value) => false;

    static bool INumberBase<BigDecimal>.IsNegative(BigDecimal value) => value.IsNegative;

    static bool INumberBase<BigDecimal>.IsNegativeInfinity(BigDecimal value) => false;

    static bool INumberBase<BigDecimal>.IsNormal(BigDecimal value) => !value.IsZero;

    static bool INumberBase<BigDecimal>.IsOddInteger(BigDecimal value) =>
        IsIntegerValue(value) && !(Truncate(value) % Two).IsZero;

    static bool INumberBase<BigDecimal>.IsPositive(BigDecimal value) => !value.IsNegative;

    static bool INumberBase<BigDecimal>.IsPositiveInfinity(BigDecimal value) => false;

    static bool INumberBase<BigDecimal>.IsRealNumber(BigDecimal value) => true;

    static bool INumberBase<BigDecimal>.IsSubnormal(BigDecimal value) => false;

    static bool INumberBase<BigDecimal>.IsZero(BigDecimal value) => value.IsZero;

    static BigDecimal INumberBase<BigDecimal>.MaxMagnitude(BigDecimal x, BigDecimal y) => MaxMagnitude(x, y);

    static BigDecimal INumberBase<BigDecimal>.MaxMagnitudeNumber(BigDecimal x, BigDecimal y) => MaxMagnitude(x, y);

    static BigDecimal INumberBase<BigDecimal>.MinMagnitude(BigDecimal x, BigDecimal y) => MinMagnitude(x, y);

    static BigDecimal INumberBase<BigDecimal>.MinMagnitudeNumber(BigDecimal x, BigDecimal y) => MinMagnitude(x, y);

    static BigDecimal INumber<BigDecimal>.MaxNumber(BigDecimal x, BigDecimal y) => Max(x, y);

    static BigDecimal INumber<BigDecimal>.MinNumber(BigDecimal x, BigDecimal y) => Min(x, y);

    static BigDecimal INumber<BigDecimal>.Clamp(BigDecimal value, BigDecimal min, BigDecimal max) =>
        Clamp(value, min, max);

    static int INumber<BigDecimal>.Sign(BigDecimal value) => value.Sign;

    static BigDecimal INumberBase<BigDecimal>.CreateChecked<TOther>(TOther value) =>
        TryConvertFrom(value, out var result)
            ? result
            : throw new NotSupportedException($"Cannot convert {typeof(TOther)} to BigDecimal.");

    static BigDecimal INumberBase<BigDecimal>.CreateSaturating<TOther>(TOther value) =>
        TryConvertFrom(value, out var result) ? result : Zero;

    static BigDecimal INumberBase<BigDecimal>.CreateTruncating<TOther>(TOther value) =>
        TryConvertFrom(value, out var result) ? result : Zero;

    static bool INumberBase<BigDecimal>.TryConvertFromChecked<TOther>(TOther value, out BigDecimal result) =>
        TryConvertFrom(value, out result);

    static bool INumberBase<BigDecimal>.TryConvertFromSaturating<TOther>(TOther value, out BigDecimal result) =>
        TryConvertFrom(value, out result);

    static bool INumberBase<BigDecimal>.TryConvertFromTruncating<TOther>(TOther value, out BigDecimal result) =>
        TryConvertFrom(value, out result);

    static bool INumberBase<BigDecimal>.TryConvertToChecked<TOther>(BigDecimal value, out TOther result) =>
        TryConvertTo(value, out result);

    static bool INumberBase<BigDecimal>.TryConvertToSaturating<TOther>(BigDecimal value, out TOther result) =>
        TryConvertTo(value, out result);

    static bool INumberBase<BigDecimal>.TryConvertToTruncating<TOther>(BigDecimal value, out TOther result) =>
        TryConvertTo(value, out result);

    private static BigDecimal Two => new(2, 0, 0, 0, false, 0);

    private static bool IsIntegerValue(BigDecimal value) => Truncate(value) == value;

    private static BigDecimal MaxMagnitude(BigDecimal x, BigDecimal y)
    {
        var cmp = CompareMagnitude(Abs(x), Abs(y));
        if (cmp != 0)
        {
            return cmp > 0 ? x : y;
        }

        return x.IsNegative ? y : x;
    }

    private static BigDecimal MinMagnitude(BigDecimal x, BigDecimal y)
    {
        var cmp = CompareMagnitude(Abs(x), Abs(y));
        if (cmp != 0)
        {
            return cmp < 0 ? x : y;
        }

        return x.IsNegative ? x : y;
    }

    private static bool TryConvertFrom<TOther>(TOther value, out BigDecimal result)
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
            case decimal v: result = v; return true;
            case double v: result = (BigDecimal)v; return true;
            case float v: result = (BigDecimal)v; return true;
            case BigInteger v: result = (BigDecimal)v; return true;
            case BigDecimal v: result = v; return true;
            default: result = default; return false;
        }
    }

    private static bool TryConvertTo<TOther>(BigDecimal value, out TOther result)
        where TOther : INumberBase<TOther>
    {
        object? converted = default(TOther) switch
        {
            byte => (byte)value,
            sbyte => (sbyte)value,
            short => (short)value,
            ushort => (ushort)value,
            int => (int)value,
            uint => (uint)value,
            long => (long)value,
            ulong => (ulong)value,
            Int128 => (Int128)value,
            UInt128 => (UInt128)value,
            nint => (nint)(long)value,
            nuint => (nuint)(ulong)value,
            decimal => (decimal)value,
            double => (double)value,
            float => (float)value,
            BigInteger => (BigInteger)value,
            BigDecimal => value,
            _ => null,
        };

        if (converted is null)
        {
            result = default!;
            return false;
        }

        result = (TOther)converted;
        return true;
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
