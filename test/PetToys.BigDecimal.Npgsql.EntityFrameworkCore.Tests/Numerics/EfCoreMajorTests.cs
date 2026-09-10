using System.Globalization;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Which EF Core major this run is actually testing.
/// </summary>
/// <remarks>
/// The package is compiled once against EF Core 9 and supported on 9 and 10, and NuGet resolves a
/// range to its floor - so a suite that named no version would test 9 twice and never 10, which is
/// the half the single-compilation shape has to be checked against. The integration leg runs twice
/// and moves <c>EfCoreProviderVersion</c> for the second run; these cases are what stop a restore
/// that quietly unified upward, or an override that did nothing, from looking like a pass.
/// </remarks>
public sealed class EfCoreMajorTests
{
    [Fact]
    public void TheLoadedProvider_IsTheOneTheRestoreWasAskedFor()
    {
        var requested = Requested();
        var loaded = typeof(NpgsqlDbContextOptionsBuilder).Assembly.GetName().Version!;

        loaded.Major.Should().Be(
            requested,
            "the range asked for resolves to its floor, so the loaded provider's major is the "
            + "floor's major unless something unified the reference upward behind the restore");
    }

    [Fact]
    public void TheLibrary_IsCompiledAgainstEfCoreNineWhateverIsLoaded()
    {
        var library = typeof(NpgsqlBigDecimalDbContextOptionsBuilderExtensions).Assembly;

        var relational = library.GetReferencedAssemblies()
            .Single(a => a.Name == "Microsoft.EntityFrameworkCore.Relational");

        // The measured shape, asserted rather than remembered: one compilation against EF Core 9
        // serves every target framework, and the mirror - a net10.0 asset against EF Core 10 -
        // cannot be consumed by an application on EF Core 9 at all.
        relational.Version!.Major.Should().Be(9);
    }

    [Fact]
    public void TheMappingIsChosen_UnderWhicheverMajorIsLoaded()
    {
        using var context = Harness.NumericModel.Offline();

        var mapping = context.Model.FindEntityType(typeof(Harness.NumericItem))!
            .FindProperty(nameof(Harness.NumericItem.Amount))!
            .GetRelationalTypeMapping();

        mapping.Should().BeOfType<BigDecimalTypeMapping>(
            "an assembly compiled against EF Core 9 has to keep working under a later major, and "
            + "this is the leg that finds out");
        mapping.Converter.Should().BeNull();
    }

    /// <summary>The major of the floor of the range this assembly's restore was asked for.</summary>
    /// <returns>The major version number.</returns>
    private static int Requested()
    {
        var range = typeof(EfCoreMajorTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "EfCoreProviderVersion")
            .Value!;

        // The value is a NuGet range such as [9.0.4,11.0.0); the floor is what NuGet resolves to.
        var floor = range.TrimStart('[', '(').Split(',')[0];

        return int.Parse(floor.Split('.')[0], CultureInfo.InvariantCulture);
    }
}
