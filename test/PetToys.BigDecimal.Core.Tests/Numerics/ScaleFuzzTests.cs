using System;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Checks scale widening and narrowing against the oracle across every rounding mode and across
/// the overflow boundary. This ran once as a throwaway probe during an earlier review; here it
/// is a test.
/// </summary>
public sealed class ScaleFuzzTests
{
    private static readonly MidpointRounding[] Modes = Enum.GetValues<MidpointRounding>();

    [Theory]
    [FuzzData]
    public void WithScale_MatchesTheOracle(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var target = random.Next(0, BigDecimal.MaxScale + 1);
            var mode = Modes[random.Next(Modes.Length)];

            OracleAssert.Matches(
                FuzzContext.Of(seed, index, drawn),
                $"WithScale({target}, {mode})",
                () => drawn.Value.WithScale(target, mode),
                () => BigIntegerOracle.WithScale(OracleValue.From(drawn), target, mode));
        }
    }

    [Theory]
    [FuzzData]
    public void WithScale_MatchesTheOracleAtTheOverflowBoundary(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            if (drawn.Value.IsZero)
            {
                continue;
            }

            // The scale at which widening stops fitting, approached from both sides, so that the
            // last accepted scale and the first refused one are both exercised.
            var headroom = BigIntegerOracle.MaxSignificantDigits
                - BigIntegerOracle.DigitCount(System.Numerics.BigInteger.Abs(drawn.Unscaled));
            var target = Math.Clamp(drawn.Scale + headroom + random.Next(-2, 3), 0, BigDecimal.MaxScale);
            var mode = Modes[random.Next(Modes.Length)];

            OracleAssert.Matches(
                FuzzContext.Of(seed, index, drawn),
                $"WithScale({target}, {mode}) at the boundary",
                () => drawn.Value.WithScale(target, mode),
                () => BigIntegerOracle.WithScale(OracleValue.From(drawn), target, mode));
        }
    }

    [Theory]
    [FuzzData]
    public void Round_MatchesTheOracle(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var target = random.Next(0, BigDecimal.MaxScale + 1);
            var mode = Modes[random.Next(Modes.Length)];

            OracleAssert.Matches(
                FuzzContext.Of(seed, index, drawn),
                $"Round({target}, {mode})",
                () => BigDecimal.Round(drawn.Value, target, mode),
                () => BigIntegerOracle.Round(OracleValue.From(drawn), target, mode));
        }
    }

    [Theory]
    [FuzzData]
    public void Round_NeverWidens(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var target = random.Next(drawn.Scale, BigDecimal.MaxScale + 1);
            var context = FuzzContext.Of(seed, index, drawn);

            BigDecimal.Round(drawn.Value, target, Modes[random.Next(Modes.Length)])
                .Scale.Should().Be(drawn.Scale, "{0} Round to the wider scale {1} leaves the value alone", context, target);
        }
    }
}
