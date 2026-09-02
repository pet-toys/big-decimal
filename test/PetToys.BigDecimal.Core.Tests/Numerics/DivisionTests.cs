using System;
using System.Globalization;
using System.Numerics;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Named examples for the division primitive, kept beside the randomised suite so that a defect it
/// found once cannot come back unnoticed because a soak happened to draw kinder operands.
/// </summary>
/// <remarks>
/// All of these once failed together, for one reason: the trial quotient saturates when the running
/// remainder's leading word equals the divisor's, and the partial remainder that the correction
/// step reads was truncated to 64 bits at that point. The correction then fired on an estimate that
/// was already right, and nothing repairs an estimate that came out too small, so the quotient word
/// was short by one, the remainder long by a divisor, and every later word was computed from a
/// remainder that had already been corrupted.
/// </remarks>
public sealed class DivisionTests
{
    // The narrowest operands that reach the saturation point: three numerator words over two
    // divisor words, chosen so that the exact quotient word is 2^64 - 1. Below this width the
    // primitive takes its single-word path and never estimates at all.
    private const string SaturatingDividend = "3011454863741893821962847635079523438298849281864699066895";

    private const string SaturatingDivisor = "163251295280550000920325954839477876654";

    [Fact]
    public void Divide_IsExactWhenTheTrialQuotientSaturates()
    {
        var dividend = Parse(SaturatingDividend);
        var divisor = Parse(SaturatingDivisor);

        var quotient = BigDecimal.Divide(dividend, divisor, 0, MidpointRounding.ToZero);

        OracleValue.Observe(quotient).Unscaled.Should().Be(new BigInteger(ulong.MaxValue));
    }

    [Fact]
    public void Remainder_IsExactWhenTheTrialQuotientSaturates()
    {
        var dividend = Parse(SaturatingDividend);
        var divisor = Parse(SaturatingDivisor);

        OracleValue.Observe(dividend % divisor)
            .Should().Be(BigIntegerOracle.Remainder(OracleValue.Observe(dividend), OracleValue.Observe(divisor)));
    }

    [Fact]
    public void DivideAtAnExplicitScale_IsCorrectlyRoundedForAWideQuotient()
    {
        // Found by the randomised suite. The exact quotient is MaxValue x 10^36, which reduces to
        // 77 digits at scale 50; what used to come back agreed for 55 digits and then diverged, an
        // error around 10^20 in the returned mantissa rather than a unit in the last place.
        var divisor = BigDecimal.FromScaled(BigInteger.Pow(10, 51), 0);

        var quotient = BigDecimal.Divide(BigDecimal.MaxValue, divisor, 87, MidpointRounding.ToZero);

        var expected = BigIntegerOracle.Divide(
            OracleValue.Observe(BigDecimal.MaxValue),
            OracleValue.Observe(divisor),
            87,
            MidpointRounding.ToZero);

        OracleValue.Observe(quotient).Should().Be(expected);
    }

    [Fact]
    public void DivideAtAnExplicitScale_KeepsPrecisionPastFiftySevenDigits()
    {
        // The same defect stated as its shape rather than as one example: the intermediate quotient
        // used to be correct only down to roughly its 57th significant digit, so the error grew as
        // the reduction that follows took away fewer digits.
        var divisor = BigDecimal.FromScaled(BigInteger.Pow(10, 40), 0);

        var quotient = BigDecimal.Divide(BigDecimal.MaxValue, divisor, 87, MidpointRounding.ToZero);

        var expected = BigIntegerOracle.Divide(
            OracleValue.Observe(BigDecimal.MaxValue),
            OracleValue.Observe(divisor),
            87,
            MidpointRounding.ToZero);

        OracleValue.Observe(quotient).Should().Be(expected);
    }

    [Fact]
    public void Remainder_IsExactWhenTheDividendIsAMultipleOfTheDivisor()
    {
        // Found by the randomised suite. An integer divided by a power of ten leaves no remainder,
        // whatever scale either side is written at; what used to come back was roughly three
        // quarters of the divisor.
        var dividend = BigDecimal.FromScaled((BigInteger.One << 192) - BigInteger.One, 5);
        var divisor = BigDecimal.FromScaled(BigInteger.Pow(10, 74), 116);

        OracleValue.Observe(dividend % divisor)
            .Should().Be(BigIntegerOracle.Remainder(OracleValue.Observe(dividend), OracleValue.Observe(divisor)));
    }

    [Fact]
    public void DivideAtTheDefaultScale_IsCorrectlyRoundedForAWideAlignment()
    {
        // Found by a soak run at fifty thousand cases per test. The quotient used to come back more
        // than half a unit in the last place from the exact one, off by around 10^40 at the
        // reported scale rather than by a rounding decision.
        var dividend = BigDecimal.FromScaled((BigInteger.One << 192) - BigInteger.One, 255);
        var divisor = BigDecimal.FromScaled(BigInteger.Pow(10, 32), 254);

        var quotient = OracleValue.Observe(dividend / divisor);
        var (numerator, denominator) = BigIntegerOracle.ExactQuotient(
            OracleValue.Observe(dividend),
            OracleValue.Observe(divisor),
            quotient.Scale);

        (BigInteger.Abs((quotient.Unscaled * denominator) - numerator) * 2)
            .Should().BeLessThanOrEqualTo(BigInteger.Abs(denominator));
    }

    [Theory]

    // Exact at the scale difference, which the trial division reaches without lifting anything.
    [InlineData("100", "10")]
    [InlineData("6", "3")]
    [InlineData("1.000", "1")]

    // Exact only after a lift, which the divisor's factors give: 8 is 2^3, 4 is 2^2, 16 is 2^4,
    // 10 is 2 times 5 and 250 is 2 times 5^3.
    [InlineData("1", "8")]
    [InlineData("100", "4")]
    [InlineData("1", "16")]
    [InlineData("7", "250")]
    [InlineData("0.5", "0.0025")]
    public void Divide_AgreesWithDecimalOnAnExactQuotient(string dividend, string divisor)
    {
        // decimal reduces an exact quotient to its shortest scale and never below the operands'
        // scale difference, which is the rule this type follows, so it pins the scale as well as
        // the value. The paths that look for an exact quotient early have to land on exactly what
        // the full-precision path would have produced.
        var quotient = Parse(dividend) / Parse(divisor);
        var reference = decimal.Parse(dividend, CultureInfo.InvariantCulture)
            / decimal.Parse(divisor, CultureInfo.InvariantCulture);

        quotient.ToString(CultureInfo.InvariantCulture)
            .Should()
            .Be(reference.ToString(CultureInfo.InvariantCulture));
    }

    private static BigDecimal Parse(string text) => BigDecimal.Parse(text, CultureInfo.InvariantCulture);
}
