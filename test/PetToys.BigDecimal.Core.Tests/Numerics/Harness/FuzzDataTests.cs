using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Tests of the harness itself. Everything the randomised suites promise about reproducibility
/// rests on the derivation below, so it is pinned here rather than assumed.
/// </summary>
public sealed class FuzzDataTests
{
    private const string Identity = "PetToys.BigDecimal.Numerics.SomeTests.SomeMethod";

    [Fact]
    public void Derive_IsStableAcrossCalls()
    {
        var first = Enumerable.Range(0, 64).Select(i => FuzzSeeds.Derive(1234, Identity, i)).ToArray();
        var second = Enumerable.Range(0, 64).Select(i => FuzzSeeds.Derive(1234, Identity, i)).ToArray();

        second.Should().Equal(first);
    }

    [Fact]
    public void Derive_IsStableAcrossProcesses()
    {
        // Pinned literals, not a recomputation: a derivation that silently changed would agree
        // with itself and disagree with every seed anybody has already written down.
        FuzzSeeds.Derive(FuzzSettings.DefaultBaseSeed, Identity, 0).Should().Be(395_843_045);
        FuzzSeeds.Derive(FuzzSettings.DefaultBaseSeed, Identity, 1).Should().Be(1_265_669_752);
    }

    [Fact]
    public void Derive_IsNonNegative()
    {
        for (var index = 0; index < 256; index++)
        {
            FuzzSeeds.Derive(int.MinValue, Identity, index).Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void Derive_SeparatesIndices()
    {
        var seeds = Enumerable.Range(0, 256).Select(i => FuzzSeeds.Derive(0, Identity, i)).ToArray();

        seeds.Distinct().Should().HaveCount(seeds.Length);
    }

    [Fact]
    public void Derive_SeparatesTests()
    {
        var here = Enumerable.Range(0, 64).Select(i => FuzzSeeds.Derive(0, Identity, i));
        var there = Enumerable.Range(0, 64).Select(i => FuzzSeeds.Derive(0, Identity + "Other", i));

        here.Intersect(there).Should().BeEmpty();
    }

    [Fact]
    public void Derive_SeparatesBaseSeeds()
    {
        var withDefault = Enumerable.Range(0, 64).Select(i => FuzzSeeds.Derive(FuzzSettings.DefaultBaseSeed, Identity, i));
        var withOther = Enumerable.Range(0, 64).Select(i => FuzzSeeds.Derive(FuzzSettings.DefaultBaseSeed + 1, Identity, i));

        withDefault.Intersect(withOther).Should().BeEmpty();
    }

    [Fact]
    public void Batches_SpreadTheRequestedCases()
    {
        var batches = FuzzDataAttribute.Batches(Identity, 0, 205).ToArray();

        batches.Should().HaveCount(FuzzSettings.BatchCount);
        batches.Sum(b => b.Cases).Should().Be(205);
        batches.Should().AllSatisfy(b => b.Cases.Should().BeGreaterThan(0));
    }

    [Fact]
    public void Batches_NeverOutnumberTheCases()
    {
        var batches = FuzzDataAttribute.Batches(Identity, 0, 3).ToArray();

        batches.Should().HaveCount(3);
        batches.Sum(b => b.Cases).Should().Be(3);
    }

    [Fact]
    public void Rows_CarryTheFuzzCategory()
    {
        var rows = FuzzDataAttribute.Rows(Identity, 0, FuzzSettings.Cases);

        rows.Should().AllSatisfy(row =>
            row.Traits.Should().ContainKey(TestCategories.TraitName)
                .WhoseValue.Should().Contain(TestCategories.Fuzz));
    }

    [Fact]
    public void Rows_NameTheSeedThatProducedThem()
    {
        var rows = FuzzDataAttribute.Rows(Identity, 0, FuzzSettings.Cases).ToArray();

        for (var index = 0; index < rows.Length; index++)
        {
            var seed = FuzzSeeds.Derive(0, Identity, index);

            rows[index].TestDisplayName.Should().Contain(seed.ToString(CultureInfo.InvariantCulture));
            rows[index].GetData()[0].Should().Be(seed);
        }
    }

    [Fact]
    public void TheCategory_IsVisibleBeforeTheTestRuns()
    {
        // Rows are produced when a randomised test runs, not when it is discovered, so a trait
        // carried only by the rows is invisible to a runner filtering by trait — and the workflow's
        // off switch would silently do nothing.
        var traits = new FuzzDataAttribute().GetTraits();

        traits.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, string>(TestCategories.TraitName, TestCategories.Fuzz));
    }

    [Fact]
    public void Settings_HaveUsableDefaults()
    {
        FuzzSettings.Cases.Should().BeGreaterThan(0);
        FuzzSettings.BatchCount.Should().BeGreaterThan(0);
    }
}
