using System;
using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Narrowing and widening the scale. No budget applies to either, so no baseline is declared.
/// </summary>
/// <remarks>
/// <see cref="Math.Round(decimal, int, MidpointRounding)"/> is measured alongside as a reference
/// point, not as a ratio: the two are comparable for narrowing and have nothing to compare for
/// widening, since <see cref="decimal"/> offers no counterpart to <c>WithScale</c> at all.
/// </remarks>
public class ScaleBenchmarks
{
    private BigDecimal _value;
    private decimal _reference;

    /// <summary>The mantissa width the value is drawn at.</summary>
    [Params(OperandShape.OneWord, OperandShape.TwoWords)]
    public OperandShape Shape { get; set; }

    /// <summary>Parses the value, so that only the scale change is measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var text = Operands.Value(Shape);
        _value = BigDecimal.Parse(text, CultureInfo.InvariantCulture);
        _reference = decimal.Parse(text, CultureInfo.InvariantCulture);
    }

    /// <summary>Rounding to two fractional digits, half to even.</summary>
    /// <returns>The rounded value, returned so that the call is not elided.</returns>
    [Benchmark]
    public BigDecimal RoundToEven() => BigDecimal.Round(_value, 2, MidpointRounding.ToEven);

    /// <summary>Rounding to two fractional digits, half away from zero.</summary>
    /// <returns>The rounded value, returned so that the call is not elided.</returns>
    [Benchmark]
    public BigDecimal RoundAwayFromZero() => BigDecimal.Round(_value, 2, MidpointRounding.AwayFromZero);

    /// <summary>The same rounding on <see cref="decimal"/>, as a reference point.</summary>
    /// <returns>The rounded value, returned so that the call is not elided.</returns>
    [Benchmark]
    public decimal RoundReference() => Math.Round(_reference, 2, MidpointRounding.ToEven);

    /// <summary>Narrowing the scale, which discards digits and rounds.</summary>
    /// <returns>The narrowed value, returned so that the call is not elided.</returns>
    [Benchmark]
    public BigDecimal Narrow() => _value.WithScale(2);

    /// <summary>Widening the scale, which multiplies the mantissa up to the requested scale.</summary>
    /// <returns>The widened value, returned so that the call is not elided.</returns>
    [Benchmark]
    public BigDecimal Widen() => _value.WithScale(20);
}
