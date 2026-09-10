namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>A row mixing both, which is what a partial migration looks like.</summary>
public sealed class MixedRow
{
    /// <summary>A value the caller left as <see cref="decimal"/>.</summary>
    public decimal Narrow { get; set; }

    /// <summary>A value the caller moved to the mapped type.</summary>
    public BigDecimal Wide { get; set; }
}
