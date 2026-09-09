using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // The namespace is the one the extended type lives in, on purpose.

namespace ClickHouse.Driver;

/// <summary>
/// Bulk inserts rows whose decimal cells are <see cref="BigDecimal"/> values.
/// </summary>
/// <remarks>
/// <para>
/// The driver's own insert path has no hook of either scope: a <see cref="BigDecimal"/> handed
/// straight to it falls through to <see cref="IConvertible"/> and <see cref="decimal"/>, silently
/// where the value fits and with an overflow that names nothing where it does not. That is why this
/// entry point exists.
/// </para>
/// <para>
/// Every decimal cell is converted at its column's scale and width before the driver serialises
/// anything, so the driver never rescales and never overflows on this path: its own rescale
/// truncates toward zero where this type rounds half to even, and its overflow names the value
/// rather than the column. Cells of any other type are passed through untouched and serialised by
/// the driver exactly as they would be without this package.
/// </para>
/// </remarks>
public static class ClickHouseClientBigDecimalExtensions
{
    /// <summary>Inserts rows, converting the <see cref="BigDecimal"/> cells in them.</summary>
    /// <param name="client">The client.</param>
    /// <param name="table">The destination table, qualified with a database when it needs to be.</param>
    /// <param name="columns">The columns the rows carry, in order.</param>
    /// <param name="rows">The rows. Enumerated once, and not modified.</param>
    /// <param name="options">
    /// The insert options. When these carry <see cref="InsertOptions.ColumnTypes"/> for every
    /// column, the types are taken from there; otherwise they are read from the server.
    /// </param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>How many rows were written.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="client"/>, <paramref name="table"/>, <paramref name="columns"/> or
    /// <paramref name="rows"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OverflowException">
    /// A value at its column's scale is outside that column's width or declared precision. The
    /// message names the column.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// A value is NaN or an infinity, which no ClickHouse decimal represents.
    /// </exception>
    /// <remarks>
    /// The column types are resolved rather than assumed, because a width that disagrees with the
    /// column does not fail: it stores a different number. Declaring them through
    /// <see cref="InsertOptions.ColumnTypes"/> saves the round trip when the caller already knows
    /// them.
    /// </remarks>
    public static async Task<long> InsertBigDecimalAsync(
        this IClickHouseClient client,
        string table,
        IReadOnlyList<string> columns,
        IEnumerable<object?[]> rows,
        InsertOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        options ??= new InsertOptions();

        var types = await ResolveColumnTypesAsync(client, table, columns, options, cancellationToken)
            .ConfigureAwait(false);

        return await client
            .InsertBinaryAsync(table, columns, Convert(rows, columns, types), options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Replaces the mapped cells of each row, leaving the rest as they are.</summary>
    /// <param name="rows">The caller's rows.</param>
    /// <param name="columns">The column names, for the failure messages.</param>
    /// <param name="types">The decimal type of each column, where it has one.</param>
    /// <returns>The rows, converted lazily.</returns>
    /// <remarks>
    /// A row is copied only when something in it changes, so a batch with no decimal cells
    /// allocates nothing and the caller's arrays are never written to.
    /// </remarks>
    private static IEnumerable<object?[]> Convert(
        IEnumerable<object?[]> rows,
        IReadOnlyList<string> columns,
        ClickHouseColumnType?[] types)
    {
        foreach (var row in rows)
        {
            // A null row cannot be inserted by any path, so it is named here rather than left to
            // surface as a dereference somewhere inside the driver's serialiser.
            ArgumentNullException.ThrowIfNull(row, nameof(rows));

            object?[]? converted = null;
            var count = Math.Min(row.Length, types.Length);
            for (var i = 0; i < count; i++)
            {
                if (types[i] is not { } type)
                {
                    continue;
                }

                var replacement = row[i] switch
                {
                    BigDecimal one => BigDecimalColumnCodec.ToColumn(one, type, columns[i]),
                    BigDecimal[] many => Array.ConvertAll(many, one => BigDecimalColumnCodec.ToColumn(one, type, columns[i])),
                    _ => (object?)null,
                };

                if (replacement is null)
                {
                    continue;
                }

                converted ??= (object?[])row.Clone();
                converted[i] = replacement;
            }

            yield return converted ?? row;
        }
    }

    /// <summary>Finds the decimal type of each column, where it has one.</summary>
    /// <param name="client">The client.</param>
    /// <param name="table">The destination table.</param>
    /// <param name="columns">The columns the rows carry.</param>
    /// <param name="options">The insert options, which may declare the types.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// One entry per column: its decimal type, or the element's for an array of them, and
    /// <see langword="null"/> for a column this package does not map.
    /// </returns>
    private static async Task<ClickHouseColumnType?[]> ResolveColumnTypesAsync(
        IClickHouseClient client,
        string table,
        IReadOnlyList<string> columns,
        InsertOptions options,
        CancellationToken cancellationToken)
    {
        var declared = options.ColumnTypes;
        if (declared is null || !CoversEveryColumn(declared, columns))
        {
            declared = await DescribeAsync(client, table, options, cancellationToken).ConfigureAwait(false);
        }

        var types = new ClickHouseColumnType?[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            if (declared.TryGetValue(columns[i], out var type) && TryParseElement(type, out var parsed))
            {
                types[i] = parsed;
            }
        }

        return types;
    }

    /// <summary>Answers whether the caller declared a type for every column.</summary>
    /// <param name="declared">What the caller declared.</param>
    /// <param name="columns">The columns the rows carry.</param>
    /// <returns><see langword="true"/> when nothing has to be read from the server.</returns>
    private static bool CoversEveryColumn(
        IReadOnlyDictionary<string, string> declared,
        IReadOnlyList<string> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (!declared.ContainsKey(columns[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Asks the server for the declared type of every column of a table.</summary>
    /// <param name="client">The client.</param>
    /// <param name="table">The table.</param>
    /// <param name="options">The insert's own options, which this query runs under.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Column name to declared type.</returns>
    /// <remarks>
    /// <para>
    /// The lookup runs under the options the insert itself will run under, which is why
    /// <see cref="InsertOptions"/> is passed through rather than a fresh
    /// <see cref="QueryOptions"/>: it derives from one and carries the database, the roles, the
    /// session and the custom settings. Describing a table in a different database than the insert
    /// writes to does not fail, it answers about a different table of the same name, and the widths
    /// it reports would then store different numbers.
    /// </para>
    /// <para>
    /// The table name is spliced into the statement, as it is by the driver's own insert path,
    /// because a table name is not a parameter in ClickHouse. It is the caller's identifier and it
    /// reaches the server as given.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyDictionary<string, string>> DescribeAsync(
        IClickHouseClient client,
        string table,
        InsertOptions options,
        CancellationToken cancellationToken)
    {
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);

        var statement = string.Create(CultureInfo.InvariantCulture, $"DESCRIBE TABLE {table}");
        var reader = await client
            .ExecuteReaderAsync(statement, null, options, cancellationToken)
            .ConfigureAwait(false);

        await using var disposal = reader.ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            declared[reader.GetString(0)] = reader.GetString(1);
        }

        return declared;
    }

    /// <summary>Reads a column type, looking inside an array when it is one.</summary>
    /// <param name="declared">The declared type.</param>
    /// <param name="type">The decimal type, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the column carries decimals.</returns>
    private static bool TryParseElement(string? declared, out ClickHouseColumnType type)
    {
        const string Array = "Array(";

        if (declared is not null
            && declared.StartsWith(Array, StringComparison.Ordinal)
            && declared.EndsWith(')'))
        {
            declared = declared[Array.Length..^1];
        }

        return ClickHouseColumnType.TryParse(declared, out type);
    }
}
