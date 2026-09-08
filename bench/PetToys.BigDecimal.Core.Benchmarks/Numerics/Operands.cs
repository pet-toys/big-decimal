using System;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The committed operand set every benchmark draws from.
/// </summary>
/// <remarks>
/// <para>
/// The values are literals and nothing here consults a clock, an environment variable or a random
/// number generator: two runs of the same benchmark must do the same work, or the numbers they
/// produce cannot be compared and the recorded baseline means nothing.
/// </para>
/// <para>
/// The set is deliberately not shared with the verification harness in the test project. That
/// generator's contract is to randomise, which is the one thing a benchmark may not do; drawing
/// from it with a fixed seed would make comparability depend on the generator never changing.
/// A project reference would also pull xunit into this assembly, and BenchmarkDotNet copies the
/// assembly's dependencies into every job it generates.
/// </para>
/// <para>
/// The operands for the shapes that fit <see cref="decimal"/> are chosen so that the
/// <see cref="decimal"/> baseline computes its result exactly, without rounding to fit 96 bits.
/// A baseline that silently rounds would be measuring less work than the benchmark it anchors.
/// </para>
/// </remarks>
public static class Operands
{
    /// <summary>The dividend all three division-exactness cases are measured over.</summary>
    public const string ExactnessDividend = "100";

    /// <summary>The divisor that makes <see cref="ExactnessDividend"/> divide exactly.</summary>
    public const string ExactDivisor = "10";

    /// <summary>
    /// The divisor that divides <see cref="ExactnessDividend"/> exactly, but only after the
    /// dividend is lifted by the divisor's own factors.
    /// </summary>
    /// <remarks>
    /// Eight is 2^3 and nothing else, so lifting the dividend by three decimal places makes the
    /// division come out exactly, whatever the dividend is. The quotient itself may need fewer
    /// places than that - 100 over 8 is 12.5 - and it is not exact at the scale difference, where
    /// 100 over 8 leaves a remainder of 4. That is the shape the search has to go two depths for,
    /// and the only one of the three that is not already in the report.
    /// </remarks>
    public const string FactorDivisor = "8";

    /// <summary>The divisor that makes <see cref="ExactnessDividend"/> divide inexactly.</summary>
    public const string InexactDivisor = "3";

    /// <summary>
    /// The format strings the formatting benchmarks are measured over: every standard specifier
    /// the type accepts and one custom format.
    /// </summary>
    /// <remarks>
    /// A row per format string, because the budget is read per format string rather than as an
    /// average over them: a specifier a caller chose deliberately can be over 3x while the mean of
    /// the others hides it. <c>R</c> is here even though it routes to the same formatter as
    /// <c>G</c>, since what it costs is a question about the dispatch rather than about the
    /// formatter, and <c>G6</c> is here because a precision makes <c>G</c> a different formatter
    /// altogether. The custom format is the shape a caller actually writes: grouped, with two
    /// fractional digits.
    /// </remarks>
    public static readonly string[] Formats = ["G", "R", "G6", "F9", "N2", "E4", "C2", "P2", "#,##0.00"];

    /// <summary>
    /// The operands for addition and subtraction: a sum that stays inside the shape it was drawn
    /// at, so that the measurement is of the operation and not of an overflow path.
    /// </summary>
    /// <param name="shape">The mantissa width to draw at.</param>
    /// <param name="pairing">Whether the two scales agree.</param>
    /// <returns>The left and right operands, as text.</returns>
    public static (string Left, string Right) Additive(OperandShape shape, ScalePairing pairing) => shape switch
    {
        OperandShape.OneWord => (
            "1234567890.123456789",
            pairing is ScalePairing.Aligned ? "9876543210.987654321" : "9876543210.9876"),
        OperandShape.TwoWords => (
            "12345678901234567890.123456789",
            pairing is ScalePairing.Aligned ? "23456789012345678901.234567891" : "23456789012345678901.2345"),
        OperandShape.ThreeWords => (
            "123456789012345678901234567890123456.789012345",
            pairing is ScalePairing.Aligned
                ? "234567890123456789012345678901234567.890123456"
                : "234567890123456789012345678901234567.8901"),
        OperandShape.FourWords => (
            "1234567890123456789012345678901234567890123456789012345678901.234567890",
            pairing is ScalePairing.Aligned
                ? "2345678901234567890123456789012345678901234567890123456789012.345678901"
                : "2345678901234567890123456789012345678901234567890123456789012.3456"),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>
    /// The operands for multiplication. The right operand is small and numerically the same in
    /// both pairings, so that the product of the two widest shapes still has an integer part the
    /// mantissa can hold and the <see cref="decimal"/> baseline is exact.
    /// </summary>
    /// <param name="shape">The mantissa width to draw the left operand at.</param>
    /// <param name="pairing">Whether the two scales agree.</param>
    /// <returns>The left and right operands, as text.</returns>
    public static (string Left, string Right) Multiplicative(OperandShape shape, ScalePairing pairing) => shape switch
    {
        OperandShape.OneWord => (
            "1234567.89",
            pairing is ScalePairing.Aligned ? "0.35" : "0.3500"),
        OperandShape.TwoWords => (
            "12345678901234567890.12345",
            pairing is ScalePairing.Aligned ? "0.35000" : "0.35"),
        OperandShape.ThreeWords => (
            "123456789012345678901234567890123456.789012345",
            pairing is ScalePairing.Aligned ? "0.350000000" : "0.35"),
        OperandShape.FourWords => (
            "1234567890123456789012345678901234567890123456789012345678901.234567890",
            pairing is ScalePairing.Aligned ? "0.350000000" : "0.35"),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>
    /// The operands for division and remainder. The divisor is the same value in both pairings and
    /// differs only in the trailing zeros that set its scale.
    /// </summary>
    /// <remarks>
    /// The divisor's mantissa fits a single 64-bit word at every shape, and has to keep doing so.
    /// Division by a single word is the step that the trailing-zero strip, digit peeling and the
    /// trial quotient of a wide division are all built on, and these four rows - one and two words
    /// here, three and four in the wide benchmarks - are how its cost per additional word of
    /// dividend is read off the report. Widening the divisor turns them into a measurement of the
    /// multi-word loop instead, and the curve stops being about that step at all.
    /// </remarks>
    /// <param name="shape">The mantissa width to draw the dividend at.</param>
    /// <param name="pairing">Whether the two scales agree.</param>
    /// <returns>The left and right operands, as text.</returns>
    public static (string Left, string Right) Divisive(OperandShape shape, ScalePairing pairing) => shape switch
    {
        OperandShape.OneWord => (
            "1234567890.123456789",
            pairing is ScalePairing.Aligned ? "9876.543210000" : "9876.54321"),
        OperandShape.TwoWords => (
            "12345678901234567890.123456789",
            pairing is ScalePairing.Aligned ? "98765.432100000" : "98765.4321"),
        OperandShape.ThreeWords => (
            "123456789012345678901234567890123456.789012345",
            pairing is ScalePairing.Aligned ? "98765.432100000" : "98765.4321"),
        OperandShape.FourWords => (
            "1234567890123456789012345678901234567890123456789012345678901.234567890",
            pairing is ScalePairing.Aligned ? "98765.432100000" : "98765.4321"),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>The text a parsing benchmark reads, and the value a formatting one renders.</summary>
    /// <param name="shape">The mantissa width to draw at.</param>
    /// <returns>The value, as text.</returns>
    public static string Value(OperandShape shape) => Additive(shape, ScalePairing.Aligned).Left;
}
