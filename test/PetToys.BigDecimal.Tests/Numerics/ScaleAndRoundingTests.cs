using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

public sealed class ScaleAndRoundingTests
{
    private static BigDecimal Parse(string text) => BigDecimal.Parse(text, CultureInfo.InvariantCulture);

    private static string Text(BigDecimal value) => value.ToString(CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void Round_RejectsAScaleOutsideTheDomain(int scale)
    {
        var act = () => BigDecimal.Round(Parse("1.5"), scale);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(scale));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void WithScale_RejectsAScaleOutsideTheDomain(int scale)
    {
        var act = () => Parse("1.5").WithScale(scale);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(scale));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void FromScaled_RejectsAScaleOutsideTheDomain(int scale)
    {
        var act = () => BigDecimal.FromScaled(BigInteger.One, scale);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(scale));
    }

    [Fact]
    public void MaxScale_IsTwoHundredAndFiftyFive()
    {
        BigDecimal.MaxScale.Should().Be(255);
        Parse("0." + new string('0', 254) + "1").Scale.Should().Be(255);
    }

    [Fact]
    public void ALongFraction_IsRoundedRatherThanRefused()
    {
        var value = Parse(new string('9', 40) + "." + new string('9', 40));

        value.Scale.Should().Be(37);
        Text(value).Should().Be("1" + new string('0', 40) + "." + new string('0', 37));
    }

    [Fact]
    public void ALongFractionAlone_LosesScaleNotAcceptance()
    {
        var value = Parse("0." + new string('1', 300));

        value.Scale.Should().Be(77);
        value.Scale.Should().BeLessThan(BigDecimal.MaxScale);
        value.IsZero.Should().BeFalse();
    }

    [Fact]
    public void AnOversizedIntegerPart_IsRefused()
    {
        var act = () => Parse(new string('9', 79));

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void RoundingCarry_PropagatesIntoTheIntegerPart()
    {
        Text(BigDecimal.Round(Parse("9.99"), 1)).Should().Be("10.0");
        Text(BigDecimal.Round(Parse("-9.99"), 1)).Should().Be("-10.0");
    }

    [Theory]
    [InlineData("0.5", "0")]
    [InlineData("1.5", "2")]
    [InlineData("2.5", "2")]
    [InlineData("3.5", "4")]
    [InlineData("-2.5", "-2")]
    [InlineData("2.51", "3")]
    public void ExcessFractionalDigits_RoundHalfToEven(string source, string expected)
    {
        Text(BigDecimal.Round(Parse(source), 0)).Should().Be(expected);
    }

    [Theory]
    [InlineData("2.345", 2)]
    [InlineData("-2.345", 2)]
    [InlineData("1.005", 2)]
    [InlineData("0.125", 2)]
    [InlineData("79228162514264337593543950335", 0)]
    public void Rounding_AgreesWithDecimalInDecimalsDomain(string source, int scale)
    {
        var expected = decimal.Round(decimal.Parse(source, CultureInfo.InvariantCulture), scale, MidpointRounding.ToEven);

        Text(BigDecimal.Round(Parse(source), scale)).Should().Be(expected.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ArithmeticCrossingTheCeiling_Throws()
    {
        var act = () => BigDecimal.MaxValue + BigDecimal.One;

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void ConversionFromAWiderInteger_Throws()
    {
        var act = () => (BigDecimal)BigInteger.Pow(2, 256);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void MultiplyingPastTheScaleCeiling_RoundsToZero()
    {
        var small = Parse("0." + new string('0', 254) + "1");

        var product = small * small;

        product.IsZero.Should().BeTrue();
        product.Scale.Should().Be(BigDecimal.MaxScale);
    }

    [Fact]
    public void ParsingBelowTheFloor_RoundsToZero()
    {
        var value = Parse("0." + new string('0', 255) + "1");

        value.IsZero.Should().BeTrue();
        value.Scale.Should().Be(BigDecimal.MaxScale);
    }

    [Fact]
    public void WithScale_WideningPadsWithZeros()
    {
        var value = Parse("1.5").WithScale(18);

        Text(value).Should().Be("1.500000000000000000");
        value.Scale.Should().Be(18);
        value.Should().Be(Parse("1.5"));
    }

    [Fact]
    public void WithScale_WideningKeepsTheSign()
    {
        Text(Parse("-1.5").WithScale(3)).Should().Be("-1.500");
    }

    [Theory]
    [InlineData(MidpointRounding.ToEven, "1.6")]
    [InlineData(MidpointRounding.AwayFromZero, "1.6")]
    [InlineData(MidpointRounding.ToZero, "1.5")]
    [InlineData(MidpointRounding.ToNegativeInfinity, "1.5")]
    [InlineData(MidpointRounding.ToPositiveInfinity, "1.6")]
    public void WithScale_NarrowingHonoursTheMode(MidpointRounding mode, string expected)
    {
        Text(Parse("1.55").WithScale(1, mode)).Should().Be(expected);
    }

    [Fact]
    public void WithScale_NarrowingDefaultsToHalfToEven()
    {
        Text(Parse("1.25").WithScale(1)).Should().Be("1.2");
        Text(Parse("1.35").WithScale(1)).Should().Be("1.4");
    }

    [Fact]
    public void WithScale_WideningPastTheMagnitude_Throws()
    {
        var act = () => BigDecimal.One.WithScale(78);

        act.Should().Throw<OverflowException>();
        BigDecimal.One.WithScale(77).Scale.Should().Be(77);
    }

    [Fact]
    public void WithScale_ToTheCurrentScale_ReturnsTheSameValue()
    {
        var value = Parse("1.500");

        var same = value.WithScale(3);

        same.Should().Be(value);
        same.Scale.Should().Be(value.Scale);
        Text(same).Should().Be(Text(value));
    }

    [Fact]
    public void WithScale_OnZero_KeepsZeroAndTakesTheScale()
    {
        var value = BigDecimal.Zero.WithScale(200);

        value.IsZero.Should().BeTrue();
        value.IsNegative.Should().BeFalse();
        value.Scale.Should().Be(200);
    }

    [Fact]
    public void Round_ToAWiderScale_ChangesNothing()
    {
        var value = Parse("1.5");

        var rounded = BigDecimal.Round(value, 5);

        Text(rounded).Should().Be("1.5");
        rounded.Scale.Should().Be(1);
    }

    [Fact]
    public void WithScale_DoesNotAllocate()
    {
        var value = Parse("1.5");
        var wide = Parse("1.234567");

        Measure(() => value.WithScale(18)).Should().Be(0);
        Measure(() => wide.WithScale(2)).Should().Be(0);
        Measure(() => value.WithScale(1)).Should().Be(0);
    }

    private static long Measure(Func<BigDecimal> operation)
    {
        for (var i = 0; i < 16; i++)
        {
            Consume(operation());
        }

        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 256; i++)
        {
            Consume(operation());
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Consume(BigDecimal value) => _ = value.Scale;
}
