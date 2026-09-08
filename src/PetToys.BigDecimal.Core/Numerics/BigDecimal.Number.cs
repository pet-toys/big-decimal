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

    // An infinity is not normal and not subnormal either, which is double's answer and not a
    // consequence of anything: measured, not reasoned about.
    static bool INumberBase<BigDecimal>.IsNormal(BigDecimal value) => IsFinite(value) && !value.IsZero;

    static bool INumberBase<BigDecimal>.IsOddInteger(BigDecimal value) =>
        IsIntegerValue(value) && !(Truncate(value) % Two).IsZero;

    // NaN carries no sign here, so the plain negation would call it positive. double answers
    // false to both IsPositive and IsNegative for its own NaN by a different route: its NaN has
    // the hardware sign bit set, so IsNegative is true there. Ours is false, which is the one
    // entry in the predicate table that diverges.
    static bool INumberBase<BigDecimal>.IsPositive(BigDecimal value) => !value.IsNegative && !IsNaN(value);

    static bool INumberBase<BigDecimal>.IsPositiveInfinity(BigDecimal value) => IsPositiveInfinity(value);

    // True for both infinities and false for NaN, which is double's answer and reads backwards
    // from the name until you notice that NaN is not a number at all.
    static bool INumberBase<BigDecimal>.IsRealNumber(BigDecimal value) => !IsNaN(value);

    static bool INumberBase<BigDecimal>.IsSubnormal(BigDecimal value) => false;

    static bool INumberBase<BigDecimal>.IsZero(BigDecimal value) => value.IsZero;

    static BigDecimal INumberBase<BigDecimal>.MaxMagnitude(BigDecimal x, BigDecimal y) => MaxMagnitude(x, y);

    static BigDecimal INumberBase<BigDecimal>.MaxMagnitudeNumber(BigDecimal x, BigDecimal y) => MaxMagnitudeNumber(x, y);

    static BigDecimal INumberBase<BigDecimal>.MinMagnitude(BigDecimal x, BigDecimal y) => MinMagnitude(x, y);

    static BigDecimal INumberBase<BigDecimal>.MinMagnitudeNumber(BigDecimal x, BigDecimal y) => MinMagnitudeNumber(x, y);

    static BigDecimal INumber<BigDecimal>.MaxNumber(BigDecimal x, BigDecimal y) => MaxNumber(x, y);

    static BigDecimal INumber<BigDecimal>.MinNumber(BigDecimal x, BigDecimal y) => MinNumber(x, y);

    // MaxNative, MinNative and ClampNative arrived with .NET 10 as default interface methods
    // that route to Max, Min and Clamp, and they are left as those defaults. double overrides
    // them with the hardware's asymmetric behaviour, where MaxNative(NaN, 1) is 1 but
    // MaxNative(1, NaN) is NaN. Not reproducing that is a decision: an operand order that
    // changes the answer is a property of an instruction, not of this type.

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
    /// <see cref="CreateSaturating{TOther}"/>, whatever the source: truncation is defined on the
    /// two's-complement representation of an integer, which a scaled decimal value does not have,
    /// so the base class library clamps here instead.
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

    // An infinity is an integer to nothing: double answers false to IsInteger, IsEvenInteger
    // and IsOddInteger for both of them, and the finiteness test here is what makes all three
    // agree. Without it Truncate(Infinity) == Infinity would report an integer.
    private static bool IsIntegerValue(BigDecimal value) => IsFinite(value) && Truncate(value) == value;

    // The four selectors come in two families that this type used to implement once. The plain
    // ones propagate a NaN operand; the Number ones return the other operand instead, which is
    // the whole content of the suffix.
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

    // Neither operand is NaN here, so an infinity simply outweighs every finite magnitude and
    // CompareMagnitude - which reads the words, and a non-finite value's words are zero - is
    // never asked about one.
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

    /// <summary>Converts into a <see cref="BigDecimal"/> from one of the recognised types.</summary>
    /// <remarks>
    /// <paramref name="saturate"/> serves both the saturating and the truncating conversion, which
    /// are the same operation here. Truncation is defined on the two's-complement representation of
    /// an integer, which a scaled decimal value does not have, so the base class library clamps
    /// instead for a real-valued source: <c>byte.CreateTruncating(300m)</c> is 255 where
    /// <c>byte.CreateTruncating(300)</c> is 44. <see cref="decimal"/> implements the two with one
    /// method for the same reason.
    /// </remarks>
    /// <returns><see langword="false"/> when the source type is not one this type recognises.</returns>
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

    /// <summary>Converts out of a <see cref="BigDecimal"/> into one of the recognised types.</summary>
    /// <remarks>
    /// <para>
    /// Written as a chain of <c>typeof(TOther) == typeof(X)</c> tests rather than as a switch
    /// producing an <see cref="object"/>, because each comparison is a compile-time constant for a
    /// value-type instantiation and the box that survives it folds away with the branch. The switch
    /// this replaced allocated 24 bytes converting to a <see cref="long"/> and 32 to a
    /// <see cref="decimal"/>.
    /// </para>
    /// <para>
    /// <paramref name="saturate"/> serves the saturating and the truncating conversion both; see
    /// <see cref="TryFrom{TOther}"/> for why they are one operation. A target that has infinities
    /// receives one rather than an exception even under the checked conversion, which is what
    /// <c>float.CreateChecked(double.MaxValue)</c> does.
    /// </para>
    /// </remarks>
    /// <returns><see langword="false"/> when the target type is not one this type recognises.</returns>
    private static bool TryTo<TOther>(BigDecimal value, bool saturate, out TOther result)
        where TOther : INumberBase<TOther>
    {
        long Signed(long min, long max) =>
            saturate ? ToInt64Saturating(value, min, max) : ToInt64Checked(value, min, max);

        ulong Unsigned(ulong max) =>
            saturate ? ToUInt64Saturating(value, max) : ToUInt64Checked(value, max);

        if (typeof(TOther) == typeof(byte))
        {
            result = (TOther)(object)(byte)Unsigned(byte.MaxValue);
            return true;
        }

        if (typeof(TOther) == typeof(sbyte))
        {
            result = (TOther)(object)(sbyte)Signed(sbyte.MinValue, sbyte.MaxValue);
            return true;
        }

        if (typeof(TOther) == typeof(short))
        {
            result = (TOther)(object)(short)Signed(short.MinValue, short.MaxValue);
            return true;
        }

        if (typeof(TOther) == typeof(ushort))
        {
            result = (TOther)(object)(ushort)Unsigned(ushort.MaxValue);
            return true;
        }

        if (typeof(TOther) == typeof(int))
        {
            result = (TOther)(object)(int)Signed(int.MinValue, int.MaxValue);
            return true;
        }

        if (typeof(TOther) == typeof(uint))
        {
            result = (TOther)(object)(uint)Unsigned(uint.MaxValue);
            return true;
        }

        if (typeof(TOther) == typeof(long))
        {
            result = (TOther)(object)Signed(long.MinValue, long.MaxValue);
            return true;
        }

        if (typeof(TOther) == typeof(ulong))
        {
            result = (TOther)(object)Unsigned(ulong.MaxValue);
            return true;
        }

        if (typeof(TOther) == typeof(char))
        {
            result = (TOther)(object)(char)Unsigned(char.MaxValue);
            return true;
        }

        if (typeof(TOther) == typeof(nint))
        {
            result = (TOther)(object)(nint)Signed(nint.MinValue, nint.MaxValue);
            return true;
        }

        if (typeof(TOther) == typeof(nuint))
        {
            result = (TOther)(object)(nuint)Unsigned((ulong)nuint.MaxValue);
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
            // BigInteger is unbounded, so the cast could never overflow and the saturate flag
            // had nothing to change - until a non-finite value became representable here. NaN
            // saturates to zero as it does for every other destination; an infinity still
            // throws, because there is no extreme to clamp to, which is what
            // BigInteger.CreateSaturating(double.PositiveInfinity) does too.
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
