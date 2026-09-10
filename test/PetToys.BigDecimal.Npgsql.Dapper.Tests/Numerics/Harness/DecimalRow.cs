namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>A row a caller never changed, still asking for <see cref="decimal"/>.</summary>
public sealed class DecimalRow
{
    /// <summary>The row's identifier.</summary>
    public int Id { get; set; }

    /// <summary>The value, as the caller's own code has always typed it.</summary>
    public decimal V { get; set; }
}
