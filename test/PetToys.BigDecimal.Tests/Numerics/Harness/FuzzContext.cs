using System.Globalization;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Everything a reader needs to reproduce one randomised case: the seed the batch was drawn from,
/// the case's position inside it, and the operands.
/// </summary>
/// <param name="Seed">The seed the batch was drawn from.</param>
/// <param name="Case">The case's position within the batch, from zero.</param>
/// <param name="Left">The left operand.</param>
/// <param name="Right">The right operand, where there is one.</param>
public readonly record struct FuzzContext(int Seed, int Case, FuzzValue Left, FuzzValue? Right)
{
    /// <summary>Describes a case with one operand.</summary>
    /// <param name="seed">The seed the batch was drawn from.</param>
    /// <param name="index">The case's position within the batch.</param>
    /// <param name="value">The operand.</param>
    /// <returns>The context.</returns>
    public static FuzzContext Of(int seed, int index, FuzzValue value) => new(seed, index, value, null);

    /// <summary>Describes a case with two operands.</summary>
    /// <param name="seed">The seed the batch was drawn from.</param>
    /// <param name="index">The case's position within the batch.</param>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>The context.</returns>
    public static FuzzContext Of(int seed, int index, FuzzValue left, FuzzValue right) =>
        new(seed, index, left, right);

    /// <summary>Renders the context for a failure message.</summary>
    /// <returns>The seed, the case and the operands.</returns>
    public override string ToString() => Right is { } right
        ? string.Create(CultureInfo.InvariantCulture, $"[seed {Seed} case {Case}] left {Left}, right {right}:")
        : string.Create(CultureInfo.InvariantCulture, $"[seed {Seed} case {Case}] value {Left}:");
}
