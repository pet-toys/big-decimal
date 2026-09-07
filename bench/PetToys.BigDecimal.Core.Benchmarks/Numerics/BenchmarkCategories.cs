namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The categories a run can be filtered on.
/// </summary>
/// <remarks>
/// The set of benchmarks a criterion is read from is declared here, on the classes themselves,
/// rather than in a command line kept in a document. A class that stops answering to a criterion,
/// or a new one that starts, changes the subset by gaining or losing an attribute, and the command
/// that runs the subset never has to be rewritten.
/// </remarks>
public static class BenchmarkCategories
{
    /// <summary>
    /// The benchmarks an acceptance criterion is read from: the four ratio budgets, the
    /// relationship between an exact division and an inexact one, the ceiling on normalising a
    /// scale away, and the widths the single-word division curve is read across.
    /// </summary>
    /// <remarks>
    /// Run them with <c>--anyCategories budget</c>. What it leaves out is everything measured only
    /// so that a change in it is visible: the conversions, the scale changes, and the group that
    /// measures the division primitive against the form it replaced.
    /// </remarks>
    public const string Budget = "budget";
}
