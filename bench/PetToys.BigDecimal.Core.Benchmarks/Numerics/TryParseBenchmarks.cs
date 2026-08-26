using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Non-throwing parsing from a <c>char</c> span, against <see cref="decimal"/>.
/// </summary>
/// <remarks>
/// The input is prepared in <see cref="Setup"/>, which also parses it once with the throwing
/// overload, so a mistyped operand fails at setup rather than turning into a silently unparsed
/// benchmark. Only the parse itself is inside the measured method.
/// </remarks>
public class TryParseBenchmarks
{
    private string _text = string.Empty;

    /// <summary>The mantissa width the input is drawn at.</summary>
    [Params(OperandShape.OneWord, OperandShape.TwoWords)]
    public OperandShape Shape { get; set; }

    /// <summary>Prepares and validates the input.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var text = Operands.Value(Shape);
        _ = BigDecimal.Parse(text, CultureInfo.InvariantCulture);
        _text = text;
    }

    /// <summary>The same parse on <see cref="decimal"/>, which the 3x budget is stated against.</summary>
    /// <returns>The parsed value, returned so that the parse is not elided.</returns>
    [Benchmark(Baseline = true)]
    public decimal Baseline()
    {
        _ = decimal.TryParse(_text, CultureInfo.InvariantCulture, out var result);
        return result;
    }

    /// <summary>The parse under budget.</summary>
    /// <returns>The parsed value, returned so that the parse is not elided.</returns>
    [Benchmark]
    public BigDecimal Measured()
    {
        _ = BigDecimal.TryParse(_text, CultureInfo.InvariantCulture, out var result);
        return result;
    }
}
