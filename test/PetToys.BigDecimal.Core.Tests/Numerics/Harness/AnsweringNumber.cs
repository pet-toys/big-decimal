using System;
using System.Globalization;
using System.Numerics;

namespace PetToys.BigDecimal.Numerics.Harness;

// A numeric type that refuses the first question and answers the second. BigDecimal cannot convert
// it, so the value has to come back through this type's own TryConvertTo, which is the fallback
// step every Create* method owes the caller before it throws.

internal readonly struct AnsweringNumber : INumberBase<AnsweringNumber>
{
    public static AnsweringNumber One => default;

    public static int Radix => 10;

    public static AnsweringNumber Zero => default;

    public static AnsweringNumber AdditiveIdentity => default;

    public static AnsweringNumber MultiplicativeIdentity => default;

    public static AnsweringNumber Abs(AnsweringNumber value) => value;

    public static bool IsCanonical(AnsweringNumber value) => true;

    public static bool IsComplexNumber(AnsweringNumber value) => false;

    public static bool IsEvenInteger(AnsweringNumber value) => false;

    public static bool IsFinite(AnsweringNumber value) => true;

    public static bool IsImaginaryNumber(AnsweringNumber value) => false;

    public static bool IsInfinity(AnsweringNumber value) => false;

    public static bool IsInteger(AnsweringNumber value) => false;

    public static bool IsNaN(AnsweringNumber value) => false;

    public static bool IsNegative(AnsweringNumber value) => false;

    public static bool IsNegativeInfinity(AnsweringNumber value) => false;

    public static bool IsNormal(AnsweringNumber value) => true;

    public static bool IsOddInteger(AnsweringNumber value) => false;

    public static bool IsPositive(AnsweringNumber value) => true;

    public static bool IsPositiveInfinity(AnsweringNumber value) => false;

    public static bool IsRealNumber(AnsweringNumber value) => true;

    public static bool IsSubnormal(AnsweringNumber value) => false;

    public static bool IsZero(AnsweringNumber value) => true;

    public static AnsweringNumber MaxMagnitude(AnsweringNumber x, AnsweringNumber y) => x;

    public static AnsweringNumber MaxMagnitudeNumber(AnsweringNumber x, AnsweringNumber y) => x;

    public static AnsweringNumber MinMagnitude(AnsweringNumber x, AnsweringNumber y) => x;

    public static AnsweringNumber MinMagnitudeNumber(AnsweringNumber x, AnsweringNumber y) => x;

    public static AnsweringNumber Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) => default;

    public static AnsweringNumber Parse(string s, NumberStyles style, IFormatProvider? provider) => default;

    public static AnsweringNumber Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => default;

    public static AnsweringNumber Parse(string s, IFormatProvider? provider) => default;

    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out AnsweringNumber result)
    {
        result = default;
        return false;
    }

    public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out AnsweringNumber result)
    {
        result = default;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out AnsweringNumber result)
    {
        result = default;
        return false;
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out AnsweringNumber result)
    {
        result = default;
        return false;
    }

    public static bool TryConvertFromChecked<TOther>(TOther value, out AnsweringNumber result)
        where TOther : INumberBase<TOther>
    {
        result = default;
        return false;
    }

    public static bool TryConvertFromSaturating<TOther>(TOther value, out AnsweringNumber result)
        where TOther : INumberBase<TOther>
    {
        result = default;
        return false;
    }

    public static bool TryConvertFromTruncating<TOther>(TOther value, out AnsweringNumber result)
        where TOther : INumberBase<TOther>
    {
        result = default;
        return false;
    }

    public static bool TryConvertToChecked<TOther>(AnsweringNumber value, out TOther result)
        where TOther : INumberBase<TOther>
    {
        result = TOther.CreateChecked(42);
        return true;
    }

    public static bool TryConvertToSaturating<TOther>(AnsweringNumber value, out TOther result)
        where TOther : INumberBase<TOther>
    {
        result = TOther.CreateSaturating(42);
        return true;
    }

    public static bool TryConvertToTruncating<TOther>(AnsweringNumber value, out TOther result)
        where TOther : INumberBase<TOther>
    {
        result = TOther.CreateTruncating(42);
        return true;
    }

    public static AnsweringNumber operator +(AnsweringNumber left, AnsweringNumber right) => left;

    public static AnsweringNumber operator -(AnsweringNumber left, AnsweringNumber right) => left;

    public static AnsweringNumber operator ++(AnsweringNumber value) => value;

    public static AnsweringNumber operator --(AnsweringNumber value) => value;

    public static AnsweringNumber operator *(AnsweringNumber left, AnsweringNumber right) => left;

    public static AnsweringNumber operator /(AnsweringNumber left, AnsweringNumber right) => left;

    public static AnsweringNumber operator +(AnsweringNumber value) => value;

    public static AnsweringNumber operator -(AnsweringNumber value) => value;

    public static bool operator ==(AnsweringNumber left, AnsweringNumber right) => true;

    public static bool operator !=(AnsweringNumber left, AnsweringNumber right) => false;

    public bool Equals(AnsweringNumber other) => true;

    public override bool Equals(object? obj) => obj is AnsweringNumber;

    public override int GetHashCode() => 0;

    public string ToString(string? format, IFormatProvider? formatProvider) => "answering";

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        charsWritten = 0;
        return true;
    }
}
