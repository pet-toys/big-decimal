using System.Globalization;
using System.Numerics;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// A decimal value as the oracle carries it: an exact signed mantissa and a scale, with the
/// numeric value being <c>Unscaled * 10^-Scale</c>.
/// </summary>
/// <remarks>
/// Both halves are compared. Two values that are numerically equal but carry different scales are
/// different <see cref="OracleValue"/>s, which is the point — scale is half of what
/// <see cref="BigDecimal"/> promises, and an oracle that reduced to a rational number would be
/// blind to exactly the half that is hardest to get right.
/// </remarks>
/// <param name="Unscaled">The signed mantissa.</param>
/// <param name="Scale">The scale, from 0 to 255.</param>
public readonly record struct OracleValue(BigInteger Unscaled, int Scale)
{
    /// <summary>The sign of the value: -1, 0 or 1.</summary>
    public int Sign => Unscaled.Sign;

    /// <summary>Takes the description of a generated value.</summary>
    /// <param name="value">The generated value.</param>
    /// <returns>Its mantissa and scale.</returns>
    public static OracleValue From(FuzzValue value) => new(value.Unscaled, value.Scale);

    /// <summary>Builds a value from an integer at scale zero.</summary>
    /// <param name="value">The integer.</param>
    /// <returns>The value at scale zero.</returns>
    public static OracleValue From(long value) => new(value, 0);

    /// <summary>
    /// Reads what the type under test actually produced. This is an observation of the result, not
    /// a source of expected values — the oracle never calls it to compute one.
    /// </summary>
    /// <param name="value">The value to describe.</param>
    /// <returns>Its mantissa and scale.</returns>
    public static OracleValue Observe(BigDecimal value) => new(value.GetMantissa(), value.Scale);

    /// <summary>Describes the value for a failure message.</summary>
    /// <returns>The mantissa and the scale.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Unscaled}e-{Scale}");
}
