using System;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Checks ordering, equality and hashing against the oracle over randomised operands, including
/// the values that are numerically equal at different scales.
/// </summary>
public sealed class ComparisonFuzzTests
{
    [Theory]
    [FuzzData]
    public void CompareTo_MatchesTheOracle(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));

        for (var index = 0; index < cases; index++)
        {
            var (left, right) = generator.NextPair();
            var context = FuzzContext.Of(seed, index, left, right);
            var required = Math.Sign(BigIntegerOracle.Compare(OracleValue.From(left), OracleValue.From(right)));

            Math.Sign(left.Value.CompareTo(right.Value)).Should().Be(required, "{0} CompareTo", context);
            left.Value.Equals(right.Value).Should().Be(required == 0, "{0} Equals", context);
            (left.Value == right.Value).Should().Be(required == 0, "{0} ==", context);
            (left.Value != right.Value).Should().Be(required != 0, "{0} !=", context);
            (left.Value < right.Value).Should().Be(required < 0, "{0} <", context);
            (left.Value <= right.Value).Should().Be(required <= 0, "{0} <=", context);
            (left.Value > right.Value).Should().Be(required > 0, "{0} >", context);
            (left.Value >= right.Value).Should().Be(required >= 0, "{0} >=", context);
        }
    }

    [Theory]
    [FuzzData]
    public void CompareTo_IsAntisymmetric(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));

        for (var index = 0; index < cases; index++)
        {
            var (left, right) = generator.NextPair();
            var context = FuzzContext.Of(seed, index, left, right);

            Math.Sign(left.Value.CompareTo(right.Value))
                .Should().Be(-Math.Sign(right.Value.CompareTo(left.Value)), "{0} ordering is antisymmetric", context);
        }
    }

    [Theory]
    [FuzzData]
    public void CompareTo_IsTransitive(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));

        for (var index = 0; index < cases; index++)
        {
            var first = generator.Next();
            var second = generator.Next();
            var third = generator.Next();

            var ordered = new[] { first, second, third };
            Array.Sort(ordered, (a, b) => a.Value.CompareTo(b.Value));

            var context = FuzzContext.Of(seed, index, ordered[0], ordered[2]);

            ordered[0].Value.CompareTo(ordered[1].Value).Should().BeLessThanOrEqualTo(0, "{0} sorted", context);
            ordered[1].Value.CompareTo(ordered[2].Value).Should().BeLessThanOrEqualTo(0, "{0} sorted", context);
            ordered[0].Value.CompareTo(ordered[2].Value).Should().BeLessThanOrEqualTo(0, "{0} sorted", context);
        }
    }

    [Theory]
    [FuzzData]
    public void GetHashCode_AgreesAcrossScales(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var wider = drawn.Scale + random.Next(1, 6);
            if (wider > BigDecimal.MaxScale)
            {
                continue;
            }

            BigDecimal widened;
            try
            {
                widened = drawn.Value.WithScale(wider);
            }
            catch (OverflowException)
            {
                continue;
            }

            var context = FuzzContext.Of(seed, index, drawn);

            widened.Should().Be(drawn.Value, "{0} widening to scale {1} keeps the number", context, wider);
            widened.GetHashCode().Should().Be(
                drawn.Value.GetHashCode(),
                "{0} equal values must hash alike at scale {1}",
                context,
                wider);
        }
    }
}
