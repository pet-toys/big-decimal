namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// How a quotient comes out, which is what decides how many times the division searches before it
/// settles on a scale.
/// </summary>
/// <remarks>
/// The three cases are the three depths of that search, over one dividend so that nothing but the
/// depth differs between the rows. <see cref="Exact"/> answers at the first, <see cref="Factored"/>
/// at the second, and <see cref="Inexact"/> reaches the full-precision pass having failed both.
/// </remarks>
public enum DivisionExactness
{
    /// <summary>The quotient is exact at the difference of the operands' scales.</summary>
    Exact,

    /// <summary>
    /// The quotient is exact, but only once the dividend is lifted by the divisor's own factors.
    /// </summary>
    Factored,

    /// <summary>The quotient is exact at no scale and is rounded from the final remainder.</summary>
    Inexact,
}
