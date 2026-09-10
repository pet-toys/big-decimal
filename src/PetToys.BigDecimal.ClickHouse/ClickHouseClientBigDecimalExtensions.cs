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
/// The driver's own insert path has no hook of either scope, and a <see cref="BigDecimal"/> handed
/// straight to it never reaches a column: the serialiser reaches for <see cref="IConvertible"/>,
/// which this type does not implement, and fails as a bulk-copy serialisation error wrapping an
/// <see cref="InvalidCastException"/> that names neither the column nor the value. That is why this
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
    /// A value is NaN or an infinity, which no ClickHouse decimal represents, or a
    /// <see cref="BigDecimal"/> is aimed at a column this package does not read as a decimal one.
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

        var mappings = await ResolveColumnTypesAsync(client, table, columns, options, cancellationToken)
            .ConfigureAwait(false);

        return await client
            .InsertBinaryAsync(table, columns, Convert(rows, columns, mappings), options, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Replaces the mapped cells of each row, leaving the rest as they are.</summary>
    /// <param name="rows">The caller's rows.</param>
    /// <param name="columns">The column names, for the failure messages.</param>
    /// <param name="mappings">What was resolved about each column.</param>
    /// <returns>The rows, converted lazily.</returns>
    /// <remarks>
    /// A row is copied only when something in it changes, so a batch with no decimal cells
    /// allocates nothing and the caller's arrays are never written to.
    /// </remarks>
    private static IEnumerable<object?[]> Convert(
        IEnumerable<object?[]> rows,
        IReadOnlyList<string> columns,
        ColumnMapping[] mappings)
    {
        foreach (var row in rows)
        {
            // A null row cannot be inserted by any path, so it is named here rather than left to
            // surface as a dereference somewhere inside the driver's serialiser.
            ArgumentNullException.ThrowIfNull(row, nameof(rows));

            object?[]? converted = null;
            var count = Math.Min(row.Length, mappings.Length);
            for (var i = 0; i < count; i++)
            {
                if (mappings[i].Type is not { } type)
                {
                    // A value of ours in a column this package does not recognise, which is
                    // reachable without a mistake through type constructors this parser skips.
                    // The driver would fail on it - it reaches for IConvertible, which this type
                    // does not implement - but name neither the column nor the value.
                    if (row[i] is BigDecimal or BigDecimal[])
                    {
                        throw Unrecognised(columns[i], mappings[i].Declared);
                    }

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

    /// <summary>Refuses a value aimed at a column this package could not read.</summary>
    /// <param name="column">The column's name.</param>
    /// <param name="declared">How the column is declared, when that is known.</param>
    /// <returns>The exception to throw.</returns>
    private static NotSupportedException Unrecognised(string column, string? declared) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"Column '{column}' is declared as {declared ?? "an unknown type"}, which this package does not read as a decimal column, so a BigDecimal cannot be written to it exactly. It is refused rather than passed to the driver, which would narrow it through System.Decimal. Declare the column's own decimal type through InsertOptions.ColumnTypes if its payload is one."));

    /// <summary>Finds the decimal type of each column, where it has one.</summary>
    /// <param name="client">The client.</param>
    /// <param name="table">The destination table.</param>
    /// <param name="columns">The columns the rows carry.</param>
    /// <param name="options">The insert options, which may declare the types.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>
    /// One entry per column: its decimal type where this package reads one, and how it is declared
    /// either way, since a refusal has to be able to name it.
    /// </returns>
    private static async Task<ColumnMapping[]> ResolveColumnTypesAsync(
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

        var mappings = new ColumnMapping[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            declared.TryGetValue(columns[i], out var type);
            mappings[i] = TryParseElement(type, out var parsed)
                ? new ColumnMapping(parsed, type)
                : new ColumnMapping(null, type);
        }

        return mappings;
    }

    /// <summary>What is known about one column of the destination.</summary>
    /// <param name="Type">Its decimal type, or the element's, where this package reads one.</param>
    /// <param name="Declared">How the column is declared, for a message.</param>
    private readonly record struct ColumnMapping(ClickHouseColumnType? Type, string? Declared);

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
    /// <see cref="InsertOptions"/> is passed through rather than a fresh
    /// <see cref="QueryOptions"/> so the lookup runs under the insert's own database, roles and
    /// session: describing a table in a different database does not fail, it answers about a
    /// different table of the same name. The table name is spliced into the statement, as the
    /// driver's own insert path does, because a table name is not a parameter in ClickHouse.
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
    /// <remarks>
    /// No trimming, and that is measured: a type read from the server never carries surrounding
    /// space, and one declared through <see cref="InsertOptions.ColumnTypes"/> with space around
    /// it is refused by the driver's schema resolver before a row is serialised.
    /// </remarks>
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
