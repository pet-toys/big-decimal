using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The power beside the multiplication loop a caller writes when the type has no power, at the one
/// mantissa width <see cref="decimal"/> can also hold.
/// </summary>
/// <remarks>
/// This class carries no budget and no baseline, and a ratio between its two rows is not a number
/// anyone may quote. The two do not compute the same thing: the loop rounds at every step and to 96
/// bits, so once the exact power stops fitting it is answering a different question, and it is the
/// slower of the two by a factor that grows with the exponent for a reason the report cannot show -
/// it performs the exponent's own number of multiplications where the other performs its bit
/// length's. It is here so that the question the change was raised to answer, whether the operation
/// is worth having over the loop it replaces, has a measurement rather than an argument.
/// </remarks>
public class PowerLoopBenchmarks
{
    private BigDecimal _value;
    private decimal _reference;

    /// <summary>
    /// The exponent. Four and a thousand are the two ends the power itself is measured across;
    /// twenty and forty are here because the crossover between the two methods lies between them,
    /// and a crossover quoted from two distant points is an interpolation rather than a reading.
    /// </summary>
    [Params(4, 20, 40, 1000)]
    public int Exponent { get; set; }

    /// <summary>Parses the base, so that only the operation itself is measured.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var text = Operands.Power(OperandShape.OneWord);
        _value = BigDecimal.Parse(text, CultureInfo.InvariantCulture);
        _reference = decimal.Parse(text, CultureInfo.InvariantCulture);
    }

    /// <summary>The operation.</summary>
    /// <returns>The result, returned so that the operation is not elided.</returns>
    [Benchmark]
    public BigDecimal Power() => BigDecimal.Pow(_value, Exponent);

    /// <summary>What a caller writes without it: one multiplication per unit of exponent.</summary>
    /// <returns>The result, returned so that the operation is not elided.</returns>
    [Benchmark]
    public decimal ReferenceLoop()
    {
        var result = decimal.One;
        for (var index = 0; index < Exponent; index++)
        {
            result *= _reference;
        }

        return result;
    }
}
