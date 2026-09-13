using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Parsing literals whose exponent puts them below the scale floor, against one that stays inside
/// it.
/// </summary>
/// <remarks>
/// <para>
/// The four rows differ in one thing: how many decimal positions the pack has to drop. The first
/// drops none, and the other three drop 45, 2745 and 99744 - the last being what the parser's
/// exponent ceiling allows. All three exhaust the magnitude in their first division, so the ratios
/// between them are flat when the drop stops there and follow the exponent when it does not.
/// </para>
/// <para>
/// The class carries no budget category. <see cref="decimal"/> parses none of these literals to the
/// same value, so there is no baseline against it to state; the criterion is a ratio between rows
/// of this class, read within one run.
/// </para>
/// <para>
/// Three dropping rows rather than two, because two are an interpolation: a flat pair at either end
/// is also what a linear cost with a large constant term looks like at that resolution.
/// </para>
/// </remarks>
public class ParseExponentBenchmarks
{
    private string _withinTheScale = string.Empty;

    private string _justBelowTheFloor = string.Empty;

    private string _farBelowTheFloor = string.Empty;

    private string _atTheExponentCeiling = string.Empty;

    /// <summary>Prepares and validates the operands, so that a bad literal fails here rather than mid-suite.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _withinTheScale = Validate("1e-200");
        _justBelowTheFloor = Validate("1e-300");
        _farBelowTheFloor = Validate("1e-3000");
        _atTheExponentCeiling = Validate("1e-99999");

        static string Validate(string text)
        {
            _ = BigDecimal.Parse(text, CultureInfo.InvariantCulture);
            return text;
        }
    }

    /// <summary>The reference: a literal whose scale is inside the ceiling, so nothing is dropped.</summary>
    /// <returns>The parsed value, returned so that the parse is not elided.</returns>
    [Benchmark(Baseline = true)]
    public BigDecimal NothingDropped() => BigDecimal.Parse(_withinTheScale, CultureInfo.InvariantCulture);

    /// <summary>Forty-five positions dropped.</summary>
    /// <returns>The parsed value, returned so that the parse is not elided.</returns>
    [Benchmark]
    public BigDecimal JustBelow() => BigDecimal.Parse(_justBelowTheFloor, CultureInfo.InvariantCulture);

    /// <summary>Two thousand seven hundred and forty-five positions dropped.</summary>
    /// <returns>The parsed value, returned so that the parse is not elided.</returns>
    [Benchmark]
    public BigDecimal FarBelow() => BigDecimal.Parse(_farBelowTheFloor, CultureInfo.InvariantCulture);

    /// <summary>The most the parser's exponent ceiling allows: 99744 positions.</summary>
    /// <returns>The parsed value, returned so that the parse is not elided.</returns>
    [Benchmark]
    public BigDecimal AtTheCeiling() => BigDecimal.Parse(_atTheExponentCeiling, CultureInfo.InvariantCulture);
}
