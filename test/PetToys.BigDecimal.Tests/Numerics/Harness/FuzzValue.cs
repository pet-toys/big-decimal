using System.Globalization;
using System.Numerics;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// A generated value together with the exact description an oracle needs: the signed unscaled
/// mantissa and the scale, from which the numeric value is <c>Unscaled * 10^-Scale</c>.
/// </summary>
/// <param name="Value">The value under test.</param>
/// <param name="Unscaled">The signed mantissa the value was built from.</param>
/// <param name="Scale">The scale the value was built with.</param>
/// <param name="Class">The class the value was drawn from, for coverage reporting.</param>
public readonly record struct FuzzValue(BigDecimal Value, BigInteger Unscaled, int Scale, ValueClass Class)
{
    /// <summary>Describes the value for a failure message, without going through formatting.</summary>
    /// <returns>The mantissa, the scale and the class.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Unscaled}e-{Scale} [{Class}]");
}
