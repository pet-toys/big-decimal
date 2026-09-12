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
/// Dapper's type handler is handed whatever <c>GetValue</c> produced, which over <c>numeric</c>
/// is a <see cref="decimal"/>. This reader calls <c>GetFieldValue&lt;BigDecimal&gt;</c> on the
/// columns it recognises and Dapper's materialiser runs over it unchanged; nothing global is
/// registered. Recognition comes from <see cref="NpgsqlDataReader.GetPostgresType"/> rather than
/// a data type name, which renders <c>numeric(12,4)</c> as a name no equality test matches.
/// </remarks>
internal sealed class BigDecimalDataReader : DbDataReader
{
    private readonly NpgsqlDataReader inner;

    private readonly IDataReader owner;

    private bool[] widened;

    private bool[] arrays;

    private bool disposed;

    // owner is what this reader disposes: Dapper's ExecuteReader returns a wrapper that also
    // disposes the command, so disposing inner instead would leak it.
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

    // Load bearing: Dapper builds its materialiser from the field types, and one built for decimal
    // then handed a BigDecimal fails with a cast.
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

    // Every named type goes to the driver. object is the exception: there the driver answers the
    // column's default, which for a widened column is the decimal this reader exists not to hand
    // back. A NULL still goes to the driver, so its own refusal is what a caller sees.
    public override T GetFieldValue<T>(int ordinal) =>
        this.AnswersObjectItself<T>(ordinal) && !this.inner.IsDBNull(ordinal)
            ? (T)this.GetValue(ordinal)
            : this.inner.GetFieldValue<T>(ordinal);

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

    // Over this reader, not the driver's, so the widened columns are widened here too.
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

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
        // The base calls Close, which closes the owner, so it runs before the owner is disposed.
        base.Dispose(disposing);

        if (disposing && !this.disposed)
        {
            this.disposed = true;
            this.owner.Dispose();
        }
    }

    // The inherited DisposeAsync calls the synchronous dispose, and closing a reader with rows left
    // drains the socket; the flag keeps the inherited path from disposing the owner twice.
    public override async ValueTask DisposeAsync()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            await CloseOwnerAsync(this.owner).ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

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

    private bool AnswersObjectItself<T>(int ordinal) =>
        typeof(T) == typeof(object) && this.widened[ordinal];

    // A faceted column and a domain both report the base type, so numeric(12,4) is covered.
    private static bool IsNumeric(PostgresType type) =>
        type is PostgresBaseType
        && string.Equals(type.Name, "numeric", StringComparison.Ordinal)
        && string.Equals(type.Namespace, "pg_catalog", StringComparison.Ordinal);
}
