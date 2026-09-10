using System.Diagnostics.CodeAnalysis;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>A row whose value is an array column.</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Materialised by Dapper.")]
public sealed class ArrayRow
{
    /// <summary>The row's identifier.</summary>
    public int Id { get; set; }

    /// <summary>The values.</summary>
#pragma warning disable CA1819 // Dapper materialises the column as the array the adapter maps.
    public BigDecimal[]? A { get; set; }
#pragma warning restore CA1819
}
