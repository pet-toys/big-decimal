using System;
using System.Numerics;
using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Tests of the oracles. An oracle is a second implementation, and a wrong one reports its own
/// errors as the implementation's — so the properties every randomised suite leans on are pinned
/// here, against `decimal` and against worked examples.
/// </summary>
public sealed class OracleTests
{
    [Fact]
    public void Rounding_HappensInOneStepFromTheFullInput()
    {
        // 1.45 is below the halfway point, so one step from the full input gives 1. Chaining
        // happens to agree here — ties-to-even takes 1.45 to 1.4 and then to 1 — which is why the
        // case that actually separates them is further down.
        var value = new OracleValue(145, 2);

        BigIntegerOracle.Round(value, 0, MidpointRounding.ToEven).Should().Be(new OracleValue(1, 0));

        var chained = BigIntegerOracle.Round(BigIntegerOracle.Round(value, 1, MidpointRounding.ToEven), 0, MidpointRounding.ToEven);
        chained.Should().Be(new OracleValue(1, 0), "one step and two steps agree here only because the first step rounds down");

        // The case where they genuinely part company: .5 followed by a non-zero digit is not a tie.
        var notATie = new OracleValue(1_501, 3);
        BigIntegerOracle.Round(notATie, 0, MidpointRounding.ToEven).Should().Be(new OracleValue(2, 0));
        BigIntegerOracle.Round(BigIntegerOracle.Round(notATie, 2, MidpointRounding.ToEven), 0, MidpointRounding.ToEven)
            .Should().Be(new OracleValue(2, 0));

        var doubleRounds = new OracleValue(1_450_001, 6);
        BigIntegerOracle.Round(doubleRounds, 0, MidpointRounding.ToEven).Should().Be(new OracleValue(1, 0));
        BigIntegerOracle.Round(BigIntegerOracle.Round(doubleRounds, 1, MidpointRounding.ToEven), 0, MidpointRounding.ToEven)
            .Should().Be(new OracleValue(2, 0), "this is what a chained reference gets wrong");
    }

    [Theory]
    [InlineData(MidpointRounding.ToEven)]
    [InlineData(MidpointRounding.AwayFromZero)]
    [InlineData(MidpointRounding.ToZero)]
    [InlineData(MidpointRounding.ToNegativeInfinity)]
    [InlineData(MidpointRounding.ToPositiveInfinity)]
    public void EveryRoundingMode_AgreesWithDecimal(MidpointRounding mode)
    {
        var random = new Random(4_242);

        for (var index = 0; index < 2_000; index++)
        {
            var unscaled = new BigInteger(random.NextInt64(-1_000_000_000_000L, 1_000_000_000_000L));
            var scale = random.Next(0, 9);
            var target = random.Next(0, scale + 1);
            var value = new OracleValue(unscaled, scale);

            BigIntegerOracle.Round(value, target, mode)
                .Should().Be(DecimalParityOracle.Round(value, target, mode), "rounding {0} to scale {1}", value, target);
        }
    }

    [Fact]
    public void Arithmetic_AgreesWithDecimalInsideItsDomain()
    {
        var random = new Random(99);

        for (var index = 0; index < 2_000; index++)
        {
            var left = new OracleValue(random.NextInt64(-1_000_000_000L, 1_000_000_000L), random.Next(0, 6));
            var right = new OracleValue(random.NextInt64(-1_000_000_000L, 1_000_000_000L), random.Next(0, 6));

            DecimalParityOracle.TryToDecimal(left, out var leftDecimal).Should().BeTrue();
            DecimalParityOracle.TryToDecimal(right, out var rightDecimal).Should().BeTrue();

            BigIntegerOracle.Add(left, right).Should().Be(DecimalParityOracle.From(leftDecimal + rightDecimal));
            BigIntegerOracle.Subtract(left, right).Should().Be(DecimalParityOracle.From(leftDecimal - rightDecimal));
            BigIntegerOracle.Multiply(left, right).Should().Be(DecimalParityOracle.From(leftDecimal * rightDecimal));

            if (right.Sign != 0)
            {
                BigIntegerOracle.Remainder(left, right).Should().Be(DecimalParityOracle.From(leftDecimal % rightDecimal));
            }
        }
    }

    [Fact]
    public void ADecimalRoundTrip_PreservesTheScale()
    {
        var random = new Random(7);

        for (var index = 0; index < 1_000; index++)
        {
            var value = new OracleValue(random.NextInt64(-1_000_000_000L, 1_000_000_000L), random.Next(0, 20));

            DecimalParityOracle.TryToDecimal(value, out var converted).Should().BeTrue();
            DecimalParityOracle.From(converted).Should().Be(value);
        }
    }

    [Fact]
    public void ADecimalOutsideTheDomain_IsRefusedRatherThanTruncated()
    {
        DecimalParityOracle.TryToDecimal(new OracleValue(BigInteger.One, 29), out _).Should().BeFalse();
        DecimalParityOracle.TryToDecimal(new OracleValue(BigInteger.One << 96, 0), out _).Should().BeFalse();
    }

    [Fact]
    public void ScaleRules_FollowTheSpecification()
    {
        var one = new OracleValue(15, 1);
        var two = new OracleValue(225, 2);

        BigIntegerOracle.Add(one, two).Should().Be(new OracleValue(375, 2), "a sum keeps the wider scale");
        BigIntegerOracle.Subtract(new OracleValue(1_500, 3), one)
            .Should().Be(new OracleValue(0, 3), "a difference keeps the wider scale even when the digits cancel");
        BigIntegerOracle.Multiply(one, two).Should().Be(new OracleValue(3_375, 3), "a product sums the scales");
        BigIntegerOracle.Remainder(new OracleValue(750, 2), new OracleValue(2, 0))
            .Should().Be(new OracleValue(150, 2), "a remainder keeps the wider scale");
        BigIntegerOracle.Remainder(new OracleValue(-220_573, 3), new OracleValue(162_763_635, 5))
            .Should().Be(new OracleValue(-220_573, 3), "a dividend smaller than the divisor comes back untouched");
        BigIntegerOracle.Remainder(new OracleValue(0, 3), new OracleValue(25, 1))
            .Should().Be(new OracleValue(0, 3), "a zero dividend keeps its own scale");
    }

    [Fact]
    public void AProductBeyondTheMantissa_NormalisesToSeventySevenDigits()
    {
        // The exact product has 79 digits. Dropping one would already fit the mantissa, but a
        // result that does not fit is reduced into the band where every value of that width fits.
        var operand = new OracleValue(BigInteger.Pow(10, 39) + 5, 20);
        var other = new OracleValue(BigInteger.Pow(10, 39) + 7, 20);

        var product = BigIntegerOracle.Multiply(operand, other);

        BigIntegerOracle.DigitCount(BigInteger.Abs(product.Unscaled)).Should().Be(77);
        product.Scale.Should().Be(38);
    }

    [Fact]
    public void AProductOverTheScaleCap_IsCappedRatherThanRefused()
    {
        var operand = new OracleValue(BigInteger.Parse("1" + new string('1', 39), System.Globalization.CultureInfo.InvariantCulture), 200);
        var other = new OracleValue(BigInteger.Parse("7" + new string('7', 29), System.Globalization.CultureInfo.InvariantCulture), 100);

        var product = BigIntegerOracle.Multiply(operand, other);

        product.Scale.Should().Be(BigIntegerOracle.MaxScale);
        product.Sign.Should().NotBe(0);
    }

    [Fact]
    public void AnIntegerPartBeyondTheMantissa_Overflows()
    {
        var max = new OracleValue(BigIntegerOracle.MaxMagnitude, 0);

        var act = () => BigIntegerOracle.Add(max, new OracleValue(BigInteger.One, 0));

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void WideningPastTheMantissa_Overflows()
    {
        var value = new OracleValue(BigIntegerOracle.MaxMagnitude, 0);

        var act = () => BigIntegerOracle.WithScale(value, 1, MidpointRounding.ToEven);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Round_OnlyNarrows()
    {
        var value = new OracleValue(15, 1);

        BigIntegerOracle.Round(value, 5, MidpointRounding.ToEven).Should().Be(value);
        BigIntegerOracle.WithScale(value, 5, MidpointRounding.ToEven).Should().Be(new OracleValue(150_000, 5));
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(9, 0, 1)]
    [InlineData(10, 0, 2)]
    [InlineData(0, 0, 1)]
    [InlineData(99, 0, 2)]
    public void DigitCount_CountsDecimalDigits(long unscaled, int scale, int expected)
    {
        _ = scale;

        BigIntegerOracle.DigitCount(new BigInteger(unscaled)).Should().Be(expected);
    }

    [Fact]
    public void DigitCount_AgreesWithTheTextAtTheBoundary()
    {
        BigIntegerOracle.DigitCount(BigIntegerOracle.MaxMagnitude).Should().Be(78);
        BigIntegerOracle.DigitCount(BigInteger.Pow(10, 76)).Should().Be(77);
    }
}
