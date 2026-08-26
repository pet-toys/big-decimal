using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Parsing from a UTF-8 span, against <see cref="decimal"/>.
/// </summary>
/// <remarks>
/// The input is prepared in <see cref="Setup"/>, which parses it once as each type, through the
/// same overload the benchmark measures. Validating only one of the two would leave the other free
/// to fail part-way through the run, which is the opposite of what a setup is for: a baseline that
/// throws mid-suite costs a re-run, and the operand set is meant to be editable. Only the parse
/// itself is inside the measured method.
/// </remarks>
public class ParseUtf8Benchmarks
{
    private byte[] _utf8 = [];

    /// <summary>The mantissa width the input is drawn at.</summary>
    [Params(OperandShape.OneWord, OperandShape.TwoWords)]
    public OperandShape Shape { get; set; }

    /// <summary>Prepares and validates the input.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var utf8 = Encoding.UTF8.GetBytes(Operands.Value(Shape));
        _ = BigDecimal.Parse(utf8, CultureInfo.InvariantCulture);
        _ = decimal.Parse(utf8, CultureInfo.InvariantCulture);
        _utf8 = utf8;
    }

    /// <summary>The same parse on <see cref="decimal"/>, which the 3x budget is stated against.</summary>
    /// <returns>The parsed value, returned so that the parse is not elided.</returns>
    [Benchmark(Baseline = true)]
    public decimal Baseline() => decimal.Parse(_utf8, CultureInfo.InvariantCulture);

    /// <summary>The parse under budget.</summary>
    /// <returns>The parsed value, returned so that the parse is not elided.</returns>
    [Benchmark]
    public BigDecimal Measured() => BigDecimal.Parse(_utf8, CultureInfo.InvariantCulture);
}
