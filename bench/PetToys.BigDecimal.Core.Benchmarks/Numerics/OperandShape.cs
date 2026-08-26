namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The width of the mantissa a benchmark's operands are drawn at, which is the dimension the
/// performance budgets are stated over.
/// </summary>
/// <remarks>
/// The first two shapes are the ones the budgets apply to: they fit <see cref="decimal"/>, so a
/// ratio against it exists. The last two do not fit it at all and are measured without a baseline,
/// because there is nothing to divide by.
/// </remarks>
public enum OperandShape
{
    /// <summary>A mantissa inside a single 64-bit word.</summary>
    OneWord,

    /// <summary>A mantissa spanning two words and still inside <see cref="decimal"/>'s 96 bits.</summary>
    TwoWords,

    /// <summary>A mantissa spanning three words, beyond anything <see cref="decimal"/> can hold.</summary>
    ThreeWords,

    /// <summary>A mantissa spanning all four words, up to the widest value the type represents.</summary>
    FourWords,
}
