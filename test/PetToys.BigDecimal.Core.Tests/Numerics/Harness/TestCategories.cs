namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The trait vocabulary the test runs are filtered on. The continuous integration workflow
/// selects everything except <see cref="Integration"/>; <see cref="Fuzz"/> exists so that the
/// randomised tests can be dropped from that run by adding one clause to the same filter,
/// without taking the deterministic suites with them.
/// </summary>
public static class TestCategories
{
    /// <summary>The trait name both categories are published under.</summary>
    public const string TraitName = "Category";

    /// <summary>
    /// Tests whose cases are drawn from a seed. Applied by <see cref="FuzzDataAttribute"/> rather
    /// than by hand, so that a randomised test cannot be written without it.
    /// </summary>
    public const string Fuzz = "Fuzz";

    /// <summary>Tests that need a database container. Excluded from continuous integration.</summary>
    public const string Integration = "Integration";
}
