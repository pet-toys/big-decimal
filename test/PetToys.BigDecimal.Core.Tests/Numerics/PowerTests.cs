using System;
using System.Globalization;
using System.Numerics;
using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The stated cases of <see cref="BigDecimal.Pow"/>: the scale rule, the trivial exponents, the
/// boundaries the exactness guarantee turns on, and the reciprocal. The property over the whole
/// corpus lives in <see cref="ArithmeticFuzzTests"/>.
/// </summary>
public sealed class PowerTests
{
    private static BigDecimal Parse(string text) => BigDecimal.Parse(text, CultureInfo.InvariantCulture);

    private static string Text(BigDecimal value) => value.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public void APower_MultipliesTheScaleByTheExponent()
    {
        var result = BigDecimal.Pow(Parse("1.50"), 3);

        Text(result).Should().Be("3.375000");
        result.Scale.Should().Be(6);
    }

    [Theory]
    [InlineData("2", 10, "1024")]
    [InlineData("-2", 3, "-8")]
    [InlineData("-2", 2, "4")]
    [InlineData("-1.25", 7, "-4.76837158203125")]
    [InlineData("0.1", 3, "0.001")]
    public void APower_IsTheExactValue(string text, int exponent, string expected) =>
        Text(BigDecimal.Pow(Parse(text), exponent)).Should().Be(expected);

    [Fact]
    public void AnExponentOfOne_ReturnsTheValueWithItsTrailingZerosIntact()
    {
        var result = BigDecimal.Pow(Parse("1.500"), 1);

        Text(result).Should().Be("1.500");
        result.Scale.Should().Be(3);
    }

    [Fact]
    public void AnExponentOfZero_GivesOneAtScaleZero()
    {
        var result = BigDecimal.Pow(Parse("1.2345"), 0);

        Text(result).Should().Be("1");
        result.Scale.Should().Be(0);
    }

    [Fact]
    public void AZeroBase_KeepsTheScaleTheExponentMultipliesOut()
    {
        var result = BigDecimal.Pow(Parse("0.00"), 5);

        result.IsZero.Should().BeTrue();
        result.Scale.Should().Be(10);
    }

    [Fact]
    public void APowerThatFits_IsExactToTheLastDigit()
    {
        // 1.05^30 is 61 significant digits at scale 60: inside the mantissa and inside MaxScale,
        // so the guarantee applies and nothing on the way to it was reduced.
        var result = BigDecimal.Pow(Parse("1.05"), 30);
        var exact = BigInteger.Pow(105, 30).ToString(CultureInfo.InvariantCulture);

        result.Scale.Should().Be(60);
        Text(result).Should().Be(exact.Insert(exact.Length - 60, "."));
    }

    [Fact]
    public void APowerWhoseScaleWouldPassTheMaximum_IsCappedRatherThanRefused()
    {
        // 0.5^700 wants scale 700 and is about 1e-211, so at 255 its digits still fit and the cap
        // is the only thing that binds.
        var result = BigDecimal.Pow(Parse("0.5"), 700);

        result.Scale.Should().Be(BigDecimal.MaxScale);
        result.IsZero.Should().BeFalse();
        result.Precision.Should().Be(45);
    }

    [Fact]
    public void APowerWhoseDigitsWouldPassTheMantissa_IsCutFurtherThanTheScaleCap()
    {
        // 1.05^200 wants scale 400 as well, but it is about 17293, so 255 fractional digits would
        // take 260 significant ones. The mantissa binds before the scale cap does and the result
        // comes back narrower than MaxScale, carrying the 77 digits the mantissa always holds.
        var result = BigDecimal.Pow(Parse("1.05"), 200);

        result.Scale.Should().Be(72);
        result.Precision.Should().Be(77);
    }

    [Fact]
    public void APowerReducedByItsScaleAlone_LandsInTheDigitBand()
    {
        // The exact square is 10^110 at scale 288. The scale cap asks for 33 digits, which would
        // leave 78 of them and still fit four words, so nothing downstream would narrow it: the
        // shape is a result that was reduced and came back one digit wider than the band, which is
        // the width the rule reserves for a value that was never reduced at all.
        var value = BigDecimal.FromScaled(BigInteger.Pow(10, 55), 144);

        var result = BigDecimal.Pow(value, 2);

        result.GetMantissa().Should().Be(BigInteger.Pow(10, 76));
        result.Scale.Should().Be(254);
        result.Precision.Should().Be(77);
    }

    [Fact]
    public void APowerReducedByItsScaleAlone_LandsInTheDigitBandAtAWiderExponent()
    {
        // The same shape reached the other way round, from a 20-digit magnitude and an exponent of
        // ten rather than from a wide magnitude squared, so that the case does not stand on one
        // arrangement of the chain.
        var value = BigDecimal.FromScaled(BigInteger.Parse("-15998970921129598847", CultureInfo.InvariantCulture), 37);

        var result = BigDecimal.Pow(value, 10);

        result.GetMantissa()
            .Should()
            .Be(BigInteger.Parse(
                "10988046548032671402029280283244067487492344156149405474243755307901936162049",
                CultureInfo.InvariantCulture));
        result.Scale.Should().Be(254);
        result.Precision.Should().Be(77);
    }

    [Fact]
    public void APowerThatGivesUpDigits_RoundsOnceFromTheExactPower()
    {
        // The exact fifth power has 93 digits at scale 270 and the 16 digits it gives up are below
        // half, so one rounding ends in 411. Reducing to MaxScale first and rounding what that
        // produced ends in 412: the first rounding creates a tie the second one carries.
        var value = BigDecimal.FromScaled(BigInteger.Parse("-2799474262758357318", CultureInfo.InvariantCulture), 54);

        var result = BigDecimal.Pow(value, 5);

        result.GetMantissa()
            .Should()
            .Be(BigInteger.Parse(
                "-17194216688885948636321720370297956692529429862388386009258476655764322099411",
                CultureInfo.InvariantCulture));
        result.Scale.Should().Be(254);
    }

    [Fact]
    public void APowerThatGivesUpDigits_RoundsOnceWhereTheSecondRoundingWouldRoundDown()
    {
        // The mirror of the case above: the double rounding moves the last digit down rather than
        // up, so a fix that only ever rounded away from zero would still be wrong here.
        var value = BigDecimal.FromScaled(
            BigInteger.Parse("70120460866688589708676248986381474105", CultureInfo.InvariantCulture),
            97);

        var result = BigDecimal.Pow(value, 3);

        result.GetMantissa()
            .Should()
            .Be(BigInteger.Parse(
                "34477382376059297776766444086711052775807531924617158296218177367506101631769",
                CultureInfo.InvariantCulture));
        result.Scale.Should().Be(254);
    }

    [Fact]
    public void APowerThatRunsOutOfFractionalDigits_KeepsWhatFits()
    {
        // The exact square has 154 digits at scale 76, so the band asks for 77 digits and only 76
        // exist to give. The reduction stops at the decimal point and what is left fits the
        // mantissa, so the result is 78 digits wide: the one case where the width says nothing
        // about whether the value was reduced, and the reason the band cannot be enforced by
        // testing the width at the point a value is packed.
        var value = BigDecimal.FromScaled(
            BigInteger.Parse(
                "-33097929454724321734568101113074430888076216457856852223986730306351718400624",
                CultureInfo.InvariantCulture),
            38);

        var result = BigDecimal.Pow(value, 2);

        result.Scale.Should().Be(0);
        result.Precision.Should().Be(78);
        result.GetMantissa()
            .Should()
            .Be(BigInteger.Parse(
                "109547293418990783746199248957372052389243190677326517524758093283100022219556",
                CultureInfo.InvariantCulture));
    }

    [Fact]
    public void APowerThatOutgrowsTheMantissaWithNoFractionToGiveUp_Throws()
    {
        var act = () => BigDecimal.Pow(Parse("10"), 100);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void APowerBelowTheFloorOfTheRange_IsZero()
    {
        var result = BigDecimal.Pow(Parse("0.5"), 2000);

        result.IsZero.Should().BeTrue();
        result.Scale.Should().Be(BigDecimal.MaxScale);
    }

    [Fact]
    public void AnExactReciprocal_IsReducedToItsShortestScale()
    {
        var result = BigDecimal.Pow(Parse("2"), -3);

        Text(result).Should().Be("0.125");
        result.Scale.Should().Be(3);
    }

    [Fact]
    public void AReciprocalOfOne_IsOneAtScaleZero()
    {
        var result = BigDecimal.Pow(Parse("1"), -5);

        Text(result).Should().Be("1");
        result.Scale.Should().Be(0);
    }

    [Fact]
    public void AReciprocalIsAnsweredWhereTheAnswerFits_NotWhereThePowerDoes()
    {
        // 2^300 does not fit the mantissa and 2^-300 is about 4.9e-91, well inside the range.
        // Composing the answer as One / Pow(2, 300) would throw here; computing it does not.
        var act = () => BigDecimal.Pow(Parse("2"), 300);
        act.Should().Throw<OverflowException>();

        var result = BigDecimal.Pow(Parse("2"), -300);

        result.IsZero.Should().BeFalse();
        Text(result).Should().StartWith("0." + new string('0', 90) + "4909093465");
        result.Scale.Should().Be(166);
    }

    [Fact]
    public void AReciprocalAgreesWithTheDivisionWhereThePowerIsExact()
    {
        var value = Parse("1.05");

        var direct = BigDecimal.Pow(value, -30);
        var composed = BigDecimal.Divide(BigDecimal.One, BigDecimal.Pow(value, 30));

        direct.Should().Be(composed);
        direct.Scale.Should().Be(composed.Scale);
    }

    [Fact]
    public void AReciprocalOfAPowerFarOutsideTheRange_IsZeroAndNotARefusal()
    {
        // 10^300 is nowhere near representable and 10^-300 is below the floor of the range, so the
        // underflow rule answers rather than an overflow being reported for an intermediate.
        var result = BigDecimal.Pow(Parse("10"), -300);

        result.IsZero.Should().BeTrue();
        result.Scale.Should().Be(BigDecimal.MaxScale);
    }

    [Fact]
    public void TheSmallestRepresentableReciprocal_IsAnswered()
    {
        var floor = BigDecimal.Pow(Parse("10"), -BigDecimal.MaxScale);

        Text(floor).Should().Be("0." + new string('0', BigDecimal.MaxScale - 1) + "1");
        BigDecimal.Pow(Parse("10"), -(BigDecimal.MaxScale + 1)).IsZero.Should().BeTrue();
    }

    [Fact]
    public void AReciprocalWhosePowerDoesNotFitTheWorkingWidth_IsStillAnswered()
    {
        // 2^600 is 181 digits and outgrows every buffer the chain runs in, while 2^-600 is about
        // 2.4e-181 and is comfortably inside the range. The operation throws for the result, never
        // for an intermediate.
        var refused = () => BigDecimal.Pow(Parse("2"), 600);
        refused.Should().Throw<OverflowException>();

        var result = BigDecimal.Pow(Parse("2"), -600);

        result.IsZero.Should().BeFalse();
        result.Scale.Should().Be(BigDecimal.MaxScale);
    }

    [Fact]
    public void TheWidestExponents_DoNotOverflowTheRunningScale()
    {
        // The chain's scale doubles on every squaring once a value has no fractional digits left,
        // so an exponent this size would take it past what an int holds if it did not saturate.
        var overflows = () => BigDecimal.Pow(Parse("10"), int.MaxValue);
        overflows.Should().Throw<OverflowException>();

        BigDecimal.Pow(Parse("10"), -int.MaxValue).IsZero.Should().BeTrue();
        BigDecimal.Pow(Parse("10"), int.MinValue).IsZero.Should().BeTrue();
    }

    [Fact]
    public void AZeroBaseWithANegativeExponent_ThrowsDivideByZero()
    {
        var act = () => BigDecimal.Pow(BigDecimal.Zero, -1);

        act.Should().Throw<DivideByZeroException>();
    }

    [Fact]
    public void AReciprocalOfAnUnderflowedPower_ReportsAnOverflowAndNotADivisionByZero()
    {
        // 0.5^2000 underflows to zero, so its reciprocal is a value too large to represent. The
        // fault is the size of the answer, not the base, and naming it a division by zero would
        // send the caller to the wrong operand.
        var act = () => BigDecimal.Pow(Parse("0.5"), -2000);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void TheWidestNegativeExponent_DoesNotWrap()
    {
        // -(int.MinValue) does not fit an int, so the magnitude is taken through a long.
        var result = BigDecimal.Pow(Parse("1.0000001"), int.MinValue);

        result.IsZero.Should().BeFalse();
        result.Should().BeLessThan(BigDecimal.One);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void APowerOfTwoAndFive_HasAnExactReciprocal(int exponent)
    {
        var result = BigDecimal.Pow(Parse("10"), -exponent);

        Text(result).Should().Be("0." + new string('0', exponent - 1) + "1");
        result.Scale.Should().Be(exponent);
    }
}
