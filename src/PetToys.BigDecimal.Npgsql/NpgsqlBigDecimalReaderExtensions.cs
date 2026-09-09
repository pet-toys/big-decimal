using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // See NpgsqlBigDecimalExtensions: the namespace is the driver's on purpose.

namespace Npgsql;

/// <summary>
/// Reads a <c>numeric</c> column as <see cref="BigDecimal"/>, naming the column when the read
/// fails.
/// </summary>
/// <remarks>
/// <para>
/// These exist for one reason. PostgreSQL <c>numeric</c> holds 131072 integer digits against this
/// type's 78, so reading is the direction that can fail, and the converter cannot say which column
/// failed: it is handed a buffer and a length and never learns that a column was involved.
/// Measured against PostgreSQL 18, an exception raised in a converter reaches the caller unwrapped
/// and unannotated through <c>GetFieldValue</c> and <c>GetFieldValueAsync</c> alike, so the name has
/// to be added by the first layer that knows one, which is the reader.
/// </para>
/// <para>
/// Both of the codec's documented failures are named, not just the expected one. An integer part
/// beyond the magnitude raises <see cref="OverflowException"/>, and a payload that is not the
/// documented layout raises <see cref="FormatException"/>; the second is the rarer and the more
/// confusing of the two, so leaving it unnamed would leave the harder case to the situation these
/// methods exist to improve.
/// </para>
/// <para>
/// The exception type does not change in either case. The core's contract is that an integer part
/// beyond the magnitude throws <see cref="OverflowException"/>, a caller catching that has to keep
/// working, and the original is carried as <see cref="Exception.InnerException"/>.
/// </para>
/// <para>
/// This is a convenience with a boundary, and the boundary is stated rather than hidden: a caller
/// who uses <c>GetFieldValue&lt;BigDecimal&gt;</c> directly, or who reaches the column through
/// Dapper or Entity Framework Core, gets the same mapping and the same exception without the column
/// name.
/// </para>
/// <para>
/// Nothing here handles NULL. Reading a NULL into the value type already fails with an
/// <see cref="InvalidCastException"/> that names the column, which is the message a caller should
/// see; the nullable accessors are for a column that admits one.
/// </para>
/// </remarks>
public static class NpgsqlBigDecimalReaderExtensions
{
    /// <summary>Reads a <c>numeric</c> column as <see cref="BigDecimal"/>.</summary>
    /// <param name="reader">The reader, positioned on a row.</param>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's
    /// magnitude. The message names the column and its ordinal, and the exception the converter
    /// threw is the inner exception.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.
    /// The message names the column and its ordinal, and the exception the converter threw is the
    /// inner exception.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's
    /// magnitude. The message names the column and its ordinal, and the exception the converter
    /// threw is the inner exception.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.
    /// The message names the column and its ordinal, and the exception the converter threw is the
    /// inner exception.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's
    /// magnitude. The message names the column and its ordinal, and the exception the converter
    /// threw is the inner exception.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.
    /// The message names the column and its ordinal, and the exception the converter threw is the
    /// inner exception.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's
    /// magnitude. The message names the column and its ordinal, and the exception the converter
    /// threw is the inner exception.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.
    /// The message names the column and its ordinal, and the exception the converter threw is the
    /// inner exception.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's
    /// magnitude. The message names the column and its ordinal, and the exception the converter
    /// threw is the inner exception.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.
    /// The message names the column and its ordinal, and the exception the converter threw is the
    /// inner exception.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's
    /// magnitude. The message names the column and its ordinal, and the exception the converter
    /// threw is the inner exception.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.
    /// The message names the column and its ordinal, and the exception the converter threw is the
    /// inner exception.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's
    /// magnitude. The message names the column and its ordinal, and the exception the converter
    /// threw is the inner exception.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.
    /// The message names the column and its ordinal, and the exception the converter threw is the
    /// inner exception.</exception>
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
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's
    /// magnitude. The message names the column and its ordinal, and the exception the converter
    /// threw is the inner exception.</exception>
    /// <exception cref="FormatException">The column's payload is not a well-formed <c>numeric</c>.
    /// The message names the column and its ordinal, and the exception the converter threw is the
    /// inner exception.</exception>
    public static Task<BigDecimal?> GetNullableBigDecimalAsync(
        this NpgsqlDataReader reader,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return reader.GetNullableBigDecimalAsync(reader.GetOrdinal(name), cancellationToken);
    }

    /// <summary>
    /// Answers whether an exception is one of the two the codec documents, which are the two a
    /// column name belongs on.
    /// </summary>
    /// <remarks>
    /// The <see cref="FormatException"/> half has no test against a live server, and cannot have
    /// one: a well-formed server never emits a payload this codec calls malformed, which is why
    /// naming the column matters there. The case it is for is a future PostgreSQL that adds a sign
    /// code, the way 14 added the two infinities, and a caller who would otherwise be told only
    /// that some column somewhere was not the layout we expect.
    /// </remarks>
    /// <param name="exception">What the read threw.</param>
    /// <returns><see langword="true"/> for a failure this class annotates.</returns>
    private static bool IsCodecFailure(Exception exception) =>
        exception is OverflowException or FormatException;

    /// <summary>
    /// Builds the exception that names the column, carrying the original underneath it and keeping
    /// its type.
    /// </summary>
    /// <remarks>
    /// The ordinal travels beside the name, because a query can project two columns of the same
    /// name and the name alone would then point at either.
    /// </remarks>
    /// <param name="reader">The reader the column belongs to.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <param name="failure">What the read threw.</param>
    /// <returns>The exception to throw, of the same type as <paramref name="failure"/>.</returns>
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
