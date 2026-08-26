namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// How the scales of a benchmark's two operands relate, which is what decides whether the
/// operation has to widen one of them before it can start.
/// </summary>
/// <remarks>
/// The two pairings are chosen so that only the scale differs: the right operand of a
/// <see cref="Misaligned"/> pair is numerically equal to, or the same value truncated from, the
/// right operand of the <see cref="Aligned"/> one. A row pair that differed in magnitude as well
/// would not isolate the cost of alignment.
/// </remarks>
public enum ScalePairing
{
    /// <summary>Both operands carry the same scale, so nothing has to be widened.</summary>
    Aligned,

    /// <summary>The scales differ, so one operand is widened before the operation proper.</summary>
    Misaligned,
}
