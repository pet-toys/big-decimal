using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // Dapper's namespace, where a caller registering a handler already is.

namespace Dapper;

/// <summary>
/// Registers the <see cref="BigDecimal"/> handler with Dapper, and reads PostgreSQL
/// <c>numeric</c> columns exactly.
/// </summary>
/// <remarks>
/// Two calls make this package work: <c>UseBigDecimal()</c> here registers the type handler with
/// Dapper, and <c>UseBigDecimal()</c> on the <c>NpgsqlDataSourceBuilder</c> puts the
/// <c>numeric</c> mapping on the data source. Neither failure is silent. Registering the handler
/// does not change what a <c>numeric</c> column produces for code that never asks for
/// <see cref="BigDecimal"/>: an untyped read is still a <see cref="decimal"/>.
/// </remarks>
public static class SqlMapperBigDecimalExtensions
{
    /// <summary>Registers the <see cref="BigDecimalTypeHandler"/> with Dapper.</summary>
    /// <remarks>
    /// Dapper's registry is static and process-wide, so this is a start-up call. It is idempotent,
    /// and it silently replaces any handler already registered for <see cref="BigDecimal"/>.
    /// </remarks>
    public static void UseBigDecimal() => SqlMapper.AddTypeHandler(new BigDecimalTypeHandler());

    /// <summary>
    /// Wraps a reader so that its PostgreSQL <c>numeric</c> columns read as
    /// <see cref="BigDecimal"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The primitive behind <c>QueryBigDecimal</c>, and the way to reach any shape it does not
    /// cover - an unbuffered read, <c>QueryMultiple</c>, multi-mapping - through Dapper's own
    /// <c>Parse&lt;T&gt;</c>:
    /// </para>
    /// <code>
    /// using var reader = connection.ExecuteReader(sql, parameters).AsBigDecimalReader();
    /// foreach (var row in reader.Parse&lt;Order&gt;())
    /// {
    /// }
    /// </code>
    /// <para>
    /// Every <c>numeric</c> column of the query is affected; a member still typed
    /// <see cref="decimal"/> converts where the value fits and raises
    /// <see cref="OverflowException"/> where it does not. Disposing the returned reader disposes
    /// the one passed in, which closes the command Dapper opened.
    /// </para>
    /// </remarks>
    /// <param name="reader">A reader over a PostgreSQL connection, Dapper's own wrapper
    /// included.</param>
    /// <returns>A reader answering <c>numeric</c> columns as <see cref="BigDecimal"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="reader"/> is not, and does not wrap, an
    /// <see cref="NpgsqlDataReader"/>. The message names what it is.</exception>
    public static DbDataReader AsBigDecimalReader(this IDataReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        // Every layer, not one; the reference check keeps a wrapper returning itself from spinning.
        var underlying = reader;

        while (underlying is IWrappedDataReader wrapped
            && !ReferenceEquals(wrapped.Reader, underlying))
        {
            underlying = wrapped.Reader;
        }

        if (underlying is not NpgsqlDataReader npgsql)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "This package reads PostgreSQL numeric columns through Npgsql, and was handed "
                    + "a {0}. Use it over a connection from Npgsql.",
                    underlying.GetType()),
                nameof(reader));
        }

        return new BigDecimalDataReader(npgsql, reader);
    }

    /// <summary>
    /// Runs a query in which every PostgreSQL <c>numeric</c> column reads as
    /// <see cref="BigDecimal"/>.
    /// </summary>
    /// <remarks>
    /// The results are buffered, so the reader is closed before this returns;
    /// <see cref="AsBigDecimalReader"/> is the unbuffered form.
    /// </remarks>
    /// <typeparam name="T">The type each row is materialised as.</typeparam>
    /// <param name="connection">The connection, open or closed.</param>
    /// <param name="sql">The query.</param>
    /// <param name="parameters">The parameters, as Dapper takes them.</param>
    /// <param name="transaction">The transaction, if there is one.</param>
    /// <param name="commandTimeout">The command timeout in seconds.</param>
    /// <param name="commandType">The command type.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is
    /// <see langword="null"/>.</exception>
    public static IEnumerable<T> QueryBigDecimal<T>(
        this NpgsqlConnection connection,
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        int? commandTimeout = null,
        CommandType? commandType = null) =>
        connection.QueryBigDecimal<T>(
            new CommandDefinition(sql, parameters, transaction, commandTimeout, commandType));

    /// <summary>
    /// Runs a query in which every PostgreSQL <c>numeric</c> column reads as
    /// <see cref="BigDecimal"/>.
    /// </summary>
    /// <typeparam name="T">The type each row is materialised as.</typeparam>
    /// <param name="connection">The connection, open or closed.</param>
    /// <param name="command">The command, as Dapper takes it.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is
    /// <see langword="null"/>.</exception>
    public static IEnumerable<T> QueryBigDecimal<T>(
        this NpgsqlConnection connection,
        CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var opened = connection.ExecuteReader(command);
        DbDataReader reader;

        try
        {
            reader = opened.AsBigDecimalReader();
        }
        catch
        {
            // Ownership passes to the wrapper only once it exists.
            opened.Dispose();

            throw;
        }

        using (reader)
        {
            return reader.Parse<T>().ToList();
        }
    }

    /// <summary>
    /// Runs a query in which every PostgreSQL <c>numeric</c> column reads as
    /// <see cref="BigDecimal"/>, asynchronously.
    /// </summary>
    /// <remarks>
    /// The rows are fetched asynchronously; materialising them is Dapper's own synchronous step.
    /// </remarks>
    /// <typeparam name="T">The type each row is materialised as.</typeparam>
    /// <param name="connection">The connection, open or closed.</param>
    /// <param name="sql">The query.</param>
    /// <param name="parameters">The parameters, as Dapper takes them.</param>
    /// <param name="transaction">The transaction, if there is one.</param>
    /// <param name="commandTimeout">The command timeout in seconds.</param>
    /// <param name="commandType">The command type.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is
    /// <see langword="null"/>.</exception>
    public static Task<IEnumerable<T>> QueryBigDecimalAsync<T>(
        this NpgsqlConnection connection,
        string sql,
        object? parameters = null,
        IDbTransaction? transaction = null,
        int? commandTimeout = null,
        CommandType? commandType = null) =>
        connection.QueryBigDecimalAsync<T>(
            new CommandDefinition(sql, parameters, transaction, commandTimeout, commandType));

    /// <summary>
    /// Runs a query in which every PostgreSQL <c>numeric</c> column reads as
    /// <see cref="BigDecimal"/>, asynchronously.
    /// </summary>
    /// <remarks>
    /// The cancellation token travels on the <see cref="CommandDefinition"/>, which is where
    /// Dapper takes it.
    /// </remarks>
    /// <typeparam name="T">The type each row is materialised as.</typeparam>
    /// <param name="connection">The connection, open or closed.</param>
    /// <param name="command">The command, as Dapper takes it.</param>
    /// <returns>The rows.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> is
    /// <see langword="null"/>.</exception>
    public static async Task<IEnumerable<T>> QueryBigDecimalAsync<T>(
        this NpgsqlConnection connection,
        CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var opened = await connection.ExecuteReaderAsync(command).ConfigureAwait(false);
        DbDataReader reader;

        try
        {
            reader = opened.AsBigDecimalReader();
        }
        catch
        {
            // Ownership passes to the wrapper only once it exists.
            await BigDecimalDataReader.CloseOwnerAsync(opened).ConfigureAwait(false);

            throw;
        }

        await using (reader.ConfigureAwait(false))
        {
            return reader.Parse<T>().ToList();
        }
    }
}
