using System.Diagnostics.CodeAnalysis;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>A row whose value admits NULL.</summary>
[SuppressMessage("Performance", "CA1812", Justification = "Materialised by Dapper.")]
public sealed class NullableNumericRow
{
    /// <summary>The row's identifier.</summary>
    public int Id { get; set; }

    /// <summary>The value, or <see langword="null"/>.</summary>
    public BigDecimal? V { get; set; }
}
