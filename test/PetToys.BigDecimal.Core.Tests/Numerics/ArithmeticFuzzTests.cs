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
