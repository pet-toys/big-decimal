namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Skip reasons for tests that assert documented behaviour the implementation does not yet
/// provide.
/// </summary>
/// <remarks>
/// Each constant names the work that owns the defect and will remove the skip. A pin states what
/// is required, never what the code currently does: asserting today's wrong answer would turn the
/// eventual fix into a red build whose cause is ambiguous, and would quietly bless the defect in
/// the meantime.
/// </remarks>
public static class Pending
{
    /// <summary>Owned by the formatting parity work.</summary>
    public const string Formatting = "Pending the formatting parity work.";

    /// <summary>
    /// Owned by the formatting parity work. The `N` specifier reads only the first element of
    /// <see cref="System.Globalization.NumberFormatInfo.NumberGroupSizes"/>, so a culture that
    /// groups three then two — the Indian convention, and what `hi-IN` actually carries — comes
    /// back grouped uniformly in threes where `decimal` groups it correctly.
    /// </summary>
    public const string NonUniformGroupSizes =
        "Pending the formatting parity work: only the first entry of NumberGroupSizes is honoured.";

    /// <summary>
    /// Owned by the parsing parity work. A leading group separator is rejected, matching
    /// <see cref="decimal"/> — except when the separator is itself whitespace, where it is consumed
    /// as leading white space and the value is accepted. <see cref="decimal"/> rejects it.
    /// </summary>
    public const string LeadingWhitespaceSeparator =
        "Pending the parsing parity work: a leading group separator that is whitespace is accepted.";

    /// <summary>Owned by the numeric conversion contracts work.</summary>
    public const string NumericContracts = "Pending the numeric conversion contracts work.";
}
