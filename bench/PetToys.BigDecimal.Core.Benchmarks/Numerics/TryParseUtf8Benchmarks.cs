using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Non-throwing parsing from a UTF-8 span, against <see cref="decimal"/>.
/// </summary>
/// <remarks>
/// The input is prepared in <see cref="Setup"/>, which parses it once as each type, through the
/// throwing overload rather than the non-throwing one measured here. That asymmetry is deliberate:
/// a failed <c>TryParse</c> returns <see langword="false"/> and leaves zero behind, so an operand
/// neither type could parse would not stop the run — it would quietly have the baseline measure a
/// rejection instead of a parse, which is faster, and flatter the ratio. Only the parse itself is
/// inside the measured method.
/// </remarks>
public class TryParseUtf8Benchmarks
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
    public decimal Baseline()
    {
        _ = decimal.TryParse(_utf8, CultureInfo.InvariantCulture, out var result);
        return result;
    }

    /// <summary>The parse under budget.</summary>
    /// <returns>The parsed value, returned so that the parse is not elided.</returns>
    [Benchmark]
    public BigDecimal Measured()
    {
        _ = BigDecimal.TryParse(_utf8, CultureInfo.InvariantCulture, out var result);
        return result;
    }
}
