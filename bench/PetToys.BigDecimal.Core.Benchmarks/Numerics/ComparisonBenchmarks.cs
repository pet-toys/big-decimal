using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Ordering and hashing. No budget applies, so no baseline is declared and no ratio is reported.
/// </summary>
/// <remarks>
/// Equality is not measured separately: <c>Equals</c> is <c>CompareTo(other) == 0</c>, so a row for
/// it would report the cost of the comparison a second time. The misaligned pairing is the
/// interesting one here — comparing two values of different scale is what forces the alignment
/// that the aligned pairing skips.
/// </remarks>
public class ComparisonBenchmarks
{
    private BigDecimal _left;
    private BigDecimal _right;
    private BigDecimal _widened;
    private decimal _referenceLeft;
    private decimal _referenceRight;

    /// <summary>The mantissa width the operands are drawn at.</summary>
    [Params(OperandShape.OneWord, OperandShape.TwoWords)]
    public OperandShape Shape { get; set; }

    /// <summary>Whether the two operands carry the same scale.</summary>
    [Params(ScalePairing.Aligned, ScalePairing.Misaligned)]
    public ScalePairing Pairing { get; set; }

    /// <summary>Parses the operands, so that only the comparison is measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var (left, right) = Operands.Additive(Shape, Pairing);
        _left = BigDecimal.Parse(left, CultureInfo.InvariantCulture);
        _right = BigDecimal.Parse(right, CultureInfo.InvariantCulture);
        _referenceLeft = decimal.Parse(left, CultureInfo.InvariantCulture);
        _referenceRight = decimal.Parse(right, CultureInfo.InvariantCulture);

        // The same value carrying eleven trailing zeros. This is not a contrived input: it is what
        // WithScale produces when a caller widens a value to a column's scale, so it is the shape
        // the adapter packages will hash. Hashing has to normalise the scale away, and the operands
        // above end in a 9 -- they cost one pass and stop, which is the floor rather than the cost.
        _widened = _left.WithScale(20);
    }

    /// <summary>Ordering two values.</summary>
    /// <returns>The comparison result, returned so that the call is not elided.</returns>
    [Benchmark]
    public int Compare() => _left.CompareTo(_right);

    /// <summary>The same ordering on <see cref="decimal"/>, as a reference point.</summary>
    /// <returns>The comparison result, returned so that the call is not elided.</returns>
    [Benchmark]
    public int CompareReference() => _referenceLeft.CompareTo(_referenceRight);

    /// <summary>Hashing a value, which has to agree with numeric equality across scales.</summary>
    /// <returns>The hash code, returned so that the call is not elided.</returns>
    [Benchmark]
    public int Hash() => _left.GetHashCode();

    /// <summary>The same hashing on <see cref="decimal"/>, as a reference point.</summary>
    /// <returns>The hash code, returned so that the call is not elided.</returns>
    [Benchmark]
    public int HashReference() => _referenceLeft.GetHashCode();

    /// <summary>Hashing a value that carries trailing zeros, which the hash has to strip.</summary>
    /// <returns>The hash code, returned so that the call is not elided.</returns>
    [Benchmark]
    public int HashWidened() => _widened.GetHashCode();
}
