using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The arithmetic at mantissa widths <see cref="decimal"/> cannot represent at all.
/// </summary>
/// <remarks>
/// <para>
/// No baseline is declared and no ratio is reported, because there is nothing to divide by: these
/// are the widths the package exists for, and no other .NET numeric type holds them. The rows are
/// a cost record, so that a change in the three- and four-word paths is visible even though no
/// budget is stated over them.
/// </para>
/// <para>
/// The division and remainder rows are read together with the one- and two-word rows of
/// <see cref="DivideBenchmarks"/>: the same single-word divisor at four widths of dividend, which
/// is the cost of dividing by one word as the dividend grows. They once measured code known to
/// return wrong values at these widths, which is no longer so - that defect was fixed with the
/// division primitive, and the randomised suite covers these shapes.
/// </para>
/// <para>
/// At the widest shape the aligned product needs more than the 256-bit mantissa can hold, so the
/// result is normalised to 77 significant digits. That is the documented behaviour and a real path
/// through the multiplier, not an error.
/// </para>
/// </remarks>
[BenchmarkCategory(BenchmarkCategories.Budget)]
public class WideOperandBenchmarks
{
    private BigDecimal _left;
    private BigDecimal _right;
    private BigDecimal _factorLeft;
    private BigDecimal _factorRight;
    private BigDecimal _dividend;
    private BigDecimal _divisor;

    /// <summary>The mantissa width the operands are drawn at.</summary>
    [Params(OperandShape.ThreeWords, OperandShape.FourWords)]
    public OperandShape Shape { get; set; }

    /// <summary>Whether the two operands carry the same scale.</summary>
    [Params(ScalePairing.Aligned, ScalePairing.Misaligned)]
    public ScalePairing Pairing { get; set; }

    /// <summary>Parses the operands, so that only the operations themselves are measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var (left, right) = Operands.Additive(Shape, Pairing);
        _left = BigDecimal.Parse(left, CultureInfo.InvariantCulture);
        _right = BigDecimal.Parse(right, CultureInfo.InvariantCulture);

        var (factorLeft, factorRight) = Operands.Multiplicative(Shape, Pairing);
        _factorLeft = BigDecimal.Parse(factorLeft, CultureInfo.InvariantCulture);
        _factorRight = BigDecimal.Parse(factorRight, CultureInfo.InvariantCulture);

        var (dividend, divisor) = Operands.Divisive(Shape, Pairing);
        _dividend = BigDecimal.Parse(dividend, CultureInfo.InvariantCulture);
        _divisor = BigDecimal.Parse(divisor, CultureInfo.InvariantCulture);
    }

    /// <summary>Addition at a width no baseline exists for.</summary>
    /// <returns>The sum, returned so that the operation is not elided.</returns>
    [Benchmark]
    public BigDecimal Add() => _left + _right;

    /// <summary>Subtraction at a width no baseline exists for.</summary>
    /// <returns>The difference, returned so that the operation is not elided.</returns>
    [Benchmark]
    public BigDecimal Subtract() => _left - _right;

    /// <summary>Multiplication at a width no baseline exists for.</summary>
    /// <returns>The product, returned so that the operation is not elided.</returns>
    [Benchmark]
    public BigDecimal Multiply() => _factorLeft * _factorRight;

    /// <summary>Division at a width no baseline exists for, and where the result is not trusted.</summary>
    /// <returns>The quotient, returned so that the operation is not elided.</returns>
    [Benchmark]
    public BigDecimal Divide() => _dividend / _divisor;

    /// <summary>Remainder at a width no baseline exists for, and where the result is not trusted.</summary>
    /// <returns>The remainder, returned so that the operation is not elided.</returns>
    [Benchmark]
    public BigDecimal Remainder() => _dividend % _divisor;
}
