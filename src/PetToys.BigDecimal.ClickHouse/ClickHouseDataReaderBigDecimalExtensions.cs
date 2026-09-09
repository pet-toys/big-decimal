using System;
using System.Data.Common;
using System.Globalization;
using PetToys.BigDecimal.Numerics;
using DriverDecimal = ClickHouse.Driver.Numerics.ClickHouseDecimal;

#pragma warning disable IDE0130 // The namespace is the one the extended type lives in, on purpose.

namespace ClickHouse.Driver.ADO.Readers;

/// <summary>
/// Reads a mapped decimal column as a <see cref="BigDecimal"/>, and says what went wrong when it
/// cannot.
/// </summary>
/// <remarks>
/// <para>
/// These accessors are not here to catch an exception, because reading cannot fail: every
/// ClickHouse decimal fits, at every width and every precision the server allows. They are here to
/// explain a shape a caller will otherwise not understand.
/// </para>
/// <para>
/// <c>GetFieldValue&lt;BigDecimal&gt;</c> cannot work and never will. The driver casts its own value
/// to the requested type before consulting the hook that could have changed it, so the cast fails
/// first; the same happens at any call site reading a column on a connection or query where the
/// mapping was not installed. Both produce an <see cref="InvalidCastException"/> about the driver's
/// decimal type that names neither the column nor the registration. These accessors do.
/// </para>
/// <para>
/// There is no asynchronous form, and that is not an omission. The value is materialised by
/// <see cref="DbDataReader.ReadAsync(System.Threading.CancellationToken)"/>, <c>GetValue</c> is
/// synchronous after it, and <c>GetFieldValueAsync&lt;T&gt;</c> is the overload that cannot work.
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

    /// <summary>Explains a column that is null where a value was asked for.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidCastException Null(ClickHouseDataReader reader, int ordinal) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"{Column(reader, ordinal)} is null. Use GetNullableBigDecimal, which answers null instead of throwing."));

    /// <summary>Explains a column that did not arrive as a mapped value.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <param name="value">What arrived instead.</param>
    /// <returns>The exception to throw.</returns>
    /// <remarks>
    /// The driver's own decimal arriving here means one thing and it is worth saying plainly: the
    /// query ran without the mapping. Anything else means the column is not a decimal at all.
    /// </remarks>
    private static InvalidCastException Unmapped(ClickHouseDataReader reader, int ordinal, object? value)
    {
        var arrived = value?.GetType().FullName ?? "null";
        var column = Column(reader, ordinal);

        // Extracted rather than inlined into the conditional: `is T or T[]` followed by `?` is
        // read as a nullable array pattern, which compiles and means something else.
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

    /// <summary>Names a column the way a message should read.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column's ordinal.</param>
    /// <returns>The phrase a message opens with.</returns>
    private static string Column(ClickHouseDataReader reader, int ordinal) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Column '{reader.GetName(ordinal)}' at ordinal {ordinal} of declared type {reader.GetDataTypeName(ordinal)}");
}
