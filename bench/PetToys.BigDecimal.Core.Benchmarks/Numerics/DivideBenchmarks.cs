using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Division, against <see cref="decimal"/>, at the two mantissa widths both types can hold.
/// </summary>
/// <remarks>
/// The <see cref="decimal"/> method is the declared baseline, so the report's ratio column is the
/// number the 10x budget is written in. Both methods operate on values parsed from the same
/// text in <see cref="Setup"/>, and both return their result, so neither operation can be removed
/// as dead code.
/// </remarks>
public class DivideBenchmarks
{
    private BigDecimal _left;
    private BigDecimal _right;
    private decimal _referenceLeft;
    private decimal _referenceRight;

    /// <summary>The mantissa width the operands are drawn at.</summary>
    [Params(OperandShape.OneWord, OperandShape.TwoWords)]
    public OperandShape Shape { get; set; }

    /// <summary>Whether the two operands carry the same scale.</summary>
    [Params(ScalePairing.Aligned, ScalePairing.Misaligned)]
    public ScalePairing Pairing { get; set; }

    /// <summary>Parses the operands, so that only the operation itself is measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var (left, right) = Operands.Divisive(Shape, Pairing);
        _left = BigDecimal.Parse(left, CultureInfo.InvariantCulture);
        _right = BigDecimal.Parse(right, CultureInfo.InvariantCulture);
        _referenceLeft = decimal.Parse(left, CultureInfo.InvariantCulture);
        _referenceRight = decimal.Parse(right, CultureInfo.InvariantCulture);
    }

    /// <summary>The same operation on <see cref="decimal"/>, which the budget is stated against.</summary>
    /// <returns>The result, returned so that the operation is not elided.</returns>
    [Benchmark(Baseline = true)]
    public decimal Baseline() => _referenceLeft / _referenceRight;

    /// <summary>The operation under budget.</summary>
    /// <returns>The result, returned so that the operation is not elided.</returns>
    [Benchmark]
    public BigDecimal Measured() => _left / _right;
}
