using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Conversions to and from the primitives, and the word-level round trip the integration packages
/// are built on. No budget applies, so no baseline is declared.
/// </summary>
/// <remarks>
/// <c>FromWords</c> and <c>GetWords</c> have no counterpart on any other type, which is exactly why
/// they are measured: the adapter packages read and write a database wire format through them, so
/// their cost is the floor of everything <c>db-interop</c> will add.
/// </remarks>
public class ConversionBenchmarks
{
    private readonly ulong[] _words = new ulong[4];

    private BigDecimal _value;
    private decimal _reference;

    // An instance field rather than a constant: a benchmark method that touches no instance state
    // is flagged as convertible to static, and BenchmarkDotNet wants instance methods.
    private long _integerSource;

    /// <summary>The mantissa width the value is drawn at.</summary>
    [Params(OperandShape.OneWord, OperandShape.TwoWords)]
    public OperandShape Shape { get; set; }

    /// <summary>Parses the value, so that only the conversion is measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var text = Operands.Value(Shape);
        _value = BigDecimal.Parse(text, CultureInfo.InvariantCulture);
        _reference = decimal.Parse(text, CultureInfo.InvariantCulture);
        _integerSource = 1_234_567_890_123_456_789L;
        _ = _value.GetWords(_words, out _, out _);
    }

    /// <summary>Converting to <see cref="decimal"/>.</summary>
    /// <returns>The converted value, returned so that the call is not elided.</returns>
    [Benchmark]
    public decimal ToReference() => (decimal)_value;

    /// <summary>Converting from <see cref="decimal"/>.</summary>
    /// <returns>The converted value, returned so that the call is not elided.</returns>
    [Benchmark]
    public BigDecimal FromReference() => _reference;

    /// <summary>Converting to a binary floating-point value, which is lossy by nature.</summary>
    /// <returns>The converted value, returned so that the call is not elided.</returns>
    [Benchmark]
    public double ToBinaryFloat() => (double)_value;

    /// <summary>Converting from a 64-bit integer, the widening every caller does implicitly.</summary>
    /// <returns>The converted value, returned so that the call is not elided.</returns>
    [Benchmark]
    public BigDecimal FromInteger() => _integerSource;

    /// <summary>Reading the mantissa out word by word.</summary>
    /// <returns>The number of words written, returned so that the call is not elided.</returns>
    [Benchmark]
    public int ToWords() => _value.GetWords(_words, out _, out _);

    /// <summary>Rebuilding a value from its words.</summary>
    /// <returns>The rebuilt value, returned so that the call is not elided.</returns>
    [Benchmark]
    public BigDecimal FromWords() => BigDecimal.FromWords(_words, false, 9);
}
