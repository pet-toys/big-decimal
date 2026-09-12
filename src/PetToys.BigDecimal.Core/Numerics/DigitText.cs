using System;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// A value decomposed for rendering: its significant digits, where the decimal point sits among
/// them, and whether it is negative.
/// </summary>
/// <remarks>
/// Scaling for <c>P</c>, <c>%</c>, <c>‰</c> and trailing commas is a move of <see cref="Point"/>
/// rather than arithmetic, since <c>MaxValue</c> times a hundred does not fit the mantissa, and
/// rounding happens after the move so it rounds the right digit. Zero is the empty digit string
/// with the point at one.
/// </remarks>
internal ref struct DigitText
{
    private readonly Span<char> digits;

    internal DigitText(Span<char> digits, int count, int point, bool negative)
    {
        this.digits = digits;
        Count = count;
        Point = point;
        Negative = negative;
    }

    /// <summary>How many significant digits are held; zero means the value is zero.</summary>
    internal int Count { get; private set; }

    /// <summary>How many digits belong before the decimal point.</summary>
    /// <remarks>Zero or negative for a value below one; past <see cref="Count"/> for an integer part ending in zeros.</remarks>
    internal int Point { get; private set; }

    /// <summary>Whether a sign belongs on the rendering.</summary>
    /// <remarks>Cleared when rounding takes the value to zero: -0.004 with <c>F1</c> is <c>0.0</c>.</remarks>
    internal bool Negative { get; private set; }

    internal readonly bool IsZero => Count == 0;

    /// <summary>Returns the digit at a position, or <c>'0'</c> for a position the digits do not reach.</summary>
    // Two comparisons rather than the unsigned trick: a Debug build checks the cast, and a
    // negative index is the ordinary case here.
    internal readonly char At(int index) => index >= 0 && index < Count ? digits[index] : '0';

    internal void RoundTo(int precision) => Keep(Point + precision);

    internal void RoundToSignificant(int significant) => Keep(significant);

    /// <summary>Drops the trailing zeros the rounding left behind, which only <c>G</c> with a precision does.</summary>
    internal void StripTrailingZeros()
    {
        while (Count > 0 && digits[Count - 1] == '0')
        {
            Count--;
        }

        if (Count == 0)
        {
            MakeZero();
        }
    }

    /// <summary>Moves the decimal point to the right, or to the left for a negative count.</summary>
    internal void Shift(int places)
    {
        if (!IsZero)
        {
            Point += places;
        }
    }

    // Rounds away from zero on the first digit dropped, as decimal does: 2.5 with F0 is 3.
    private void Keep(int count)
    {
        if (IsZero || count >= Count)
        {
            return;
        }

        if (count < 0)
        {
            MakeZero();
            return;
        }

        var roundUp = digits[count] >= '5';

        if (count == 0)
        {
            if (!roundUp)
            {
                MakeZero();
                return;
            }

            digits[0] = '1';
            Count = 1;
            Point++;
            return;
        }

        Count = count;
        if (!roundUp)
        {
            return;
        }

        for (var i = count - 1; i >= 0; i--)
        {
            if (digits[i] != '9')
            {
                digits[i]++;
                return;
            }

            digits[i] = '0';
        }

        // Every kept digit was a nine: 99 becomes 10 with the point one place further right.
        digits[0] = '1';
        Point++;
    }

    private void MakeZero()
    {
        Count = 0;
        Point = 1;
        Negative = false;
    }
}
