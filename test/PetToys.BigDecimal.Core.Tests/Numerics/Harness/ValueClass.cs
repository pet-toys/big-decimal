namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The kinds of value <see cref="ValueGenerator"/> draws from, named because arithmetic breaks at
/// them rather than because they are convenient.
/// </summary>
/// <remarks>
/// A uniformly random 256-bit magnitude is four words wide essentially always, never within one of
/// a word boundary, and never an exact power of ten. Every class below exists to cover ground that
/// a uniform draw never reaches.
/// </remarks>
public enum ValueClass
{
    /// <summary>A magnitude occupying one 64-bit word.</summary>
    OneWord,

    /// <summary>A magnitude occupying two 64-bit words.</summary>
    TwoWords,

    /// <summary>A magnitude occupying three 64-bit words.</summary>
    ThreeWords,

    /// <summary>A magnitude occupying all four 64-bit words.</summary>
    FourWords,

    /// <summary>A magnitude one below a word boundary, where an addition carries into a new word.</summary>
    BelowWordBoundary,

    /// <summary>A magnitude exactly at a word boundary, where a subtraction borrows out of one.</summary>
    AtWordBoundary,

    /// <summary>Zero, at a scale that is not always zero.</summary>
    Zero,

    /// <summary>An exact power of ten, where scaling is a shift of the decimal point.</summary>
    PowerOfTen,

    /// <summary>A value whose magnitude ends in zeros, which scale reduction can strip.</summary>
    TrailingZeros,

    /// <summary>The largest magnitude the type holds, positive or negative.</summary>
    Extremum,
}
