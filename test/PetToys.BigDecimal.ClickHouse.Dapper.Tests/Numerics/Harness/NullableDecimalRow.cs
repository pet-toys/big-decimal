namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>A row whose value column may be NULL.</summary>
public sealed class NullableDecimalRow
{
    /// <summary>The row's identifier.</summary>
    public int Id { get; set; }

    /// <summary>The value.</summary>
    public BigDecimal? V { get; set; }
}
