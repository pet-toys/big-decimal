using System;
using System.Data.Common;
using System.Globalization;
using ClickHouse.Driver.ADO.Readers;
using PetToys.BigDecimal.Numerics;
using DriverDecimal = ClickHouse.Driver.Numerics.ClickHouseDecimal;

#pragma warning disable IDE0130 // The driver's namespace, the one a ClickHouse caller already imports.

namespace ClickHouse.Driver;

/// <summary>
/// Reads a mapped decimal column as a <see cref="BigDecimal"/>, and says what went wrong when it
/// cannot.
/// </summary>
/// <remarks>
/// <para>
/// Reading cannot fail - every ClickHouse decimal fits - so these accessors exist to explain a
/// shape: <c>GetFieldValue&lt;BigDecimal&gt;</c> cannot work, because the driver casts its own
/// value to the requested type before consulting the hook, and a column read where the mapping
/// was not installed fails the same way. Both raise an <see cref="InvalidCastException"/> that
/// names neither the column nor the registration; these do.
/// </para>
/// <para>
/// There is no asynchronous form: the value is materialised by
/// <see cref="DbDataReader.ReadAsync(System.Threading.CancellationToken)"/> and <c>GetValue</c>
/// is synchronous after it.
/// </para>
/// </remarks>
public static class ClickHouseDataReaderBigDecimalExtensions
{
    /// <summary>Reads a mapped decimal column.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidCastException">
    /// The column is null, or holds something other than a mapped decimal. The message names the
    /// column, what arrived, and the registration call when that is what is missing.
    /// </exception>
    public static BigDecimal GetBigDecimal(this ClickHouseDataReader reader, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return reader.GetValue(ordinal) switch
        {
            BigDecimal value => value,
            DBNull => throw Null(reader, ordinal),
            var other => throw Unmapped(reader, ordinal, other),
        };
    }

    /// <summary>Reads a mapped decimal column by name.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="name">The column's name.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidCastException">
    /// The column is null, or holds something other than a mapped decimal. The message names the
    /// column, what arrived, and the registration call when that is what is missing.
    /// </exception>
    public static BigDecimal GetBigDecimal(this ClickHouseDataReader reader, string name)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return reader.GetBigDecimal(reader.GetOrdinal(name));
    }

    /// <summary>Reads a mapped decimal column that may be null.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <returns>The value, or <see langword="null"/> when the column is null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidCastException">
    /// The column holds something other than a mapped decimal. The message names the column, what
    /// arrived, and the registration call when that is what is missing.
    /// </exception>
    public static BigDecimal? GetNullableBigDecimal(this ClickHouseDataReader reader, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return reader.GetValue(ordinal) switch
        {
            BigDecimal value => value,
            DBNull => null,
            var other => throw Unmapped(reader, ordinal, other),
        };
    }

    /// <summary>Reads a mapped decimal column by name, which may be null.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="name">The column's name.</param>
    /// <returns>The value, or <see langword="null"/> when the column is null.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidCastException">
    /// The column holds something other than a mapped decimal. The message names the column, what
    /// arrived, and the registration call when that is what is missing.
    /// </exception>
    public static BigDecimal? GetNullableBigDecimal(this ClickHouseDataReader reader, string name)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return reader.GetNullableBigDecimal(reader.GetOrdinal(name));
    }

    private static InvalidCastException Null(ClickHouseDataReader reader, int ordinal) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"{Column(reader, ordinal)} is null. Use GetNullableBigDecimal, which answers null instead of throwing."));

    // The driver's own decimal arriving here means the query ran without the mapping; anything
    // else means the column is not a decimal at all.
    private static InvalidCastException Unmapped(ClickHouseDataReader reader, int ordinal, object? value)
    {
        var arrived = value?.GetType().FullName ?? "null";
        var column = Column(reader, ordinal);

        // Not inlined: `is T or T[] ?` parses as a nullable array pattern.
        var unmapped = value is DriverDecimal or DriverDecimal[];

        var message = unmapped
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{column} arrived as {arrived}, which means the mapping was not installed for this read. Pass ClickHouseBigDecimal.CreateQueryOptions() to the query, or build the connection from settings with UseBigDecimal().")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{column} arrived as {arrived}, which is not a ClickHouse decimal column.");

        return new InvalidCastException(message);
    }

    private static string Column(ClickHouseDataReader reader, int ordinal) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Column '{reader.GetName(ordinal)}' at ordinal {ordinal} of declared type {reader.GetDataTypeName(ordinal)}");
}
