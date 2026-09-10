using System;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Npgsql.PostgresTypes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// A reader that answers PostgreSQL <c>numeric</c> columns as <see cref="BigDecimal"/>, wrapping
/// the driver's own reader.
/// </summary>
/// <remarks>
/// <para>
/// Dapper's type handler is handed whatever <c>GetValue</c> already produced, so over a
/// <c>numeric</c> column it receives a <see cref="decimal"/> and never sees a value wider than
/// one. This reader is how the exact value reaches Dapper instead: it calls
/// <c>GetFieldValue&lt;BigDecimal&gt;</c> on the columns it recognises, and Dapper's own
/// materialiser runs over it unchanged. Nothing global is registered to make that happen, so a
/// <c>numeric</c> column read anywhere else in the application is still a <see cref="decimal"/>.
/// </para>
/// <para>
/// Recognition comes from <see cref="NpgsqlDataReader.GetPostgresType"/> rather than from a data
/// type name. Measured against PostgreSQL 18: a bare column, a <c>numeric(12,4)</c> column, an
/// aggregate over one and a domain over one all report the base type <c>numeric</c>, while
/// <c>GetDataTypeName</c> renders the second as <c>numeric(12, 4)</c> - a name no equality test
/// against <c>numeric</c> matches, and a column missed that way is read as <see cref="decimal"/>
/// with nothing said.
/// </para>
/// <para>
/// The type is internal because Dapper's materialiser is its only consumer.
/// <c>AsBigDecimalReader</c> hands it out as a <see cref="DbDataReader"/>, which is the whole of
/// the contract a caller needs.
/// </para>
/// </remarks>
internal sealed class BigDecimalDataReader : DbDataReader
{
    private readonly NpgsqlDataReader inner;

    private readonly IDataReader owner;

    private bool[] widened;

    private bool[] arrays;

    private bool disposed;

    /// <summary>Wraps a reader positioned before its first row.</summary>
    /// <param name="inner">The driver's reader, which the recognised columns are read from.</param>
    /// <param name="owner">
    /// What the caller was handed and what this reader disposes: Dapper's <c>ExecuteReader</c>
    /// returns a wrapper that also disposes the command, so disposing
    /// <paramref name="inner"/> instead would leak it.
    /// </param>
    internal BigDecimalDataReader(NpgsqlDataReader inner, IDataReader owner)
    {
        this.inner = inner;
        this.owner = owner;
        this.widened = [];
        this.arrays = [];
        this.Describe();
    }

    public override object this[int ordinal] => this.GetValue(ordinal);

    public override object this[string name] => this.GetValue(this.GetOrdinal(name));

    public override int Depth => this.inner.Depth;

    public override int FieldCount => this.inner.FieldCount;

    public override bool HasRows => this.inner.HasRows;

    public override bool IsClosed => this.inner.IsClosed;

    public override int RecordsAffected => this.inner.RecordsAffected;

    public override object GetValue(int ordinal)
    {
        if (!this.widened[ordinal])
        {
            return this.inner.GetValue(ordinal);
        }

        if (this.inner.IsDBNull(ordinal))
        {
            return DBNull.Value;
        }

        return this.arrays[ordinal]
            ? this.inner.GetFieldValue<BigDecimal[]>(ordinal)
            : this.inner.GetFieldValue<BigDecimal>(ordinal);
    }

    /// <summary>Answers the CLR type a column reads as.</summary>
    /// <remarks>
    /// Declaring the type is load bearing rather than cosmetic. Dapper builds a materialiser from
    /// the field types and caches it by that shape; one built for <see cref="decimal"/> and then
    /// handed a <see cref="BigDecimal"/> fails with a cast, which is what a member still typed
    /// <see cref="decimal"/> in a result would hit.
    /// </remarks>
    [return: DynamicallyAccessedMembers(
        DynamicallyAccessedMemberTypes.PublicFields
        | DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal)
    {
        if (!this.widened[ordinal])
        {
            return ((DbDataReader)this.inner).GetFieldType(ordinal);
        }

        return this.arrays[ordinal] ? typeof(BigDecimal[]) : typeof(BigDecimal);
    }

    public override int GetValues(object[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var count = Math.Min(values.Length, this.FieldCount);

        for (var i = 0; i < count; i++)
        {
            values[i] = this.GetValue(i);
        }

        return count;
    }

    /// <summary>Reads a column as the type asked for.</summary>
    /// <remarks>
    /// The driver knows how to produce every type it can produce, this one included, so every
    /// named type goes to it. <see cref="object"/> is the exception: there the driver answers the
    /// column's default, which for a widened column is the <see cref="decimal"/> this reader
    /// exists not to hand back. A NULL still goes to the driver, so its own refusal is what a
    /// caller sees.
    /// </remarks>
    /// <typeparam name="T">The type to read the column as.</typeparam>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>The value.</returns>
    public override T GetFieldValue<T>(int ordinal) =>
        this.AnswersObjectItself<T>(ordinal) && !this.inner.IsDBNull(ordinal)
            ? (T)this.GetValue(ordinal)
            : this.inner.GetFieldValue<T>(ordinal);

    /// <summary>Reads a column as the type asked for, asynchronously.</summary>
    /// <typeparam name="T">The type to read the column as.</typeparam>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The value.</returns>
    public override async Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
    {
        if (this.AnswersObjectItself<T>(ordinal)
            && !await this.inner.IsDBNullAsync(ordinal, cancellationToken).ConfigureAwait(false))
        {
            return (T)this.GetValue(ordinal);
        }

        return await this.inner.GetFieldValueAsync<T>(ordinal, cancellationToken).ConfigureAwait(false);
    }

    public override bool GetBoolean(int ordinal) => this.inner.GetBoolean(ordinal);

    public override byte GetByte(int ordinal) => this.inner.GetByte(ordinal);

    public override long GetBytes(
        int ordinal,
        long dataOffset,
        byte[]? buffer,
        int bufferOffset,
        int length) =>
        this.inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);

    public override char GetChar(int ordinal) => this.inner.GetChar(ordinal);

    public override long GetChars(
        int ordinal,
        long dataOffset,
        char[]? buffer,
        int bufferOffset,
        int length) =>
        this.inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);

    public override string GetDataTypeName(int ordinal) => this.inner.GetDataTypeName(ordinal);

    public override DateTime GetDateTime(int ordinal) => this.inner.GetDateTime(ordinal);

    public override decimal GetDecimal(int ordinal) => this.inner.GetDecimal(ordinal);

    public override double GetDouble(int ordinal) => this.inner.GetDouble(ordinal);

    public override float GetFloat(int ordinal) => this.inner.GetFloat(ordinal);

    public override Guid GetGuid(int ordinal) => this.inner.GetGuid(ordinal);

    public override short GetInt16(int ordinal) => this.inner.GetInt16(ordinal);

    public override int GetInt32(int ordinal) => this.inner.GetInt32(ordinal);

    public override long GetInt64(int ordinal) => this.inner.GetInt64(ordinal);

    public override string GetName(int ordinal) => this.inner.GetName(ordinal);

    public override int GetOrdinal(string name) => this.inner.GetOrdinal(name);

    public override string GetString(int ordinal) => this.inner.GetString(ordinal);

    public override DataTable? GetSchemaTable() => this.inner.GetSchemaTable();

    public override bool IsDBNull(int ordinal) => this.inner.IsDBNull(ordinal);

    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) =>
        this.inner.IsDBNullAsync(ordinal, cancellationToken);

    public override bool Read() => this.inner.Read();

    public override Task<bool> ReadAsync(CancellationToken cancellationToken) =>
        this.inner.ReadAsync(cancellationToken);

    public override bool NextResult()
    {
        var more = this.inner.NextResult();
        this.Describe();

        return more;
    }

    public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        var more = await this.inner.NextResultAsync(cancellationToken).ConfigureAwait(false);
        this.Describe();

        return more;
    }

    /// <summary>Enumerates the rows as records.</summary>
    /// <remarks>
    /// Over this reader rather than over the one underneath it: an enumerator built from the
    /// driver's would answer <see cref="decimal"/> for the columns every other member of this
    /// class answers as <see cref="BigDecimal"/>.
    /// </remarks>
    /// <returns>An enumerator over the remaining rows.</returns>
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    /// <summary>Closes the reader this one was built over.</summary>
    public override void Close()
    {
        if (!this.disposed)
        {
            this.owner.Close();
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        // The base implementation calls Close, which closes the owner, so it runs before the owner
        // is disposed rather than after it.
        base.Dispose(disposing);

        if (disposing && !this.disposed)
        {
            this.disposed = true;
            this.owner.Dispose();
        }
    }

    /// <summary>
    /// Disposes the reader this one was built over, asynchronously where it can be.
    /// </summary>
    /// <remarks>
    /// The inherited implementation calls the synchronous dispose, and closing a reader with rows
    /// left drains them from the socket, so an <c>await using</c> over this one would block on
    /// network work. The flag is what keeps the inherited path, which still runs afterwards, from
    /// closing and disposing an owner that is already gone.
    /// </remarks>
    /// <returns>Nothing.</returns>
    public override async ValueTask DisposeAsync()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            await CloseOwnerAsync(this.owner).ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Disposes a reader by its best available route.</summary>
    /// <param name="reader">The reader to dispose.</param>
    /// <returns>Nothing.</returns>
    internal static async ValueTask CloseOwnerAsync(IDataReader reader)
    {
        if (reader is IAsyncDisposable asynchronous)
        {
            await asynchronous.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            reader.Dispose();
        }
    }

    /// <summary>
    /// Works out which columns of the current result set are this package's to answer.
    /// </summary>
    private void Describe()
    {
        var count = this.inner.FieldCount;
        this.widened = new bool[count];
        this.arrays = new bool[count];

        for (var i = 0; i < count; i++)
        {
            var type = this.inner.GetPostgresType(i);

            if (type is PostgresArrayType array)
            {
                this.arrays[i] = this.widened[i] = IsNumeric(array.Element);
            }
            else
            {
                this.widened[i] = IsNumeric(type);
            }
        }
    }

    /// <summary>
    /// Answers whether an untyped read of this column is this reader's to answer.
    /// </summary>
    /// <typeparam name="T">The type the caller asked for.</typeparam>
    /// <param name="ordinal">The zero-based column ordinal.</param>
    /// <returns>
    /// <see langword="true"/> when the caller asked for <see cref="object"/> over a column this
    /// reader widens.
    /// </returns>
    private bool AnswersObjectItself<T>(int ordinal) =>
        typeof(T) == typeof(object) && this.widened[ordinal];

    /// <summary>Answers whether a type is PostgreSQL's <c>numeric</c>.</summary>
    /// <remarks>
    /// A faceted column and a domain both report the base type they are built on, so this covers
    /// <c>numeric(12,4)</c> and a domain over one without a rule of its own.
    /// </remarks>
    private static bool IsNumeric(PostgresType type) =>
        type is PostgresBaseType
        && string.Equals(type.Name, "numeric", StringComparison.Ordinal)
        && string.Equals(type.Namespace, "pg_catalog", StringComparison.Ordinal);
}
