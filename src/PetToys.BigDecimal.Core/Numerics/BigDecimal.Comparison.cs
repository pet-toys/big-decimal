using System;

namespace PetToys.BigDecimal.Numerics;

public readonly partial struct BigDecimal : IEquatable<BigDecimal>, IComparable<BigDecimal>, IComparable
{
    /// <summary>Tests two values for numeric equality.</summary>
    /// <remarks>
    /// <para>
    /// Comparison is numeric, so values that differ only in scale compare equal: <c>1.0</c> equals
    /// <c>1.00</c>. Use <see cref="Scale"/> when the difference matters.
    /// </para>
    /// <para>
    /// This operator is false whenever either side is <see cref="NaN"/>, including when both are,
    /// while <see cref="Equals(BigDecimal)"/> is true for two NaNs. The two deliberately
    /// disagree, as they do for <see cref="double"/>: the operator follows IEEE 754 and
    /// <see cref="Equals(BigDecimal)"/> follows the total order that
    /// <c>Dictionary</c> and <see cref="Array.Sort{T}(T[])"/> need.
    /// </para>
    /// </remarks>
    public static bool operator ==(BigDecimal left, BigDecimal right) =>
        !IsNaN(left) && !IsNaN(right) && left.Equals(right);

    /// <summary>Tests two values for numeric inequality.</summary>
    /// <remarks>True whenever either side is <see cref="NaN"/>, including two NaNs.</remarks>
    public static bool operator !=(BigDecimal left, BigDecimal right) => !(left == right);

    /// <summary>Tests whether the left value is less than the right one.</summary>
    /// <remarks>False whenever either side is <see cref="NaN"/>, which is why the four
    /// relational operators are not defined through <see cref="CompareTo(BigDecimal)"/>: that
    /// orders NaN rather than leaving it unordered.</remarks>
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
    /// <remarks>
    /// True for two <see cref="NaN"/> values, where <c>operator ==</c> is false. That split is
    /// <see cref="double"/>'s and is what lets a NaN be found again as a dictionary key.
    /// </remarks>
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
        if (IsNonFinite)
        {
            // Three values, three flag words, and every one of them far from the zero a
            // magnitude of zero would otherwise hash to. Equal values hash alike because there
            // is exactly one bit pattern per non-finite value. Unchecked because negative
            // infinity's flags word has bit 31 set and Debug builds check their conversions.
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

        // The sign rides in the low bit of the scale rather than in a round of its own. The scale
        // is at most MaxScale after the strip, so the two cannot collide, and a value of zero has
        // already returned above, which is what keeps a negative zero from hashing apart from a
        // positive one.
        hash.Add((scale << 1) | (IsNegative ? 1 : 0));
        return hash.ToHashCode();
    }

    /// <summary>Compares this value with another.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns>A negative number, zero or a positive number as this value is less than, equal to, or greater than <paramref name="other"/>.</returns>
    /// <remarks>
    /// A total order, which is what <see cref="Array.Sort{T}(T[])"/> requires and what the
    /// relational operators deliberately are not: <see cref="NaN"/> compares equal to itself and
    /// less than every other value, <see cref="NegativeInfinity"/> included. That is
    /// <see cref="double"/>'s order too. PostgreSQL sorts its <c>numeric</c> NaN the other way,
    /// above every value; the divergence is deliberate and is recorded in the database
    /// correspondence.
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

    // NaN below negative infinity, positive infinity above everything, every finite value in
    // one band between them. Only reached when at least one side is non-finite, so two finite
    // values never share the zero rank here.
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
