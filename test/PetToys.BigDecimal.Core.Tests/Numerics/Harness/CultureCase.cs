namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>The cultures every parsing and formatting test runs under.</summary>
/// <remarks>
/// Between them they cover the ways a culture can disagree with the invariant one about a number:
/// which character separates the fraction, which separates the groups, and how wide the groups are.
/// </remarks>
public enum CultureCase
{
    /// <summary>The invariant culture: a dot for the fraction, no grouping in play.</summary>
    Invariant,

    /// <summary>A comma for the fraction and a dot for the groups, the way most of Europe writes.</summary>
    CommaDecimal,

    /// <summary>Groups of three then two, the way the Indian subcontinent writes.</summary>
    NonUniformGroups,

    /// <summary>A non-breaking space between groups, the way France and Scandinavia write.</summary>
    SpaceGroups,
}
