using System;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// A value decomposed for rendering: its significant digits, where the decimal point sits among
/// them, and whether it is negative.
/// </summary>
/// <remarks>
/// Formatting works on this rather than on the value, so that the scaling <c>P</c> and a custom
/// format's <c>%</c>, <c>‰</c> and trailing commas call for is a move of <see cref="Point"/>
/// rather than arithmetic: <c>MaxValue</c> times a hundred does not fit the mantissa, yet
/// <see cref="decimal"/> renders its own maximum with <c>P0</c>. Rounding is here for the same
/// reason - the deciding digit sits at a different position before and after the shift, so
/// rounding the value first rounds the wrong one. Zero is the empty digit string with the point
/// at one, which renders a leading <c>0</c> with no special case at each use.
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
    /// <remarks>
    /// May be zero or negative, which is a value below one, and may exceed <see cref="Count"/>,
    /// which is an integer part that ends in zeros the digits do not carry.
    /// </remarks>
    internal int Point { get; private set; }

    /// <summary>Whether a sign belongs on the rendering.</summary>
    /// <remarks>Cleared when rounding takes the value to zero, because a rounded-away sign is not
    /// written: <see cref="decimal"/> renders -0.004 with <c>F1</c> as <c>0.0</c>.</remarks>
    internal bool Negative { get; private set; }

    /// <summary>Whether the value is zero.</summary>
    internal readonly bool IsZero => Count == 0;

    /// <summary>Returns the digit at a position, counting from the first significant one.</summary>
    /// <param name="index">The position. Outside the digits, the answer is a zero.</param>
    /// <returns>The digit, or <c>'0'</c> for a position the digits do not reach.</returns>
    /// <remarks>
    /// Positions before the first digit are the leading zeros of a value below one, and positions
    /// past the last are the trailing zeros of an integer part wider than the significand. Both
    /// are zeros, and answering them here keeps every writer free of the distinction.
    /// </remarks>
    // Two comparisons rather than the usual unsigned trick: the project builds Debug with
    // CheckForOverflowUnderflow, where casting a negative index to uint throws, and a negative
    // index is the ordinary case here rather than a bug.
    internal readonly char At(int index) => index >= 0 && index < Count ? digits[index] : '0';

    /// <summary>Rounds so that the given number of digits remain after the decimal point.</summary>
    /// <param name="precision">The count of fractional digits to keep.</param>
    internal void RoundTo(int precision) => Keep(Point + precision);

    /// <summary>Rounds to a count of significant digits.</summary>
    /// <param name="significant">The count of significant digits to keep.</param>
    internal void RoundToSignificant(int significant) => Keep(significant);

    /// <summary>Drops the trailing zeros the rounding left behind.</summary>
    /// <remarks><c>G</c> with a precision strips them; nothing else does.</remarks>
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

    /// <summary>Moves the decimal point, which is what scaling a rendering means.</summary>
    /// <param name="places">Places to move it to the right; negative moves it left.</param>
    internal void Shift(int places)
    {
        if (!IsZero)
        {
            Point += places;
        }
    }

    // Keeps the first count digits, rounding away from zero on the first digit dropped, which is
    // what decimal does: 0.5 renders as 1 with F0 and 2.5 renders as 3.
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

        // Every kept digit was a nine, so the carry runs off the front: 99 becomes 10 with the
        // point one place further right.
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
