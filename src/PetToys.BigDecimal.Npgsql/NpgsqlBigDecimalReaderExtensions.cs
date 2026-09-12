using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // The driver's namespace, where every Npgsql plugin puts its registration.

namespace Npgsql;

/// <summary>
/// Reads a <c>numeric</c> column as <see cref="BigDecimal"/>, naming the column when the read
/// fails.
/// </summary>
/// <remarks>
/// PostgreSQL <c>numeric</c> holds 131072 integer digits against this type's 78, so reading is the
/// direction that can fail, and the converter never learns which column it was reading. These
/// accessors add the name to both of the codec's failures, <see cref="OverflowException"/> and
/// <see cref="FormatException"/>, keeping the type and carrying the original as
/// <see cref="Exception.InnerException"/>. <c>GetFieldValue&lt;BigDecimal&gt;</c>, Dapper and
/// Entity Framework Core get the same mapping and the same exception without the name.
/// </remarks>
public static class NpgsqlBigDecimalReaderExtensions
{
    /// <summary>Reads a <c>numeric</c> column as <see cref="BigDecimal"/>.</summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's magnitude.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.</exception>
    /// <exception cref="InvalidCastException">The column is NULL.</exception>
    public static BigDecimal GetBigDecimal(this NpgsqlDataReader reader, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(reader);

        try
        {
            return reader.GetFieldValue<BigDecimal>(ordinal);
        }
        catch (Exception exception) when (IsCodecFailure(exception))
        {
            throw Named(reader, ordinal, exception);
        }
    }

    /// <summary>Reads a <c>numeric</c> column as <see cref="BigDecimal"/>, by name.</summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="name">The column's name.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's magnitude.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.</exception>
    /// <exception cref="InvalidCastException">The column is NULL.</exception>
    public static BigDecimal GetBigDecimal(this NpgsqlDataReader reader, string name)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return reader.GetBigDecimal(reader.GetOrdinal(name));
    }

    /// <summary>Reads a <c>numeric</c> column that admits NULL.</summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The value, or <see langword="null"/> when the column is NULL.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's magnitude.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.</exception>
    public static BigDecimal? GetNullableBigDecimal(this NpgsqlDataReader reader, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(reader);

        try
        {
            return reader.GetFieldValue<BigDecimal?>(ordinal);
        }
        catch (Exception exception) when (IsCodecFailure(exception))
        {
            throw Named(reader, ordinal, exception);
        }
    }

    /// <summary>Reads a <c>numeric</c> column that admits NULL, by name.</summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="name">The column's name.</param>
    /// <returns>The value, or <see langword="null"/> when the column is NULL.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's magnitude.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.</exception>
    public static BigDecimal? GetNullableBigDecimal(this NpgsqlDataReader reader, string name)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return reader.GetNullableBigDecimal(reader.GetOrdinal(name));
    }

    /// <summary>Reads a <c>numeric</c> column as <see cref="BigDecimal"/>, asynchronously.</summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's magnitude.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.</exception>
    /// <exception cref="InvalidCastException">The column is NULL.</exception>
    public static async Task<BigDecimal> GetBigDecimalAsync(
        this NpgsqlDataReader reader,
        int ordinal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        try
        {
            return await reader.GetFieldValueAsync<BigDecimal>(ordinal, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsCodecFailure(exception))
        {
            throw Named(reader, ordinal, exception);
        }
    }

    /// <summary>
    /// Reads a <c>numeric</c> column as <see cref="BigDecimal"/>, by name, asynchronously.
    /// </summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="name">The column's name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's magnitude.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.</exception>
    /// <exception cref="InvalidCastException">The column is NULL.</exception>
    public static Task<BigDecimal> GetBigDecimalAsync(
        this NpgsqlDataReader reader,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return reader.GetBigDecimalAsync(reader.GetOrdinal(name), cancellationToken);
    }

    /// <summary>
    /// Reads a <c>numeric</c> column that admits NULL, asynchronously.
    /// </summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The value, or <see langword="null"/> when the column is NULL.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's magnitude.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.</exception>
    public static async Task<BigDecimal?> GetNullableBigDecimalAsync(
        this NpgsqlDataReader reader,
        int ordinal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        try
        {
            return await reader.GetFieldValueAsync<BigDecimal?>(ordinal, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsCodecFailure(exception))
        {
            throw Named(reader, ordinal, exception);
        }
    }

    /// <summary>
    /// Reads a <c>numeric</c> column that admits NULL, by name, asynchronously.
    /// </summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="name">The column's name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The value, or <see langword="null"/> when the column is NULL.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's magnitude.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.</exception>
    public static Task<BigDecimal?> GetNullableBigDecimalAsync(
        this NpgsqlDataReader reader,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return reader.GetNullableBigDecimalAsync(reader.GetOrdinal(name), cancellationToken);
    }

    // The FormatException half is for a future PostgreSQL that adds a sign code, as 14 did.
    private static bool IsCodecFailure(Exception exception) =>
        exception is OverflowException or FormatException;

    // The ordinal travels beside the name: a query can project two columns of one name.
    private static Exception Named(NpgsqlDataReader reader, int ordinal, Exception failure)
    {
        var column = string.Create(
            CultureInfo.InvariantCulture,
            $"Column '{reader.GetName(ordinal)}' at ordinal {ordinal}");

        return failure is OverflowException
            ? new OverflowException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{column} holds a numeric whose integer part is larger than BigDecimal can represent: {failure.Message}"),
                failure)
            : new FormatException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{column} holds a numeric payload that is not the documented binary layout: {failure.Message}"),
                failure);
    }
}
