namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// How the scales of a generated pair of operands relate, which is what decides whether an
/// operation has to align them and how far.
/// </summary>
public enum ScaleRelationship
{
    /// <summary>Both operands carry the same scale, so no alignment is needed.</summary>
    Equal,

    /// <summary>The scales differ by one, the cheapest alignment there is.</summary>
    OffByOne,

    /// <summary>
    /// The scales differ by more than the magnitude can absorb, so aligning them costs significant
    /// digits and the result is rounded.
    /// </summary>
    FarApart,
}
