using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Raising a base below one to a power, which is the regime where the chain's reduction is driven
/// by the scale rather than by the digit count.
/// </summary>
/// <remarks>
/// <see cref="PowerBenchmarks"/> cannot measure this path and never could. Its three bases are all
/// a little above one, so every intermediate the chain holds is at least one, and a working value
/// of at least one carries at least as many digits as its scale: the digit term of the reduction
/// therefore always dominates the scale term, and the chain's scale cap is never what decides how
/// much is given up. Simulating the chain over those three bases puts the widest working scale they
/// reach at 153 against a cap of 411.
/// <para>
/// Below one the two swap over. A value whose powers fall past about 1e-101 carries fewer digits
/// than its scale, the scale cap becomes what decides the reduction, and how wide the accumulator
/// stays between multiplications follows from where that cap sits. Since a full-width multiplication
/// at this width is 64 word products against 16 at the mantissa's, the width the cap leaves is the
/// cost. This class is the row that sees it.
/// </para>
/// <para>
/// There is no reciprocal row. The reciprocal of a power this small does not fit, so the operation
/// throws, and a benchmark of an exception is a benchmark of nothing.
/// </para>
/// <para>
/// Deliberately outside the budget category. No acceptance criterion is read from it: it exists so
/// that a change in this cost stays visible, which is the same reason the conversions and the scale
/// changes sit outside that subset.
/// </para>
/// </remarks>
public class PowerUnderOneBenchmarks
{
    private BigDecimal _value;

    /// <summary>
    /// The exponent. Four is below the regime and 600 is inside it, so the pair reads as the same
    /// contrast the shape criterion is stated across: at four the reduction is digit-driven and at
    /// 600 it is scale-driven, and both results are representable.
    /// </summary>
    [Params(4, 600)]
    public int Exponent { get; set; }

    /// <summary>Parses the base, so that only the operation itself is measured.</summary>
    [GlobalSetup]
    public void Setup() => _value = BigDecimal.Parse("0.5", CultureInfo.InvariantCulture);

    /// <summary>The power itself.</summary>
    /// <returns>The result, returned so that the operation is not elided.</returns>
    [Benchmark]
    public BigDecimal Power() => BigDecimal.Pow(_value, Exponent);
}
