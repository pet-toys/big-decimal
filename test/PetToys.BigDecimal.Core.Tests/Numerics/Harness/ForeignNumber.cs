using System;
using System.Globalization;
using System.Numerics;

namespace PetToys.BigDecimal.Numerics.Harness;

// A numeric type that refuses every conversion in both directions, which is the case Create* has
// to end in an exception rather than in a zero. Everything else on INumberBase is stubbed because
// nothing calls it; only the six conversion members carry behaviour.

internal readonly struct ForeignNumber : INumberBase<ForeignNumber>
{
    public static ForeignNumber One => default;

    public static int Radix => 10;

    public static ForeignNumber Zero => default;

    public static ForeignNumber AdditiveIdentity => default;

    public static ForeignNumber MultiplicativeIdentity => default;

    public static ForeignNumber Abs(ForeignNumber value) => value;

    public static bool IsCanonical(ForeignNumber value) => true;

    public static bool IsComplexNumber(ForeignNumber value) => false;

    public static bool IsEvenInteger(ForeignNumber value) => false;

    public static bool IsFinite(ForeignNumber value) => true;

    public static bool IsImaginaryNumber(ForeignNumber value) => false;

    public static bool IsInfinity(ForeignNumber value) => false;

    public static bool IsInteger(ForeignNumber value) => false;

    public static bool IsNaN(ForeignNumber value) => false;

    public static bool IsNegative(ForeignNumber value) => false;

    public static bool IsNegativeInfinity(ForeignNumber value) => false;

    public static bool IsNormal(ForeignNumber value) => true;

    public static bool IsOddInteger(ForeignNumber value) => false;

    public static bool IsPositive(ForeignNumber value) => true;

    public static bool IsPositiveInfinity(ForeignNumber value) => false;

    public static bool IsRealNumber(ForeignNumber value) => true;

    public static bool IsSubnormal(ForeignNumber value) => false;

    public static bool IsZero(ForeignNumber value) => true;

    public static ForeignNumber MaxMagnitude(ForeignNumber x, ForeignNumber y) => x;

    public static ForeignNumber MaxMagnitudeNumber(ForeignNumber x, ForeignNumber y) => x;

    public static ForeignNumber MinMagnitude(ForeignNumber x, ForeignNumber y) => x;

    public static ForeignNumber MinMagnitudeNumber(ForeignNumber x, ForeignNumber y) => x;

    public static ForeignNumber Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) => default;

    public static ForeignNumber Parse(string s, NumberStyles style, IFormatProvider? provider) => default;

    public static ForeignNumber Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => default;

    public static ForeignNumber Parse(string s, IFormatProvider? provider) => default;

    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out ForeignNumber result)
    {
        result = default;
        return false;
    }

    public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out ForeignNumber result)
    {
        result = default;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ForeignNumber result)
    {
        result = default;
        return false;
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out ForeignNumber result)
    {
        result = default;
        return false;
    }

    public static bool TryConvertFromChecked<TOther>(TOther value, out ForeignNumber result)
        where TOther : INumberBase<TOther>
    {
        result = default;
        return false;
    }

    public static bool TryConvertFromSaturating<TOther>(TOther value, out ForeignNumber result)
        where TOther : INumberBase<TOther>
    {
        result = default;
        return false;
    }

    public static bool TryConvertFromTruncating<TOther>(TOther value, out ForeignNumber result)
        where TOther : INumberBase<TOther>
    {
        result = default;
        return false;
    }

    public static bool TryConvertToChecked<TOther>(ForeignNumber value, out TOther result)
        where TOther : INumberBase<TOther>
    {
        result = default!;
        return false;
    }

    public static bool TryConvertToSaturating<TOther>(ForeignNumber value, out TOther result)
        where TOther : INumberBase<TOther>
    {
        result = default!;
        return false;
    }

    public static bool TryConvertToTruncating<TOther>(ForeignNumber value, out TOther result)
        where TOther : INumberBase<TOther>
    {
        result = default!;
        return false;
    }

    public static ForeignNumber operator +(ForeignNumber left, ForeignNumber right) => left;

    public static ForeignNumber operator -(ForeignNumber left, ForeignNumber right) => left;

    public static ForeignNumber operator ++(ForeignNumber value) => value;

    public static ForeignNumber operator --(ForeignNumber value) => value;

    public static ForeignNumber operator *(ForeignNumber left, ForeignNumber right) => left;

    public static ForeignNumber operator /(ForeignNumber left, ForeignNumber right) => left;

    public static ForeignNumber operator +(ForeignNumber value) => value;

    public static ForeignNumber operator -(ForeignNumber value) => value;

    public static bool operator ==(ForeignNumber left, ForeignNumber right) => true;

    public static bool operator !=(ForeignNumber left, ForeignNumber right) => false;

    public bool Equals(ForeignNumber other) => true;

    public override bool Equals(object? obj) => obj is ForeignNumber;

    public override int GetHashCode() => 0;

    public string ToString(string? format, IFormatProvider? formatProvider) => "foreign";

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        charsWritten = 0;
        return true;
    }
}
