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
    public void Sorting_StaysTotalWithTheNonFiniteValuesMixedIn(int seed, int cases)
    {
        // The three non-finite values are singletons and are answered before any scale work, so
        // the other operand's shape cannot change the result. What is worth randomising is the
        // company they keep: a total order only wrong in a long array is one only Array.Sort finds.
        var random = new Random(seed);
        var generator = new ValueGenerator(random);
        var rounds = Math.Max(cases / 32, 1);

        for (var round = 0; round < rounds; round++)
        {
            var values = new BigDecimal[32];
            for (var index = 0; index < values.Length - 3; index++)
            {
                values[index] = generator.Next().Value;
            }

            values[^3] = BigDecimal.NaN;
            values[^2] = BigDecimal.PositiveInfinity;
            values[^1] = BigDecimal.NegativeInfinity;
            random.Shuffle(values);

            Array.Sort(values);

            for (var index = 1; index < values.Length; index++)
            {
                values[index - 1].CompareTo(values[index])
                    .Should().BeLessThanOrEqualTo(0, "seed {0} round {1} index {2} is out of order", seed, round, index);
            }

            BigDecimal.IsNaN(values[0]).Should().BeTrue("NaN sorts below every other value");
            values[1].Should().Be(BigDecimal.NegativeInfinity, "and negative infinity below every finite one");
            values[^1].Should().Be(BigDecimal.PositiveInfinity);
        }
    }

    [Theory]
    [FuzzData]
    public void ANonFiniteOperand_KeepsOrderingAntisymmetricAgainstAnyValue(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));
        BigDecimal[] specials = [BigDecimal.NaN, BigDecimal.PositiveInfinity, BigDecimal.NegativeInfinity];

        for (var index = 0; index < cases; index++)
        {
            var finite = generator.Next();
            var context = FuzzContext.Of(seed, index, finite);

            foreach (var special in specials)
            {
                Math.Sign(special.CompareTo(finite.Value))
                    .Should().Be(-Math.Sign(finite.Value.CompareTo(special)), "{0} ordering is antisymmetric", context);

                // Unordered by the operators whichever side NaN is on, and never equal by them.
                (special < finite.Value).Should().Be(!BigDecimal.IsNaN(special) && special.CompareTo(finite.Value) < 0);
                (finite.Value < special).Should().Be(!BigDecimal.IsNaN(special) && finite.Value.CompareTo(special) < 0);
                (special == finite.Value).Should().BeFalse("{0} is never equal to a finite value", context);
            }
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
