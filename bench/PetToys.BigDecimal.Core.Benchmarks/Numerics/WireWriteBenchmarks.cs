using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Encoding to both database wire formats, against formatting the same value as text.
/// </summary>
/// <remarks>
/// <para>
/// The baseline is this package's own <c>TryFormat</c> rather than <see cref="decimal"/>. Neither
/// database format has a <see cref="decimal"/> counterpart to divide by, and the claim the budget
/// defends is structural: a binary codec converts digit groups and stops, while the text path
/// converts digit groups and then handles a culture, a format specifier and one character per
/// digit. A binary path costing half again as much as the text path is doing work that belongs to
/// neither.
/// </para>
/// <para>
/// All three rows sit in one class so that they interleave. A ratio is only worth reading if both
/// arms met the same disturbance, and two classes measured minutes apart did not.
/// </para>
/// <para>
/// Every destination is allocated in <see cref="Setup"/>. Renting one inside the measured method
/// would put an allocation the operation does not make into the allocation column.
/// </para>
/// </remarks>
[BenchmarkCategory(BenchmarkCategories.Budget)]
public class WireWriteBenchmarks
{
    private BigDecimal _value;
    private char[] _text = [];
    private byte[] _postgres = [];
    private byte[] _clickHouse = [];

    /// <summary>The mantissa width the value is drawn at.</summary>
    [Params(
        OperandShape.OneWord,
        OperandShape.TwoWords,
        OperandShape.ThreeWords,
        OperandShape.FourWords)]
    public OperandShape Shape { get; set; }

    /// <summary>Parses the value and allocates every destination, so only the write is measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _value = BigDecimal.Parse(Operands.Value(Shape), CultureInfo.InvariantCulture);
        _text = new char[256];
        _postgres = new byte[PostgresNumeric.MaxByteCount];
        _clickHouse = new byte[ClickHouseDecimal.Decimal256Size];
    }

    /// <summary>Formatting the same value, which the 1.5x budget is stated against.</summary>
    /// <returns>The number of characters written, returned so that the call is not elided.</returns>
    [Benchmark(Baseline = true)]
    public int Format()
    {
        _ = _value.TryFormat(_text, out var written, "G", CultureInfo.InvariantCulture);

        return written;
    }

    /// <summary>Encoding to the PostgreSQL binary <c>numeric</c> layout.</summary>
    /// <returns>The number of bytes written, returned so that the call is not elided.</returns>
    [Benchmark]
    public int Postgres()
    {
        _ = PostgresNumeric.TryWrite(_value, _postgres, out var written);

        return written;
    }

    /// <summary>
    /// Encoding to a ClickHouse <c>Decimal256</c> column declared at the value's own scale.
    /// </summary>
    /// <remarks>
    /// The column's scale is the value's, so nothing is rescaled and the row measures the codec
    /// rather than a multiplication the caller chose. The widest width is used at every shape
    /// because the four-word operand needs it, and one width across the four rows keeps the shape
    /// the only thing that varies.
    /// </remarks>
    /// <returns>A byte of the payload, returned so that the call is not elided.</returns>
    [Benchmark]
    public byte ClickHouse()
    {
        ClickHouseDecimal.Write(_value, _value.Scale, _clickHouse);

        return _clickHouse[0];
    }
}
