using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // The driver's namespace, the one a ClickHouse caller already imports.

namespace ClickHouse.Driver;

/// <summary>
/// Bulk inserts rows whose decimal cells are <see cref="BigDecimal"/> values.
/// </summary>
/// <remarks>
/// The driver's own insert path has no hook, and a <see cref="BigDecimal"/> handed straight to it
/// fails as a serialisation error naming neither the column nor the value. Here every decimal
/// cell is converted at its column's scale and width before the driver serialises anything, so
/// the driver never rescales - it truncates where this type rounds half to even - and never
/// overflows. Cells of any other type pass through untouched.
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
    /// column stores a different number rather than failing.
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

    // A row is copied only when something in it changes; the caller's arrays are never written to.
    private static IEnumerable<object?[]> Convert(
        IEnumerable<object?[]> rows,
        IReadOnlyList<string> columns,
        ColumnMapping[] mappings)
    {
        foreach (var row in rows)
        {
            ArgumentNullException.ThrowIfNull(row, nameof(rows));

            object?[]? converted = null;
            var count = Math.Min(row.Length, mappings.Length);
            for (var i = 0; i < count; i++)
            {
                if (mappings[i].Type is not { } type)
                {
                    // A column this package does not recognise, reachable through type
                    // constructors this parser skips; the driver would fail without naming it.
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

    private static NotSupportedException Unrecognised(string column, string? declared) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"Column '{column}' is declared as {declared ?? "an unknown type"}, which this package does not read as a decimal column, so a BigDecimal cannot be written to it exactly. It is refused rather than passed to the driver, which would narrow it through System.Decimal. Declare the column's own decimal type through InsertOptions.ColumnTypes if its payload is one."));

    // One entry per column: its decimal type where this package reads one, and how it is
    // declared either way, so a refusal can name it.
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

    private readonly record struct ColumnMapping(ClickHouseColumnType? Type, string? Declared);

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

    // Runs under the insert's own options, so the lookup describes the table in the insert's
    // database. The table name is spliced in, as the driver does: it is not a parameter.
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

    // Looks inside an array type. No trimming: the driver's schema resolver refuses a declared
    // type with space around it before a row is serialised.
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
