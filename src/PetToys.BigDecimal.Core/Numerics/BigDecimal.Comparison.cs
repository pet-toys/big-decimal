using System;

namespace PetToys.BigDecimal.Numerics;

public readonly partial struct BigDecimal : IEquatable<BigDecimal>, IComparable<BigDecimal>, IComparable
{
    /// <summary>Tests two values for numeric equality.</summary>
    /// <remarks>
    /// Values that differ only in scale compare equal: <c>1.0</c> equals <c>1.00</c>. False
    /// whenever either side is <see cref="NaN"/>, where <see cref="Equals(BigDecimal)"/> is true
    /// for two NaNs; the two disagree as they do for <see cref="double"/>, the operator following
    /// IEEE 754 and <see cref="Equals(BigDecimal)"/> the total order a dictionary needs.
    /// </remarks>
    public static bool operator ==(BigDecimal left, BigDecimal right) =>
        !IsNaN(left) && !IsNaN(right) && left.Equals(right);

    /// <summary>Tests two values for numeric inequality.</summary>
    /// <remarks>True whenever either side is <see cref="NaN"/>, including two NaNs.</remarks>
    public static bool operator !=(BigDecimal left, BigDecimal right) => !(left == right);

    /// <summary>Tests whether the left value is less than the right one.</summary>
    /// <remarks>False whenever either side is <see cref="NaN"/>, unlike <see cref="CompareTo(BigDecimal)"/>.</remarks>
    public static bool operator <(BigDecimal left, BigDecimal right) =>
        AreOrdered(left, right) && left.CompareTo(right) < 0;

    /// <summary>Tests whether the left value is less than or equal to the right one.</summary>
    /// <remarks>False whenever either side is <see cref="NaN"/>.</remarks>
    public static bool operator <=(BigDecimal left, BigDecimal right) =>
        AreOrdered(left, right) && left.CompareTo(right) <= 0;

    /// <summary>Tests whether the left value is greater than the right one.</summary>
    /// <remarks>False whenever either side is <see cref="NaN"/>.</remarks>
    public static bool operator >(BigDecimal left, BigDecimal right) =>
        AreOrdered(left, right) && left.CompareTo(right) > 0;

    /// <summary>Tests whether the left value is greater than or equal to the right one.</summary>
    /// <remarks>False whenever either side is <see cref="NaN"/>.</remarks>
    public static bool operator >=(BigDecimal left, BigDecimal right) =>
        AreOrdered(left, right) && left.CompareTo(right) >= 0;

    /// <summary>Tests this value for numeric equality with another.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> when the two are numerically equal, whatever their scales.</returns>
    /// <remarks>True for two <see cref="NaN"/> values, where <c>operator ==</c> is false.</remarks>
    public bool Equals(BigDecimal other) => CompareTo(other) == 0;

    /// <summary>Tests this value for numeric equality with another object.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><see langword="true"/> when <paramref name="obj"/> is a <see cref="BigDecimal"/> numerically equal to this one.</returns>
    public override bool Equals(object? obj) => obj is BigDecimal other && Equals(other);

    /// <summary>Returns a hash code consistent with numeric equality.</summary>
    /// <remarks>
    /// Values that are equal but differ in scale hash alike, so <c>1.0</c> and <c>1.00</c> land in
    /// the same dictionary bucket. That agreement sends every hash through the shortest form, so a
    /// value with no trailing zeros costs 12.7x to 16.1x <see cref="decimal"/>'s hash, which strips
    /// them for the same reason, and one widened to a database column's scale about 2x as much.
    /// </remarks>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        if (IsNonFinite)
        {
            // One bit pattern per value. Unchecked: negative infinity's flags word has bit 31 set.
            return unchecked((int)_flags);
        }

        Span<ulong> magnitude = stackalloc ulong[WordCount + 1];
        var len = CopyMagnitude(magnitude);
        var scale = Scale;

        len = StripTrailingZeros(magnitude, len, ref scale, 0);

        if (len == 0)
        {
            return 0;
        }

        HashCode hash = default;
        for (var i = 0; i < len; i++)
        {
            hash.Add(magnitude[i]);
        }

        // The sign rides in the low bit of the scale.
        hash.Add((scale << 1) | (IsNegative ? 1 : 0));
        return hash.ToHashCode();
    }

    /// <summary>Compares this value with another.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>A negative number, zero or a positive number as this value is less than, equal to, or greater than <paramref name="other"/>.</returns>
    /// <remarks>
    /// A total order, as <see cref="Array.Sort{T}(T[])"/> requires: <see cref="NaN"/> compares
    /// equal to itself and less than every other value, <see cref="NegativeInfinity"/> included,
    /// which is <see cref="double"/>'s order. PostgreSQL sorts its <c>numeric</c> NaN above every
    /// value instead.
    /// </remarks>
    public int CompareTo(BigDecimal other)
    {
        if (IsNonFinite || other.IsNonFinite)
        {
            return OrderRank(this).CompareTo(OrderRank(other));
        }

        var leftSign = FiniteSign;
        var rightSign = other.FiniteSign;
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

    // NaN, then negative infinity, then every finite value, then positive infinity. Only reached
    // when at least one side is non-finite.
    private static int OrderRank(BigDecimal value)
    {
        if (IsNaN(value))
        {
            return -2;
        }

        if (IsNegativeInfinity(value))
        {
            return -1;
        }

        return IsPositiveInfinity(value) ? 1 : 0;
    }

    private static bool AreOrdered(BigDecimal left, BigDecimal right) =>
        !IsNaN(left) && !IsNaN(right);

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
