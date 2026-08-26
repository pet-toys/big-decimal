using System.Collections.Generic;
using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Formatting into a UTF-8 span, against <see cref="decimal"/>.
/// </summary>
/// <remarks>
/// The destination buffer and the format string are prepared in <see cref="Setup"/>, for the same
/// reason as in the <c>char</c> case: the measurement is of the formatter, not of the allocator.
/// </remarks>
public class FormatUtf8Benchmarks
{
    private BigDecimal _value;
    private decimal _reference;
    private byte[] _destination = [];

    /// <summary>The format strings this benchmark is measured over.</summary>
    public static IEnumerable<string> FormatStrings => Operands.Formats;

    /// <summary>The mantissa width the value is drawn at.</summary>
    [Params(OperandShape.OneWord, OperandShape.TwoWords)]
    public OperandShape Shape { get; set; }

    /// <summary>The format string to render with.</summary>
    [ParamsSource(nameof(FormatStrings))]
    public string Format { get; set; } = "G";

    /// <summary>Parses the value and allocates the destination, so that only the format is measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var text = Operands.Value(Shape);
        _value = BigDecimal.Parse(text, CultureInfo.InvariantCulture);
        _reference = decimal.Parse(text, CultureInfo.InvariantCulture);
        _destination = new byte[256];
    }

    /// <summary>The same format on <see cref="decimal"/>, which the 3x budget is stated against.</summary>
    /// <returns>The number of bytes written, returned so that the call is not elided.</returns>
    [Benchmark(Baseline = true)]
    public int Baseline()
    {
        _ = _reference.TryFormat(_destination, out var written, Format, CultureInfo.InvariantCulture);
        return written;
    }

    /// <summary>The format under budget.</summary>
    /// <returns>The number of bytes written, returned so that the call is not elided.</returns>
    [Benchmark]
    public int Measured()
    {
        _ = _value.TryFormat(_destination, out var written, Format, CultureInfo.InvariantCulture);
        return written;
    }
}
