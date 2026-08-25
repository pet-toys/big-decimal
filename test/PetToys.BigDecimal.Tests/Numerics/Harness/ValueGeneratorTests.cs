using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Tests of the generator. A weight that silently stopped producing a class would narrow every
/// randomised suite at once and nothing else would notice, so the coverage is asserted here.
/// </summary>
public sealed class ValueGeneratorTests
{
    private static ValueGenerator Generator(int seed = 12_345) => new(new Random(seed));

    [Fact]
    public void ADefaultRun_VisitsEveryValueClass()
    {
        var generator = Generator();
        var seen = new HashSet<ValueClass>();

        for (var index = 0; index < FuzzSettings.Cases; index++)
        {
            seen.Add(generator.Next().Class);
        }

        seen.Should().BeEquivalentTo(Enum.GetValues<ValueClass>());
    }

    [Fact]
    public void ADefaultRun_VisitsEveryScaleRelationship()
    {
        var generator = Generator();
        var seen = new HashSet<ScaleRelationship>();

        for (var index = 0; index < FuzzSettings.Cases; index++)
        {
            var (left, right) = generator.NextPair();
            seen.Add(Classify(left.Scale, right.Scale));
        }

        seen.Should().BeEquivalentTo(Enum.GetValues<ScaleRelationship>());
    }

    [Theory]
    [InlineData(ValueClass.OneWord, 1)]
    [InlineData(ValueClass.TwoWords, 2)]
    [InlineData(ValueClass.ThreeWords, 3)]
    [InlineData(ValueClass.FourWords, 4)]
    public void AWordWidthClass_ProducesThatManyWords(ValueClass valueClass, int words)
    {
        var generator = Generator();

        for (var index = 0; index < 200; index++)
        {
            var bits = BigInteger.Abs(generator.Next(valueClass).Unscaled).GetBitLength();

            bits.Should().BeGreaterThan((words - 1) * 64L);
            bits.Should().BeLessThanOrEqualTo(words * 64L);
        }
    }

    [Fact]
    public void ADefaultRun_VisitsEveryWordBoundary()
    {
        var generator = Generator();
        var wanted = new HashSet<BigInteger>();

        for (var shift = 64; shift <= 192; shift += 64)
        {
            wanted.Add(BigInteger.One << shift);
            wanted.Add((BigInteger.One << shift) - BigInteger.One);
        }

        for (var index = 0; index < FuzzSettings.Cases && wanted.Count > 0; index++)
        {
            wanted.Remove(BigInteger.Abs(generator.Next().Unscaled));
        }

        wanted.Should().BeEmpty();
    }

    [Fact]
    public void TheExtremes_AreProducedExactly()
    {
        var generator = Generator();
        var magnitudes = Enumerable.Range(0, 64)
            .Select(_ => generator.Next(ValueClass.Extremum))
            .ToArray();

        magnitudes.Should().AllSatisfy(v => BigInteger.Abs(v.Unscaled).Should().Be(ValueGenerator.MaxMagnitude));
        magnitudes.Should().Contain(v => v.Unscaled.Sign < 0);
        magnitudes.Should().Contain(v => v.Unscaled.Sign > 0);
    }

    [Fact]
    public void EveryDraw_DescribesTheValueItProduced()
    {
        var generator = Generator();

        for (var index = 0; index < FuzzSettings.Cases; index++)
        {
            var drawn = generator.Next();

            drawn.Value.GetMantissa().Should().Be(drawn.Unscaled, "the draw claims {0}", drawn);
            drawn.Value.Scale.Should().Be(drawn.Scale, "the draw claims {0}", drawn);
        }
    }

    [Fact]
    public void EveryDraw_StaysInsideTheDomain()
    {
        var generator = Generator();

        for (var index = 0; index < FuzzSettings.Cases; index++)
        {
            var drawn = generator.Next();

            BigInteger.Abs(drawn.Unscaled).Should().BeLessThanOrEqualTo(ValueGenerator.MaxMagnitude);
            drawn.Scale.Should().BeInRange(0, BigDecimal.MaxScale);
        }
    }

    [Fact]
    public void Zero_IsNeverSigned()
    {
        var generator = Generator();

        for (var index = 0; index < 200; index++)
        {
            var drawn = generator.Next(ValueClass.Zero);

            drawn.Unscaled.Should().Be(BigInteger.Zero);
            drawn.Value.IsNegative.Should().BeFalse();
            drawn.Value.Sign.Should().Be(0);
        }
    }

    [Theory]
    [InlineData(ScaleRelationship.Equal)]
    [InlineData(ScaleRelationship.OffByOne)]
    [InlineData(ScaleRelationship.FarApart)]
    public void ARequestedRelationship_IsTheOneProduced(ScaleRelationship relationship)
    {
        var generator = Generator();

        for (var index = 0; index < 200; index++)
        {
            var (left, right) = generator.NextPair(relationship);

            Classify(left.Scale, right.Scale).Should().Be(relationship);
        }
    }

    [Fact]
    public void ASeed_ReproducesTheSameDraws()
    {
        var firstGenerator = Generator(99);
        var secondGenerator = Generator(99);

        // Two sequences from two generators seeded alike, not two copies of one draw: the claim
        // under test is that a seed reproduces the whole run, not just its first case.
        var first = Enumerable.Range(0, 100).Select(_ => firstGenerator.Next()).ToArray();
        var second = Enumerable.Range(0, 100).Select(_ => secondGenerator.Next()).ToArray();

        second.Should().Equal(first);
        first.Should().Contain(value => value.Class != first[0].Class, "the sequence must actually advance");
    }

    private static ScaleRelationship Classify(int left, int right) => Math.Abs(left - right) switch
    {
        0 => ScaleRelationship.Equal,
        1 => ScaleRelationship.OffByOne,
        _ => ScaleRelationship.FarApart,
    };
}
