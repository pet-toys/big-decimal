using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Supplies a randomised test with its cases: a fixed number of rows, each carrying a seed derived
/// from the test's own identity and a count of cases to draw from it.
/// </summary>
/// <remarks>
/// The seeds are derived rather than drawn, so the same test runs the same cases on every machine,
/// operating system and target framework, and a failure that reports its seed is reproducible
/// somewhere else. Each row also carries the <see cref="TestCategories.Fuzz"/> trait: applying it
/// here rather than on the test class is what keeps a randomised test from being written without
/// it, and keeps it off the deterministic tests that must never be filtered out with them.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FuzzDataAttribute : DataAttribute, ITraitAttribute
{
    /// <summary>
    /// Publishes the category at discovery time as well as on every row.
    /// </summary>
    /// <remarks>
    /// The rows carry it too, but rows are produced when the test runs, not when it is discovered,
    /// so a runner filtering by trait would never see them. Implementing
    /// <see cref="ITraitAttribute"/> is what makes the category an actual filter and keeps the
    /// off switch in the workflow honest.
    /// </remarks>
    /// <returns>The randomised category.</returns>
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new(TestCategories.TraitName, TestCategories.Fuzz)];

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(
        MethodInfo testMethod,
        DisposalTracker disposalTracker)
    {
        ArgumentNullException.ThrowIfNull(testMethod);

        var identity = testMethod.DeclaringType is null
            ? testMethod.Name
            : $"{testMethod.DeclaringType.FullName}.{testMethod.Name}";

        return new(Rows(identity, FuzzSettings.BaseSeed, FuzzSettings.Cases));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The rows depend on the environment, so they are produced when the test runs rather than
    /// when it is discovered.
    /// </remarks>
    public override bool SupportsDiscoveryEnumeration() => false;

    /// <summary>Builds the rows for one test. Exposed so that the derivation itself is testable.</summary>
    /// <param name="identity">The test's stable identity.</param>
    /// <param name="baseSeed">The run's base constant.</param>
    /// <param name="cases">The total number of cases to spread across the rows.</param>
    /// <returns>One row per batch, each carrying a seed and a case count.</returns>
    public static IReadOnlyCollection<ITheoryDataRow> Rows(string identity, int baseSeed, int cases)
    {
        var rows = new List<ITheoryDataRow>();

        foreach (var (seed, count) in Batches(identity, baseSeed, cases))
        {
            var row = new TheoryDataRow<int, int>(seed, count)
                .WithTrait(TestCategories.TraitName, TestCategories.Fuzz)
                .WithTestDisplayName(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{identity} [seed {seed}, {count} case(s)]"));

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Splits a case count into batches, spreading the remainder over the first ones.</summary>
    /// <param name="identity">The test's stable identity.</param>
    /// <param name="baseSeed">The run's base constant.</param>
    /// <param name="cases">The total number of cases.</param>
    /// <returns>A seed and a case count per batch.</returns>
    public static IEnumerable<(int Seed, int Cases)> Batches(string identity, int baseSeed, int cases)
    {
        var batches = Math.Min(FuzzSettings.BatchCount, Math.Max(cases, 1));
        var quotient = Math.DivRem(Math.Max(cases, 1), batches, out var remainder);

        for (var index = 0; index < batches; index++)
        {
            yield return (FuzzSeeds.Derive(baseSeed, identity, index), quotient + (index < remainder ? 1 : 0));
        }
    }
}
