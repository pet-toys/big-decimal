using System;
using System.Globalization;
using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

public sealed class DatabaseRangeMappingTests
{
    private static BigDecimal Parse(string text) => BigDecimal.Parse(text, CultureInfo.InvariantCulture);

    private static string Text(BigDecimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Extreme(int precision, int scale)
    {
        var integerDigits = precision - scale;
        var integerPart = integerDigits == 0 ? "0" : new string('9', integerDigits);
        return scale == 0 ? integerPart : integerPart + "." + new string('9', scale);
    }

    // ClickHouse Decimal32/64/128/256 carry precisions 9, 18, 38 and 76, each with a scale
    // from 0 to the precision.
    [Theory]
    [InlineData(9, 0)]
    [InlineData(9, 4)]
    [InlineData(9, 9)]
    [InlineData(18, 0)]
    [InlineData(18, 9)]
    [InlineData(18, 18)]
    [InlineData(38, 0)]
    [InlineData(38, 18)]
    [InlineData(38, 38)]
    [InlineData(76, 0)]
    [InlineData(76, 38)]
    [InlineData(76, 76)]
    public void ClickHouseDecimals_RoundTripAtTheirExtremes(int precision, int scale)
    {
        var text = Extreme(precision, scale);

        var value = Parse(text);
        var negative = Parse("-" + text);

        Text(value).Should().Be(text);
        value.Scale.Should().Be(scale);
        Text(negative).Should().Be("-" + text);
        negative.Scale.Should().Be(scale);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(18)]
    [InlineData(38)]
    [InlineData(76)]
    public void NoClickHouseDecimal_OverflowsTheType(int precision)
    {
        foreach (var scale in new[] { 0, precision / 2, precision })
        {
            var act = () => Parse(Extreme(precision, scale));

            act.Should().NotThrow();
        }
    }

    [Fact]
    public void PostgresNumeric_IsCoveredUpToSeventySevenDigits()
    {
        var text = Extreme(77, 38);

        var value = Parse(text);

        Text(value).Should().Be(text);
        value.Scale.Should().Be(38);
    }

    [Fact]
    public void PostgresNumeric_CoversTheCommonMoneyPrecision()
    {
        var text = Extreme(38, 18);

        Text(Parse(text)).Should().Be(text);
        Text(Parse("1.5").WithScale(18)).Should().Be("1.500000000000000000");
        Text(Parse("-0.000000000000000001")).Should().Be("-0.000000000000000001");
    }

    [Fact]
    public void PostgresNumeric_TooLargeInTheIntegerPart_Throws()
    {
        var act = () => Parse(new string('9', 200));

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void PostgresNumeric_WithTheLongestFraction_IsRoundedNotRefused()
    {
        var value = Parse("0." + new string('1', 16383));

        value.IsZero.Should().BeFalse();
        value.Scale.Should().BeLessThan(BigDecimal.MaxScale);
        Text(value).Should().StartWith("0.111111");
    }

    [Fact]
    public void TheTypesOwnBounds_AreTheOnesTheDocumentationQuotes()
    {
        BigDecimal.MaxScale.Should().Be(255);
        Text(BigDecimal.MaxValue).Should().HaveLength(78);
        Text(Parse(new string('9', 77))).Should().HaveLength(77);
        Text(Parse("0." + new string('0', 254) + "1")).Should().Be("0." + new string('0', 254) + "1");
    }
}
