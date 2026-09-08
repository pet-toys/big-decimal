using System;
using System.Numerics;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Checks the arithmetic against an independent <see cref="BigInteger"/> oracle over randomised
/// operands, comparing both the value and the scale of every result.
/// </summary>
public sealed class ArithmeticFuzzTests
{
    [Theory]
    [FuzzData]
    public void Add_MatchesTheOracle(int seed, int cases) =>
        RunPairs(seed, cases, "+", (l, r) => l + r, BigIntegerOracle.Add);

    [Theory]
    [FuzzData]
    public void Subtract_MatchesTheOracle(int seed, int cases) =>
        RunPairs(seed, cases, "-", (l, r) => l - r, BigIntegerOracle.Subtract);

    [Theory]
    [FuzzData]
    public void Multiply_MatchesTheOracle(int seed, int cases) =>
        RunPairs(seed, cases, "*", (l, r) => l * r, BigIntegerOracle.Multiply);

    [Theory]
    [FuzzData]
    public void Remainder_MatchesTheOracle(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));

        for (var index = 0; index < cases; index++)
        {
            var (left, right) = generator.NextPair();
            var dividend = OracleValue.From(left);
            var divisor = OracleValue.From(right);

            OracleAssert.Matches(
                FuzzContext.Of(seed, index, left, right),
                "%",
                () => left.Value % right.Value,
                () => BigIntegerOracle.Remainder(dividend, divisor));
        }
    }

    [Theory]
    [FuzzData]
    public void DivideAtAScale_MatchesTheOracle(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);
        var modes = Enum.GetValues<MidpointRounding>();

        for (var index = 0; index < cases; index++)
        {
            var (left, right) = generator.NextPair();
            var scale = random.Next(0, BigDecimal.MaxScale + 1);
            var mode = modes[random.Next(modes.Length)];
            var dividend = OracleValue.From(left);
            var divisor = OracleValue.From(right);

            OracleAssert.Matches(
                FuzzContext.Of(seed, index, left, right),
                $"/ at scale {scale} ({mode})",
                () => BigDecimal.Divide(left.Value, right.Value, scale, mode),
                () => BigIntegerOracle.Divide(dividend, divisor, scale, mode));
        }
    }

    [Theory]
    [FuzzData]
    public void DivideAtTheDefaultScale_SatisfiesTheContract(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));

        for (var index = 0; index < cases; index++)
        {
            var (left, right) = generator.NextPair();
            var context = FuzzContext.Of(seed, index, left, right);
            var dividend = OracleValue.From(left);
            var divisor = OracleValue.From(right);

            if (divisor.Sign == 0)
            {
                var byZero = () => left.Value / right.Value;
                byZero.Should().Throw<DivideByZeroException>("{0} divides by zero", context);

                continue;
            }

            BigDecimal quotient;
            try
            {
                quotient = left.Value / right.Value;
            }
            catch (OverflowException)
            {
                var (numerator, denominator) = BigIntegerOracle.ExactQuotient(dividend, divisor, 0);

                BigInteger.Abs(numerator / denominator)
                    .Should()
                    .BeGreaterThan(BigIntegerOracle.MaxMagnitude, "{0} / overflowed, so its integer part must not fit", context);

                continue;
            }

            AssertQuotientContract(context, dividend, divisor, OracleValue.Observe(quotient));
        }
    }

    private static void AssertQuotientContract(
        in FuzzContext context,
        OracleValue dividend,
        OracleValue divisor,
        OracleValue quotient)
    {
        quotient.Scale.Should().BeInRange(0, BigDecimal.MaxScale, "{0} / produced {1}", context, quotient);
        BigInteger.Abs(quotient.Unscaled)
            .Should().BeLessThanOrEqualTo(BigIntegerOracle.MaxMagnitude, "{0} / produced {1}", context, quotient);

        // The value is checked against the exact quotient at the scale the result itself reports:
        // the contract fixes how close the answer has to be, not which scale the divider picks.
        var (numerator, denominator) = BigIntegerOracle.ExactQuotient(dividend, divisor, quotient.Scale);
        var error = BigInteger.Abs((quotient.Unscaled * denominator) - numerator) * 2;
        var unit = BigInteger.Abs(denominator);

        error.Should().BeLessThanOrEqualTo(
            unit,
            "{0} / produced {1}, which is more than half a unit in the last place from the exact quotient",
            context,
            quotient);

        if (error == unit)
        {
            quotient.Unscaled.IsEven.Should().BeTrue("{0} / landed on a tie, which resolves to even", context);
        }

        var floor = Math.Max(0, dividend.Scale - divisor.Scale);
        if (BigInteger.Remainder(numerator, denominator).IsZero && quotient.Scale > floor)
        {
            (quotient.Unscaled % 10).Should().NotBe(
                BigInteger.Zero,
                "{0} / divided exactly, so {1} should have been reduced to its shortest scale above {2}",
                context,
                quotient,
                floor);
        }
    }

    [Theory]
    [FuzzData]
    public void Pow_MatchesTheOracle(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);

        for (var index = 0; index < cases; index++)
        {
            var value = generator.Next();
            var exponent = NextExponent(random, value);
            var basis = OracleValue.From(value);

            OracleAssert.Matches(
                FuzzContext.Of(seed, index, value),
                $"^ {exponent}",
                () => BigDecimal.Pow(value.Value, exponent),
                () => BigIntegerOracle.Pow(basis, exponent));
        }
    }

    [Theory]
    [FuzzData]
    public void PowAtANegativeExponent_SatisfiesTheContract(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);

        for (var index = 0; index < cases; index++)
        {
            var value = generator.Next();
            var exponent = Math.Max(NextExponent(random, value), 1);
            var context = FuzzContext.Of(seed, index, value);
            var basis = OracleValue.From(value);

            if (basis.Sign == 0)
            {
                var byZero = () => BigDecimal.Pow(value.Value, -exponent);
                byZero.Should().Throw<DivideByZeroException>("{0} ^ -{1} divides by zero", context, exponent);

                continue;
            }

            var (numerator, denominator) = BigIntegerOracle.ReciprocalOfPow(basis, exponent);

            BigDecimal reciprocal;
            try
            {
                reciprocal = BigDecimal.Pow(value.Value, -exponent);
            }
            catch (OverflowException)
            {
                // The only reason to refuse is that the answer itself does not fit, which is a
                // statement about its integer part and nothing to do with the power's own size.
                BigInteger.Abs(numerator / denominator)
                    .Should()
                    .BeGreaterThan(
                        BigIntegerOracle.MaxMagnitude,
                        "{0} ^ -{1} refused, so its integer part must not fit",
                        context,
                        exponent);

                continue;
            }

            AssertReciprocalContract(context, exponent, numerator, denominator, OracleValue.Observe(reciprocal));
        }
    }

    private static void AssertReciprocalContract(
        in FuzzContext context,
        int exponent,
        BigInteger numerator,
        BigInteger denominator,
        OracleValue reciprocal)
    {
        reciprocal.Scale.Should().BeInRange(0, BigDecimal.MaxScale, "{0} ^ -{1} produced {2}", context, exponent, reciprocal);
        BigInteger.Abs(reciprocal.Unscaled)
            .Should()
            .BeLessThanOrEqualTo(BigIntegerOracle.MaxMagnitude, "{0} ^ -{1} produced {2}", context, exponent, reciprocal);

        var negative = denominator.Sign < 0 && !reciprocal.Unscaled.IsZero;
        (reciprocal.Sign < 0).Should().Be(
            negative,
            "{0} ^ -{1} takes the sign of the power it inverts",
            context,
            exponent);

        // Checked at the scale the result itself reports, exactly as a default-scale division is:
        // the contract fixes how close the answer has to be and not which scale it comes back at.
        var unit = BigInteger.Abs(denominator);
        var lifted = numerator * BigIntegerOracle.Pow10(reciprocal.Scale);
        var error = BigInteger.Abs((BigInteger.Abs(reciprocal.Unscaled) * unit) - lifted) * 2;

        error.Should().BeLessThanOrEqualTo(
            unit,
            "{0} ^ -{1} produced {2}, which is more than half a unit in the last place from the exact reciprocal",
            context,
            exponent,
            reciprocal);

        if (error == unit)
        {
            reciprocal.Unscaled.IsEven.Should().BeTrue("{0} ^ -{1} landed on a tie, which resolves to even", context, exponent);
        }
    }

    /// <summary>
    /// Picks an exponent that lands on one of the two edges the exactness guarantee turns on, or
    /// well inside them.
    /// </summary>
    /// <remarks>
    /// Sampling uniformly would spend the corpus in the interior, where a reduction that is one
    /// digit wrong cannot show. The two edges are the scale reaching <c>MaxScale</c> and the exact
    /// power reaching the mantissa's capacity, and each is approached from both sides. The ceiling
    /// keeps the exact power a number the oracle can hold: a 78-digit operand at the digit edge
    /// takes an exponent of one, so the widest cases stay small on their own.
    /// </remarks>
    private static int NextExponent(Random random, FuzzValue value)
    {
        const int Ceiling = 40;

        var digits = BigIntegerOracle.DigitCount(BigInteger.Abs(value.Unscaled));
        var atTheMantissa = Math.Max(BigIntegerOracle.MaxSignificantDigits / Math.Max(digits, 1), 1);
        var atTheScale = value.Scale == 0 ? Ceiling : Math.Max(BigDecimal.MaxScale / value.Scale, 1);

        var target = random.Next(3) switch
        {
            0 => atTheMantissa,
            1 => atTheScale,
            _ => random.Next(0, 8),
        };

        var exponent = Math.Clamp(target + random.Next(-2, 3), 0, Ceiling);

        // The oracle raises the exact power and lifts it by the result's scale, so the case has to
        // stay a number worth multiplying. Three thousand fractional digits is an order of
        // magnitude past MaxScale, which is where the interesting reductions are.
        return value.Scale == 0 ? exponent : Math.Min(exponent, Math.Max(3000 / value.Scale, 1));
    }

    private static void RunPairs(
        int seed,
        int cases,
        string operation,
        Func<BigDecimal, BigDecimal, BigDecimal> actual,
        Func<OracleValue, OracleValue, OracleValue> expected)
    {
        var generator = new ValueGenerator(new Random(seed));

        for (var index = 0; index < cases; index++)
        {
            var (left, right) = generator.NextPair();

            OracleAssert.Matches(
                FuzzContext.Of(seed, index, left, right),
                operation,
                () => actual(left.Value, right.Value),
                () => expected(OracleValue.From(left), OracleValue.From(right)));
        }
    }
}
