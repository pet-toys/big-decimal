using System;

namespace PetToys.BigDecimal.Numerics;

public readonly partial struct BigDecimal : IEquatable<BigDecimal>, IComparable<BigDecimal>, IComparable
{
    /// <summary>Tests two values for numeric equality.</summary>
    /// <remarks>
    /// Comparison is numeric, so values that differ only in scale compare equal: <c>1.0</c> equals
    /// <c>1.00</c>. Use <see cref="Scale"/> when the difference matters.
    /// </remarks>
    public static bool operator ==(BigDecimal left, BigDecimal right) => left.Equals(right);

    /// <summary>Tests two values for numeric inequality.</summary>
    public static bool operator !=(BigDecimal left, BigDecimal right) => !left.Equals(right);

    /// <summary>Tests whether the left value is less than the right one.</summary>
    public static bool operator <(BigDecimal left, BigDecimal right) => left.CompareTo(right) < 0;

    /// <summary>Tests whether the left value is less than or equal to the right one.</summary>
    public static bool operator <=(BigDecimal left, BigDecimal right) => left.CompareTo(right) <= 0;

    /// <summary>Tests whether the left value is greater than the right one.</summary>
    public static bool operator >(BigDecimal left, BigDecimal right) => left.CompareTo(right) > 0;

    /// <summary>Tests whether the left value is greater than or equal to the right one.</summary>
    public static bool operator >=(BigDecimal left, BigDecimal right) => left.CompareTo(right) >= 0;

    /// <summary>Tests this value for numeric equality with another.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> when the two are numerically equal, whatever their scales.</returns>
    public bool Equals(BigDecimal other) => CompareTo(other) == 0;

    /// <summary>Tests this value for numeric equality with another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is a <see cref="BigDecimal"/> numerically equal to this one.</returns>
    public override bool Equals(object? obj) => obj is BigDecimal other && Equals(other);

    /// <summary>Returns a hash code consistent with numeric equality.</summary>
    /// <remarks>
    /// Values that are equal but differ in scale hash alike, so <c>1.0</c> and <c>1.00</c> land in
    /// the same dictionary bucket.
    /// </remarks>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        Span<ulong> magnitude = stackalloc ulong[WordCount + 1];
        var len = CopyMagnitude(magnitude);
        var scale = Scale;

        while (scale > 0 && len > 0)
        {
            len = Words.DivRemSmall(magnitude, len, 10, out var remainder);
            if (remainder != 0)
            {
                len = Words.MulAddSmall(magnitude, len, 10, remainder);
                break;
            }

            scale--;
        }

        if (len == 0)
        {
            return 0;
        }

        HashCode hash = default;
        for (var i = 0; i < len; i++)
        {
            hash.Add(magnitude[i]);
        }

        hash.Add(scale);
        hash.Add(IsNegative);
        return hash.ToHashCode();
    }

    /// <summary>Compares this value with another.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>A negative number, zero or a positive number as this value is less than, equal to, or greater than <paramref name="other"/>.</returns>
    public int CompareTo(BigDecimal other)
    {
        var leftSign = Sign;
        var rightSign = other.Sign;
        if (leftSign != rightSign)
        {
            return leftSign < rightSign ? -1 : 1;
        }

        if (leftSign == 0)
        {
            return 0;
        }

        var magnitudeCompare = CompareMagnitude(this, other);
        return leftSign > 0 ? magnitudeCompare : -magnitudeCompare;
    }

    /// <summary>Compares this value with another object.</summary>
    /// <param name="obj">The object to compare with, or <see langword="null"/>.</param>
    /// <returns>A negative number, zero or a positive number as this value is less than, equal to, or greater than <paramref name="obj"/>. A null object sorts first.</returns>
    /// <exception cref="ArgumentException"><paramref name="obj"/> is neither <see langword="null"/> nor a <see cref="BigDecimal"/>.</exception>
    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        BigDecimal other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(BigDecimal)}.", nameof(obj)),
    };

    private static int CompareMagnitude(BigDecimal left, BigDecimal right)
    {
        if (left.Scale == right.Scale)
        {
            Span<ulong> shortA = stackalloc ulong[WordCount];
            Span<ulong> shortB = stackalloc ulong[WordCount];
            var shortALen = left.CopyMagnitude(shortA);
            var shortBLen = right.CopyMagnitude(shortB);
            return Words.Compare(shortA, shortALen, shortB, shortBLen);
        }

        Span<ulong> a = stackalloc ulong[WorkWords];
        Span<ulong> b = stackalloc ulong[WorkWords];
        a.Clear();
        b.Clear();
        var aLen = left.CopyMagnitude(a);
        var bLen = right.CopyMagnitude(b);

        if (left.Scale < right.Scale)
        {
            aLen = Words.ScaleUp(a, aLen, right.Scale - left.Scale);
        }
        else
        {
            bLen = Words.ScaleUp(b, bLen, left.Scale - right.Scale);
        }

        return Words.Compare(a, aLen, b, bLen);
    }
}
