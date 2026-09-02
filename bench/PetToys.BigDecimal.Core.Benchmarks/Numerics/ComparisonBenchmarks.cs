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
    private BigDecimal _widenedOne;
    private BigDecimal _widened;
    private BigDecimal _widenedNineteen;
    private BigDecimal _widenedBeyond;
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

        // The same value carrying trailing zeros. This is not a contrived input: it is what
        // WithScale produces when a caller widens a value to a column's scale, so it is the shape
        // the adapter packages will hash. Hashing has to normalise the scale away, and the operands
        // above end in a 9 -- they cost no division at all, which is the floor rather than the cost.
        //
        // Four widths, because the requirement is on the shape of the curve rather than on one
        // ratio: one, eleven and nineteen zeros all come off in a single division and have to cost
        // the same, and twenty-five needs a second one and may cost a step more. The operands carry
        // scale 9, so the widened scales are 9 more than the zeros they add.
        _widenedOne = _left.WithScale(10);
        _widened = _left.WithScale(20);
        _widenedNineteen = _left.WithScale(28);
        _widenedBeyond = _left.WithScale(34);
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

    /// <summary>Hashing a value carrying one trailing zero.</summary>
    /// <returns>The hash code, returned so that the call is not elided.</returns>
    [Benchmark]
    public int HashWidenedOne() => _widenedOne.GetHashCode();

    /// <summary>Hashing a value carrying eleven trailing zeros, the column-scale shape.</summary>
    /// <returns>The hash code, returned so that the call is not elided.</returns>
    [Benchmark]
    public int HashWidened() => _widened.GetHashCode();

    /// <summary>Hashing a value carrying nineteen trailing zeros, the last that come off in one division.</summary>
    /// <returns>The hash code, returned so that the call is not elided.</returns>
    [Benchmark]
    public int HashWidenedNineteen() => _widenedNineteen.GetHashCode();

    /// <summary>Hashing a value carrying twenty-five trailing zeros, which needs a second division.</summary>
    /// <returns>The hash code, returned so that the call is not elided.</returns>
    [Benchmark]
    public int HashWidenedBeyond() => _widenedBeyond.GetHashCode();
}
