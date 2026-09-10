using PetToys.BigDecimal.Numerics;

namespace BigDecimalProbes;

/// <summary>
/// A value inside an object, which is how a caller usually meets the converter.
/// </summary>
internal sealed class Holder
{
    public BigDecimal Amount { get; set; }
}
