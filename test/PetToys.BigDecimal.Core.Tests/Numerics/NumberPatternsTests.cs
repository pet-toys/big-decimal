using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Pins the sign, symbol and digit layouts against <see cref="decimal"/>, one index at a time.
/// </summary>
/// <remarks>
/// The layouts are data, and data transcribed from documentation is data nobody checked. Each case
/// formats a value with <see cref="decimal"/> under a culture whose sign, symbol and separators are
/// distinct markers, extracts the digit body from that output, and rebuilds the string from the
/// layout this package holds. A layout that is wrong in one character fails here rather than in a
/// formatting test, where it would read as a formatter defect.
/// </remarks>
public sealed partial class NumberPatternsTests
{
    private const decimal Negative = -1234.5678m;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void NumberNegative_MatchesDecimal(int pattern)
    {
        var info = Markers();
        info.NumberNegativePattern = pattern;

        Rebuild(NumberPatterns.NumberNegative(pattern), Negative.ToString("N1", info), info)
            .Should().Be(Negative.ToString("N1", info));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CurrencyPositive_MatchesDecimal(int pattern)
    {
        var info = Markers();
        info.CurrencyPositivePattern = pattern;

        Rebuild(NumberPatterns.CurrencyPositive(pattern), (-Negative).ToString("C1", info), info)
            .Should().Be((-Negative).ToString("C1", info));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void CurrencyNegative_MatchesDecimal(int pattern)
    {
        var info = Markers();
        info.CurrencyNegativePattern = pattern;

        Rebuild(NumberPatterns.CurrencyNegative(pattern), Negative.ToString("C1", info), info)
            .Should().Be(Negative.ToString("C1", info));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PercentPositive_MatchesDecimal(int pattern)
    {
        var info = Markers();
        info.PercentPositivePattern = pattern;

        Rebuild(NumberPatterns.PercentPositive(pattern), (-Negative).ToString("P1", info), info)
            .Should().Be((-Negative).ToString("P1", info));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void PercentNegative_MatchesDecimal(int pattern)
    {
        var info = Markers();
        info.PercentNegativePattern = pattern;

        Rebuild(NumberPatterns.PercentNegative(pattern), Negative.ToString("P1", info), info)
            .Should().Be(Negative.ToString("P1", info));
    }

    [Fact]
    public void AnUndefinedIndex_FallsBackRatherThanThrowing()
    {
        NumberPatterns.NumberNegative(99).Should().Be(NumberPatterns.NumberNegative(1));
        NumberPatterns.CurrencyNegative(-1).Should().Be(NumberPatterns.CurrencyNegative(0));
        NumberPatterns.CurrencyPositive(99).Should().Be(NumberPatterns.CurrencyPositive(0));
        NumberPatterns.PercentNegative(99).Should().Be(NumberPatterns.PercentNegative(0));
        NumberPatterns.PercentPositive(-1).Should().Be(
            NumberPatterns.PercentPositive(0),
            "a culture is free to carry an index the framework does not define, and formatting it must "
            + "not throw from a table lookup");
    }

    // The digit body is whatever decimal wrote that is not one of the markers: the markers are
    // chosen so that no character of the body can be mistaken for one.
    private static string Rebuild(string layout, string actual, NumberFormatInfo info)
    {
        var body = Body().Match(actual).Value;
        var rebuilt = new System.Text.StringBuilder(actual.Length);

        foreach (var token in layout)
        {
            _ = rebuilt.Append(token switch
            {
                'n' => body,
                '-' => info.NegativeSign,
                '$' => info.CurrencySymbol,
                '%' => info.PercentSymbol,
                _ => token.ToString(),
            });
        }

        return rebuilt.ToString();
    }

    [GeneratedRegex("[0-9_:]+")]
    private static partial Regex Body();

    private static NumberFormatInfo Markers()
    {
        var info = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();

        info.NegativeSign = "!";
        info.CurrencySymbol = "$$";
        info.PercentSymbol = "pc";
        info.NumberGroupSeparator = "_";
        info.NumberDecimalSeparator = ":";
        info.CurrencyGroupSeparator = "_";
        info.CurrencyDecimalSeparator = ":";
        info.PercentGroupSeparator = "_";
        info.PercentDecimalSeparator = ":";

        return info;
    }
}
