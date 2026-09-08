using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Decoding from both database wire formats, against parsing the same value from its text.
/// </summary>
/// <remarks>
/// <para>
/// The other half of the pair in <see cref="WireWriteBenchmarks"/>, budgeted the same way and for
/// the same reason. The payloads are the ones this package writes for the value being parsed, so
/// all three rows carry the same number and differ only in what it arrived as.
/// </para>
/// <para>
/// The PostgreSQL payload is trimmed to the bytes actually written. Handing the reader the whole
/// <c>MaxByteCount</c> buffer would make it refuse the length rather than read it, and a benchmark
/// measuring a refusal is measuring nothing.
/// </para>
/// </remarks>
[BenchmarkCategory(BenchmarkCategories.Budget)]
public class WireReadBenchmarks
{
    private string _text = string.Empty;
    private byte[] _postgres = [];
    private byte[] _clickHouse = [];
    private int _scale;

    /// <summary>The mantissa width the value is drawn at.</summary>
    [Params(
        OperandShape.OneWord,
        OperandShape.TwoWords,
        OperandShape.ThreeWords,
        OperandShape.FourWords)]
    public OperandShape Shape { get; set; }

    /// <summary>Composes the payloads the three rows read, so only the read is measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _text = Operands.Value(Shape);

        var value = BigDecimal.Parse(_text, CultureInfo.InvariantCulture);
        _scale = value.Scale;

        var postgres = new byte[PostgresNumeric.MaxByteCount];
        _ = PostgresNumeric.TryWrite(value, postgres, out var written);
        _postgres = postgres[..written];

        _clickHouse = new byte[ClickHouseDecimal.Decimal256Size];
        ClickHouseDecimal.Write(value, _scale, _clickHouse);
    }

    /// <summary>Parsing the same value's text, which the 1.5x budget is stated against.</summary>
    /// <returns>The parsed value, returned so that the parse is not elided.</returns>
    [Benchmark(Baseline = true)]
    public BigDecimal Parse() => BigDecimal.Parse(_text, CultureInfo.InvariantCulture);

    /// <summary>Decoding the PostgreSQL binary <c>numeric</c> payload of the same value.</summary>
    /// <returns>The decoded value, returned so that the read is not elided.</returns>
    [Benchmark]
    public BigDecimal Postgres() => PostgresNumeric.Read(_postgres);

    /// <summary>Decoding the ClickHouse <c>Decimal256</c> payload of the same value.</summary>
    /// <returns>The decoded value, returned so that the read is not elided.</returns>
    [Benchmark]
    public BigDecimal ClickHouse() => ClickHouseDecimal.Read(_clickHouse, _scale);
}
