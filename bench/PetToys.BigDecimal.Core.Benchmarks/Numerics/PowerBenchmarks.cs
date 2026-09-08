using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Raising a value to an integer power, at each mantissa width and at two exponents three orders of
/// magnitude apart.
/// </summary>
/// <remarks>
/// There is no baseline method and no ratio column here on purpose. <see cref="decimal"/> has no
/// power operation, so nothing in this class has a counterpart to be a ratio of, and the criterion
/// this class answers is stated between two of its own rows instead: raising to the thousandth
/// costs at most 20x raising to the fourth, where a fold over multiplication would cost about 250x.
/// That is a guard on the shape of the curve rather than a budget on the speed, which is why the
/// ceiling sits so far above what the intended algorithm measures.
/// <para>
/// The reciprocal is its own row because it is the most expensive shape the operation has: it runs
/// the whole positive chain and then divides at the working width.
/// </para>
/// </remarks>
[BenchmarkCategory(BenchmarkCategories.Budget)]
public class PowerBenchmarks
{
    private BigDecimal _value;

    /// <summary>The mantissa width the base is drawn at.</summary>
    [Params(OperandShape.OneWord, OperandShape.TwoWords, OperandShape.FourWords)]
    public OperandShape Shape { get; set; }

    /// <summary>The exponent, at the two ends of the range the shape criterion is read across.</summary>
    [Params(4, 1000)]
    public int Exponent { get; set; }

    /// <summary>Parses the base, so that only the operation itself is measured.</summary>
    [GlobalSetup]
    public void Setup() => _value = BigDecimal.Parse(Operands.Power(Shape), CultureInfo.InvariantCulture);

    /// <summary>The power itself.</summary>
    /// <returns>The result, returned so that the operation is not elided.</returns>
    [Benchmark]
    public BigDecimal Power() => BigDecimal.Pow(_value, Exponent);

    /// <summary>The reciprocal, which pays for the chain and then for a division.</summary>
    /// <returns>The result, returned so that the operation is not elided.</returns>
    [Benchmark]
    public BigDecimal Reciprocal() => BigDecimal.Pow(_value, -Exponent);
}
