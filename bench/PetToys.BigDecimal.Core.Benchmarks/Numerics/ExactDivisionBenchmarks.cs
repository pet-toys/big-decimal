using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The three ways a quotient comes out, over one dividend, side by side.
/// </summary>
/// <remarks>
/// <para>
/// The requirement this answers is not a ratio against <see cref="decimal"/> but a comparison of
/// rows of this table against each other: an exact division must not cost more than an inexact
/// one. The cases are a parameter rather than three methods so that they sit in adjacent rows of
/// the report with everything else held equal.
/// </para>
/// <para>
/// The middle case carries no budget of its own. It is here because it is the deepest the search
/// goes - a divisor the factor test accepts over a dividend the trial division cannot finish - so
/// work taken out of the search shows up in the difference between the three rows rather than
/// having to be inferred from one of them.
/// </para>
/// </remarks>
[BenchmarkCategory(BenchmarkCategories.Budget)]
public class ExactDivisionBenchmarks
{
    private BigDecimal _dividend;
    private BigDecimal _divisor;
    private decimal _referenceDividend;
    private decimal _referenceDivisor;

    /// <summary>How the quotient comes out, which decides how deep the division searches.</summary>
    [Params(DivisionExactness.Exact, DivisionExactness.Factored, DivisionExactness.Inexact)]
    public DivisionExactness Exactness { get; set; }

    /// <summary>Parses the operands, so that only the division itself is measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var divisor = Exactness switch
        {
            DivisionExactness.Exact => Operands.ExactDivisor,
            DivisionExactness.Factored => Operands.FactorDivisor,
            _ => Operands.InexactDivisor,
        };
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
