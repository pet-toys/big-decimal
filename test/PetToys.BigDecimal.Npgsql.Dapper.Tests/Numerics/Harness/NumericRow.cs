using System.Diagnostics.CodeAnalysis;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>A row whose value is the mapped type.</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Materialised by Dapper.")]
public sealed class NumericRow
{
    /// <summary>The row's identifier.</summary>
    public int Id { get; set; }

    /// <summary>The value.</summary>
    public BigDecimal V { get; set; }
}
