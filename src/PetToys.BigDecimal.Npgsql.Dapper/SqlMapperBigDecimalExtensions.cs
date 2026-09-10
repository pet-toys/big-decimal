using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // See BigDecimalTypeHandler: the namespace is Dapper's on purpose.

namespace Dapper;

/// <summary>
/// Registers the <see cref="BigDecimal"/> handler with Dapper, and reads PostgreSQL
/// <c>numeric</c> columns exactly.
/// </summary>
/// <remarks>
/// <para>
/// Two calls make this package work, and they are not interchangeable.
/// <c>UseBigDecimal()</c> here registers the type handler with Dapper, which is what carries a
/// parameter of the type and the scalar read shape. <c>UseBigDecimal()</c> on an
/// <c>NpgsqlDataSourceBuilder</c>, from <c>PetToys.BigDecimal.Npgsql</c>, is what puts the
/// <c>numeric</c> mapping on the data source; this package does not install it, because the data
/// source is the caller's. Neither failure is silent: without the data source registration a read
/// fails naming the column's data type and a write is refused naming the CLR type.
/// </para>
/// <para>
/// Registering the handler does not change what a <c>numeric</c> column produces for code that
/// never asks for <see cref="BigDecimal"/>. An untyped read is still a <see cref="decimal"/>,
/// <c>GetFieldType</c> still reports <see cref="decimal"/>, and a dynamic query still answers
/// <see cref="decimal"/>.
/// </para>
/// </remarks>
public static class SqlMapperBigDecimalExtensions
{
    /// <summary>Registers the <see cref="BigDecimalTypeHandler"/> with Dapper.</summary>
    /// <remarks>
    /// Dapper's handler registry is static and process-wide - it takes no scope argument - so this
    /// is a process-level act, and calling it from application start-up is the shape that matches
    /// what it does. It is idempotent. It also replaces any handler already registered for
    /// <see cref="BigDecimal"/>: Dapper accepts a second registration silently and publishes no way
    /// to read the registry back, so a caller with a handler of their own registers theirs after
    /// this one, or not at all.
    /// </remarks>
    public static void UseBigDecimal() => SqlMapper.AddTypeHandler(new BigDecimalTypeHandler());

    /// <summary>
    /// Wraps a reader so that its PostgreSQL <c>numeric</c> columns read as
    /// <see cref="BigDecimal"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the primitive the exact read is built from, and the way to reach any shape
    /// <c>QueryBigDecimal</c> does not cover - an unbuffered read, <c>QueryMultiple</c>,
    /// multi-mapping - by handing the result to Dapper's own <c>Parse&lt;T&gt;</c>:
    /// </para>
    /// <code>
    /// using var reader = connection.ExecuteReader(sql, parameters).AsBigDecimalReader();
    /// foreach (var row in reader.Parse&lt;Order&gt;())
    /// {
    /// }
    /// </code>
    /// <para>
    /// Every <c>numeric</c> column of the query is affected, including ones a caller was not
    /// thinking about. That is not a hole: a member still typed <see cref="decimal"/> converts
    /// where the value fits and raises <see cref="OverflowException"/> where it does not, so
    /// nothing narrows quietly, and a result type mixing both works.
    /// </para>
    /// <para>
    /// Disposing the returned reader disposes the one passed in, which is what closes the command
    /// Dapper opened.
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

        // Every layer, not one: Dapper wraps once today, and a second wrapper would otherwise be
        // refused as a foreign provider. The reference check keeps a wrapper that returns itself
        // from spinning here.
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
    /// The results are buffered, so the reader is closed before this returns. See
    /// <see cref="AsBigDecimalReader"/> for what the widening covers and for the unbuffered form.
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
            // Ownership passes to the wrapper only once it exists; until then the reader, the
            // command and a connection Dapper opened are this method's to release.
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
    /// The command is opened and the rows are fetched asynchronously; materialising them is
    /// Dapper's own synchronous step over an already-buffered reader.
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
            // See the synchronous overload: the wrapper owns the reader only once it exists.
            await BigDecimalDataReader.CloseOwnerAsync(opened).ConfigureAwait(false);

            throw;
        }

        await using (reader.ConfigureAwait(false))
        {
            return reader.Parse<T>().ToList();
        }
    }
}
