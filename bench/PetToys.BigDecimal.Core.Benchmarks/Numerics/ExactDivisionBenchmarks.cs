using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The exact and the inexact quotient of the same dividend, side by side.
/// </summary>
/// <remarks>
/// The requirement this answers is not a ratio against <see cref="decimal"/> but a comparison of
/// two rows of this table against each other: an exact division must not cost more than an inexact
/// one. The two cases are a parameter rather than two methods so that they sit in adjacent rows of
/// the report with everything else held equal.
/// </remarks>
public class ExactDivisionBenchmarks
{
    private BigDecimal _dividend;
    private BigDecimal _divisor;
    private decimal _referenceDividend;
    private decimal _referenceDivisor;

    /// <summary>Whether the quotient comes out exactly.</summary>
    [Params(true, false)]
    public bool Exact { get; set; }

    /// <summary>Parses the operands, so that only the division itself is measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var divisor = Exact ? Operands.ExactDivisor : Operands.InexactDivisor;
        _dividend = BigDecimal.Parse(Operands.ExactnessDividend, CultureInfo.InvariantCulture);
        _divisor = BigDecimal.Parse(divisor, CultureInfo.InvariantCulture);
        _referenceDividend = decimal.Parse(Operands.ExactnessDividend, CultureInfo.InvariantCulture);
        _referenceDivisor = decimal.Parse(divisor, CultureInfo.InvariantCulture);
    }

    /// <summary>The same division on <see cref="decimal"/>.</summary>
    /// <returns>The quotient, returned so that the division is not elided.</returns>
    [Benchmark(Baseline = true)]
    public decimal Baseline() => _referenceDividend / _referenceDivisor;

    /// <summary>The division under measurement.</summary>
    /// <returns>The quotient, returned so that the division is not elided.</returns>
    [Benchmark]
    public BigDecimal Measured() => _dividend / _divisor;
}
