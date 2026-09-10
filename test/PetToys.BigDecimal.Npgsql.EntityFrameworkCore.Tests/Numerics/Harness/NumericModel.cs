using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Xunit;

namespace PetToys.BigDecimal.Numerics.Harness;

// EF1002 and EF1003 are about SQL built from strings, and they are right in general. Every
// statement here is built from this suite's own constants - a table name it chose - and none
// of it is reachable from a caller. EF1003 exists only from EF Core 10, which is why it
// appears on one of the two provider runs and not the other; that is the second run doing
// its job.
#pragma warning disable EF1002, EF1003

/// <summary>
/// Builds the contexts the suite uses, in the four configurations that are worth telling apart.
/// </summary>
/// <remarks>
/// The model-level configurations take a connection string and never open it, which is what lets
/// the cases about the plugin, the comparer, the facets and the generated schema run with no
/// container at all.
/// </remarks>
public static class NumericModel
{
    /// <summary>A connection string that is never opened, for the model-level cases.</summary>
    public const string Unreachable =
        "Host=localhost;Port=1;Username=none;Password=none;Database=none";

    /// <summary>A context over a connection string, with the registration applied.</summary>
    /// <param name="table">The table to map to.</param>
    /// <returns>The context.</returns>
    public static NumericContext Offline(string table = "Items") =>
        new(
            new DbContextOptionsBuilder<NumericContext>()
                .UseNpgsql(Unreachable, o => o.UseBigDecimal())
                .ReplaceService<IModelCacheKeyFactory, NumericModelCacheKeyFactory>()
                .Options,
            table);

    /// <summary>A context over a connection string with no registration at all.</summary>
    /// <remarks>
    /// The case a consumer builds when they reference the package and forget the call. What Entity
    /// Framework says about it is pinned rather than assumed, because the message is EF's.
    /// </remarks>
    /// <param name="table">The table to map to.</param>
    /// <returns>The context.</returns>
    public static NumericContext Unregistered(string table = "Items") =>
        new(
            new DbContextOptionsBuilder<NumericContext>()
                .UseNpgsql(Unreachable)
                .ReplaceService<IModelCacheKeyFactory, NumericModelCacheKeyFactory>()
                .Options,
            table);

    /// <summary>A context over a live data source, with both registrations in place.</summary>
    /// <param name="source">
    /// The data source, which the caller built with the adapter's own <c>UseBigDecimal</c>.
    /// </param>
    /// <param name="table">The table to map to.</param>
    /// <returns>The context.</returns>
    public static NumericContext Over(NpgsqlDataSource source, string table) =>
        new(
            new DbContextOptionsBuilder<NumericContext>()
                .UseNpgsql(source, o => o.UseBigDecimal())
                .ReplaceService<IModelCacheKeyFactory, NumericModelCacheKeyFactory>()
                .Options,
            table);

    /// <summary>
    /// A context over a data source that never had the adapter's mapping applied, which is the
    /// half-configured combination a reader who skimmed the README will build.
    /// </summary>
    /// <param name="connectionString">The server to connect to.</param>
    /// <param name="table">The table to map to.</param>
    /// <returns>The context and the bare data source it owns.</returns>
    public static (NumericContext Context, NpgsqlDataSource Source) WithoutTheAdapter(
        string connectionString,
        string table)
    {
        var source = new NpgsqlDataSourceBuilder(connectionString).Build();

        return (
            new NumericContext(
                new DbContextOptionsBuilder<NumericContext>()
                    .UseNpgsql(source, o => o.UseBigDecimal())
                    .ReplaceService<IModelCacheKeyFactory, NumericModelCacheKeyFactory>()
                    .Options,
                table),
            source);
    }

    /// <summary>Creates this context's table from the model it declares.</summary>
    /// <param name="context">The context.</param>
    /// <returns>Nothing.</returns>
    public static async Task CreateTableAsync(this NumericContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.Database.ExecuteSqlRawAsync(
            "drop table if exists \"" + context.Table + "\"",
            TestContext.Current.CancellationToken);

        await context.Database.ExecuteSqlRawAsync(
            context.Database.GenerateCreateScript(),
            TestContext.Current.CancellationToken);
    }
}
