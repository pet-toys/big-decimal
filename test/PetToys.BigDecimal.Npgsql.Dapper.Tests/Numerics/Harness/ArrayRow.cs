using System.Diagnostics.CodeAnalysis;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>A row whose value is an array column.</summary>
public sealed class ArrayRow
{
    /// <summary>The row's identifier.</summary>
    public int Id { get; set; }

    /// <summary>The values.</summary>
    [SuppressMessage(
        "Performance",
        "CA1819",
        Justification = "Dapper materialises the column as the array the adapter maps.")]
    public BigDecimal[]? A { get; set; }
}
