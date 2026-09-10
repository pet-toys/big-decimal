using System.Collections.Generic;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The entity every test in this suite maps, carrying one property per shape the package answers
/// for.
/// </summary>
public sealed class NumericItem
{
    /// <summary>The key, assigned by the test rather than by the server.</summary>
    public int Id { get; set; }

    /// <summary>An unconstrained <c>numeric</c> column.</summary>
    public BigDecimal Amount { get; set; }

    /// <summary>A column the model declares as <c>numeric(12,4)</c>.</summary>
    public BigDecimal Faceted { get; set; }

    /// <summary>A column whose precision equals its scale, which is the awkward boundary.</summary>
    public BigDecimal Tight { get; set; }

    /// <summary>A nullable property, which needs no mapping of its own.</summary>
    public BigDecimal? Maybe { get; set; }

    /// <summary>The array form.</summary>
    public BigDecimal[] Amounts { get; set; } = [];

    /// <summary>The list form.</summary>
    public List<BigDecimal> More { get; set; } = [];

    /// <summary>The array form with a nullable element.</summary>
    public BigDecimal?[] Sparse { get; set; } = [];

    /// <summary>The list form with a nullable element.</summary>
    public List<BigDecimal?> SparseList { get; set; } = [];
}
