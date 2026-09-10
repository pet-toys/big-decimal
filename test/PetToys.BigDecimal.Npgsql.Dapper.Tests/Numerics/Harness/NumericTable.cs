using System;
using System.Globalization;
using System.Threading.Tasks;
using Npgsql;
using Xunit;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// A table carrying every shape of <c>numeric</c> column this package has to recognise, created
/// per test and dropped with it.
/// </summary>
/// <remarks>
/// <para>
/// The columns are the forms the server names differently - bare, faceted, an array, and a domain
/// over a faceted one - because a recognition rule written over the reader's data type name string
/// matches only the first of them.
/// </para>
/// <para>
/// Each table carries a name of its own, and that is load bearing rather than tidy: Dapper caches
/// its materialiser by the reader's column shape rather than by the query text, so two cases over
/// one shape can answer from whichever ran first.
/// </para>
/// </remarks>
public sealed class NumericTable : IAsyncDisposable
{
    /// <summary>The domain every table here declares a column of.</summary>
    public const string Domain = "dapper_money";

    private readonly NpgsqlDataSource source;

    private NumericTable(NpgsqlDataSource source, string name)
    {
        this.source = source;
        this.Name = name;
    }

    /// <summary>The table's name, unique to the test that created it.</summary>
    public string Name { get; }

    /// <summary>Creates the table over a data source.</summary>
    /// <param name="source">The data source, which carries the adapter's registration.</param>
    /// <returns>The table.</returns>
    public static async Task<NumericTable> CreateAsync(NpgsqlDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var name = string.Create(CultureInfo.InvariantCulture, $"dp_{Guid.NewGuid():N}");
        var table = new NumericTable(source, name);

        // CREATE DOMAIN has no IF NOT EXISTS, and these tests share one database.
        await table.ExecuteAsync(
            $"DO $$ BEGIN CREATE DOMAIN {Domain} AS numeric(12,4); "
            + "EXCEPTION WHEN duplicate_object THEN NULL; END $$;");

        await table.ExecuteAsync(
            $"CREATE TABLE {name} (id int, v numeric, f numeric(12,4), a numeric[], d {Domain})");

        return table;
    }

    /// <summary>Runs a statement against the table's data source.</summary>
    /// <param name="sql">The statement.</param>
    /// <returns>Nothing.</returns>
    public async Task ExecuteAsync(string sql)
    {
        await using var command = this.source.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Reads one value as the server renders it.</summary>
    /// <param name="sql">A query returning one text column.</param>
    /// <returns>The text, or <see langword="null"/> when the column is NULL.</returns>
    public async Task<string?> RenderAsync(string sql)
    {
        await using var command = this.source.CreateCommand(sql);

        var scalar = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        // Not `as string`: a query that forgot its ::text would then read as null, and a case
        // asserting null would pass over a value that is there.
        return scalar switch
        {
            null or DBNull => null,
            string text => text,
            _ => throw new InvalidOperationException(
                $"The query answered a {scalar.GetType()} rather than text: {sql}"),
        };
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await this.ExecuteAsync($"DROP TABLE IF EXISTS {this.Name}");
        }
        catch (NpgsqlException)
        {
            // A refused statement leaves the connection's transaction aborted, and that failure is
            // the one naming the defect. The table goes away with the container.
        }
    }
}
