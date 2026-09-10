namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>A row whose value is the mapped type.</summary>
public sealed class DecimalRow
{
    /// <summary>The row's identifier.</summary>
    public int Id { get; set; }

    /// <summary>The value.</summary>
    public BigDecimal V { get; set; }
}
