using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AwesomeAssertions;
using Npgsql;
using NpgsqlTypes;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The mapping through the driver, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PostgresNumericServerTests"/> checks the codec, and it travels by a raw binary
/// <c>COPY</c> exactly because that route converts nothing: the bytes it asserts are the bytes on
/// the wire. This class is the layer above it and covers what that route deliberately bypasses,
/// which is the registration, the converter and the driver's own machinery around them.
/// </para>
/// <para>
/// Every case goes through a data source built with the package's own <c>UseBigDecimal</c>, so a
/// registration that stopped taking effect fails here rather than being replaced by the test. The
/// oracle is still the server: a value is compared against the decimal string a
/// <see cref="BigInteger"/> derives from its mantissa and scale, never against what the package
/// read back from what the package wrote.
/// </para>
/// </remarks>
/// <param name="server">The server, one per this class, started on first use.</param>
[Trait(TestCategories.TraitName, TestCategories.Integration)]
public sealed class BigDecimalMappingServerTests(PostgresServer server) : IClassFixture<PostgresServer>
{
    /// <summary>The largest magnitude the type holds, which is where reading starts to fail.</summary>
    private static BigInteger MaxMagnitude { get; } = (BigInteger.One << 256) - BigInteger.One;

    [Fact]
    public async Task RegisteringTheMapping_LeavesDecimalAsTheDefault()
    {
        await server.RequireAsync();

        await using var command = server.DataSource.CreateCommand(
            "SELECT 123.4500::numeric AS amount, ARRAY[1.5, 2.5]::numeric[] AS amounts");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        // The whole bargain of this package in three assertions: the mapping is registered on a
        // data source the application shares, so if these move, code written before the package
        // existed reads a different type out of the same column, at a cast far from here.
        reader.GetFieldType(0).Should().Be<decimal>("registering the mapping must not change what an untyped read produces");
        reader.GetValue(0).Should().BeOfType<decimal>();
        reader.GetValue(1).Should().BeOfType<decimal[]>("the array default moves with the element default");
    }

    [Fact]
    public async Task TheSlimBuilder_CarriesTheSameMapping()
    {
        await server.RequireAsync();

        // One implementation, reached through the other builder. The registration is on the type
        // mapper interface and these overloads only return the builder, so this is the assertion
        // that the interface method is what both of them run.
        await using var source = new NpgsqlSlimDataSourceBuilder(server.ConnectionString)
            .UseBigDecimal()
            .Build();

        await using var command = source.CreateCommand("SELECT 1.2345::numeric");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        Observe(reader.GetBigDecimal(0)).Should().Be(new OracleValue(12345, 4));
    }

    [Fact]
    public async Task AValueWiderThanDecimal_ReadsExactly()
    {
        await server.RequireAsync();

        // 40 significant digits, well past System.Decimal's 28 to 29, so a mapping that went
        // through it could not produce this at all.
        const string Literal = "1234567890123456789012345678901.234567890";

        var expected = new OracleValue(BigInteger.Parse("1234567890123456789012345678901234567890", CultureInfo.InvariantCulture), 9);

        await using var command = server.DataSource.CreateCommand($"SELECT '{Literal}'::numeric AS amount");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        Observe(reader.GetBigDecimal("amount")).Should().Be(expected);
    }

    [Fact]
    public async Task ADeclaredColumn_ReadsTheSameWay()
    {
        await server.RequireAsync();

        await using var command = server.DataSource.CreateCommand("SELECT 12.3400::numeric(12,4)");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        // A declared numeric(p, s) is the same type identifier as an unconstrained one, so it
        // resolves through the same converter; the declaration only decides what the server stored.
        Observe(reader.GetBigDecimal(0)).Should().Be(new OracleValue(123400, 4));
    }

    [Fact]
    public async Task TrailingZeros_SurviveTheRead()
    {
        await server.RequireAsync();

        await using var command = server.DataSource.CreateCommand("SELECT 1.500::numeric, 0.00::numeric, 100::numeric");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        // PostgreSQL carries the display scale beside the digits, and the scale is half of what
        // this type promises, so a mapping that inferred it from the digits would lose exactly the
        // half that is hardest to get right.
        Observe(reader.GetBigDecimal(0)).Should().Be(new OracleValue(1500, 3));
        Observe(reader.GetBigDecimal(1)).Should().Be(new OracleValue(BigInteger.Zero, 2));
        Observe(reader.GetBigDecimal(2)).Should().Be(new OracleValue(100, 0));
    }

    [Fact]
    public async Task AParameterCarryingOnlyAValue_IsSentAsNumeric()
    {
        await server.RequireAsync();

        var value = BigDecimal.FromScaled(BigInteger.Parse("98765432109876543210987654321098765432", CultureInfo.InvariantCulture), 18);

        await using var command = server.DataSource.CreateCommand("SELECT $1::numeric::text");
        command.Parameters.Add(new NpgsqlParameter { Value = value });

        var rendered = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        rendered.Should().Be(Observe(value).ToDecimalString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AParameterWithAnExplicitType_IsSentAsNumeric(bool useDbType)
    {
        await server.RequireAsync();

        var value = BigDecimal.FromScaled(new BigInteger(-1234500), 5);

        var parameter = useDbType
            ? new NpgsqlParameter { Value = value, DbType = DbType.Decimal }
            : new NpgsqlParameter { Value = value, NpgsqlDbType = NpgsqlDbType.Numeric };

        await using var command = server.DataSource.CreateCommand("SELECT $1::numeric::text");
        command.Parameters.Add(parameter);

        var rendered = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        rendered.Should().Be(Observe(value).ToDecimalString());
    }

    [Fact]
    public async Task TheExtremesOfTheType_Write()
    {
        await server.RequireAsync();

        // Writing cannot overflow, because PostgreSQL numeric holds far more than this type does in
        // both directions. Asserted at the extremes rather than stated: without these it is a
        // sentence, and it stops being true the day MaxScale moves.
        BigDecimal[] values =
        [
            BigDecimal.MaxValue,
            BigDecimal.MinValue,
            BigDecimal.FromScaled(MaxMagnitude, BigDecimal.MaxScale),
            BigDecimal.FromScaled(BigInteger.One, BigDecimal.MaxScale),
        ];

        foreach (var value in values)
        {
            await using var command = server.DataSource.CreateCommand("SELECT $1::numeric::text");
            command.Parameters.Add(new NpgsqlParameter { Value = value });

            var rendered = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

            rendered.Should().Be(Observe(value).ToDecimalString(), "every value of the type fits a numeric");
        }
    }

    [Fact]
    public async Task AnOverflowThroughTheAccessor_NamesTheColumn()
    {
        await server.RequireAsync();

        // One unit past the largest magnitude, composed by the server: the case is not limited by
        // what this type can express, which is the point of putting the boundary here.
        var beyond = (MaxMagnitude + BigInteger.One).ToString(CultureInfo.InvariantCulture);

        await using var command = server.DataSource.CreateCommand($"SELECT {beyond}::numeric AS invoice_total");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        var read = () => reader.GetBigDecimal(0);

        read.Should().Throw<OverflowException>()
            .WithMessage("*invoice_total*")
            .Which.InnerException.Should().BeOfType<OverflowException>(
                "the converter's own exception is carried rather than replaced");
    }

    [Fact]
    public async Task AnOverflowThroughTheDriver_IsStillAnOverflow()
    {
        await server.RequireAsync();

        var beyond = (MaxMagnitude + BigInteger.One).ToString(CultureInfo.InvariantCulture);

        await using var command = server.DataSource.CreateCommand($"SELECT {beyond}::numeric AS invoice_total");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        // The documented boundary of the convenience: a caller who does not use our accessor, or
        // who reaches the column through Dapper or Entity Framework Core, gets the same mapping and
        // the same exception type without the column name.
        var read = () => reader.GetFieldValue<BigDecimal>(0);

        read.Should().Throw<OverflowException>();
    }

    [Fact]
    public async Task AValueAtTheBoundary_ReadsRatherThanOverflowing()
    {
        await server.RequireAsync();

        var boundary = MaxMagnitude.ToString(CultureInfo.InvariantCulture);

        await using var command = server.DataSource.CreateCommand($"SELECT {boundary}::numeric");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        Observe(reader.GetBigDecimal(0)).Should().Be(new OracleValue(MaxMagnitude, 0));
    }

    [Fact]
    public async Task ALongerFraction_RoundsRatherThanThrowing()
    {
        await server.RequireAsync();

        // 1500e-258: three digits past MaxScale, and what is given up sits exactly on a tie. Half
        // to even takes 1.5 to 2, so the result is 2e-255 and not 1e-255, which is what separates
        // rounding from truncation. The magnitude is nowhere near binding here.
        var literal = "0." + new string('0', 254) + "1500";

        await using var command = server.DataSource.CreateCommand($"SELECT '{literal}'::numeric");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        Observe(reader.GetBigDecimal(0)).Should().Be(new OracleValue(2, BigDecimal.MaxScale));
    }

    [Fact]
    public async Task ANullColumn_KeepsTheDriversOwnMessage()
    {
        await server.RequireAsync();

        await using var command = server.DataSource.CreateCommand("SELECT NULL::numeric AS invoice_total");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        // Nothing in this package handles NULL, and that is deliberate: the driver already names
        // the column and says what happened, and wrapping it would replace a good message.
        var read = () => reader.GetBigDecimal(0);

        read.Should().Throw<InvalidCastException>().WithMessage("*invoice_total*");
    }

    [Fact]
    public async Task TheNullableForm_ReadsAValueAndANull()
    {
        await server.RequireAsync();

        await using var command = server.DataSource.CreateCommand("SELECT 1.5::numeric, NULL::numeric");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        reader.GetNullableBigDecimal(0).Should().NotBeNull().And.Match<BigDecimal?>(value => Observe(value!.Value) == new OracleValue(15, 1));
        reader.GetNullableBigDecimal(1).Should().BeNull();
    }

    [Fact]
    public async Task TheAsynchronousAccessors_ReadWhatTheSynchronousOnesDo()
    {
        await server.RequireAsync();

        await using var command = server.DataSource.CreateCommand(
            "SELECT 1.500::numeric AS amount, NULL::numeric AS missing");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        var expected = new OracleValue(1500, 3);

        Observe(await reader.GetBigDecimalAsync(0, TestContext.Current.CancellationToken)).Should().Be(expected);
        Observe(await reader.GetBigDecimalAsync("amount", TestContext.Current.CancellationToken)).Should().Be(expected);

        var byOrdinal = await reader.GetNullableBigDecimalAsync(0, TestContext.Current.CancellationToken);
        var byName = await reader.GetNullableBigDecimalAsync("amount", TestContext.Current.CancellationToken);

        Observe(byOrdinal!.Value).Should().Be(expected);
        Observe(byName!.Value).Should().Be(expected);

        // The nullable form has to ask the driver for the nullable type, not for the struct: asking
        // for the struct would throw on every NULL column and the value cases above would not say so.
        (await reader.GetNullableBigDecimalAsync(1, TestContext.Current.CancellationToken)).Should().BeNull();
        (await reader.GetNullableBigDecimalAsync("missing", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task TheNullableAccessor_ReadsByName()
    {
        await server.RequireAsync();

        await using var command = server.DataSource.CreateCommand(
            "SELECT 2.25::numeric AS amount, NULL::numeric AS missing");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        Observe(reader.GetNullableBigDecimal("amount")!.Value).Should().Be(new OracleValue(225, 2));
        reader.GetNullableBigDecimal("missing").Should().BeNull();
    }

    [Fact]
    public async Task AnOverflowThroughAnAsynchronousAccessor_NamesTheColumn()
    {
        await server.RequireAsync();

        var beyond = (MaxMagnitude + BigInteger.One).ToString(CultureInfo.InvariantCulture);

        await using var command = server.DataSource.CreateCommand($"SELECT {beyond}::numeric AS invoice_total");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        // The asynchronous path is the one where the exception crosses the await machinery, so the
        // catch that adds the name has to be asserted there and not inferred from the sync case.
        var read = async () => await reader.GetBigDecimalAsync("invoice_total", TestContext.Current.CancellationToken);

        await read.Should().ThrowAsync<OverflowException>().WithMessage("*invoice_total*");

        var readNullable = async () => await reader.GetNullableBigDecimalAsync(0, TestContext.Current.CancellationToken);

        await readNullable.Should().ThrowAsync<OverflowException>().WithMessage("*invoice_total*");
    }

    [Fact]
    public async Task AnArrayColumn_ReadsAsAnArrayAndAsAList()
    {
        await server.RequireAsync();

        await using var command = server.DataSource.CreateCommand("SELECT ARRAY[1.50, -2.5, 0]::numeric[]");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        OracleValue[] expected = [new(150, 2), new(-25, 1), new(BigInteger.Zero, 0)];

        reader.GetFieldValue<BigDecimal[]>(0).Select(Observe).Should().Equal(expected);
        reader.GetFieldValue<List<BigDecimal>>(0).Select(Observe).Should().Equal(expected);
    }

    [Fact]
    public async Task AnArrayParameter_IsSentAsNumericArray()
    {
        await server.RequireAsync();

        // The writing direction resolves through the array converter rather than the element one,
        // so a mapping registered on the wrong resolver would pass every read above and fail here.
        BigDecimal[] values =
        [
            BigDecimal.FromScaled(BigInteger.Parse("12345678901234567890123456789012345678", CultureInfo.InvariantCulture), 18),
            BigDecimal.FromScaled(new BigInteger(-1500), 3),
        ];

        await using var command = server.DataSource.CreateCommand("SELECT array_to_string($1::numeric[], '|')");
        command.Parameters.Add(new NpgsqlParameter { Value = values });

        var rendered = (string?)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        rendered.Should().Be(string.Join('|', values.Select(value => Observe(value).ToDecimalString())));
    }

    [Fact]
    public async Task AnArrayWithANull_ReadsAsNullableElements()
    {
        await server.RequireAsync();

        await using var command = server.DataSource.CreateCommand("SELECT ARRAY[1.5, NULL]::numeric[]");
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);

        var values = reader.GetFieldValue<List<BigDecimal?>>(0);

        values.Should().HaveCount(2);
        Observe(values[0]!.Value).Should().Be(new OracleValue(15, 1));
        values[1].Should().BeNull();
    }

    [Fact]
    public async Task ABulkLoad_MapsThroughTheSameConverter()
    {
        await server.RequireAsync();

        // The bulk path resolves the mapping through a different entry point than a command
        // parameter, so "it happens to work" is not a promise until this runs. It is also the path
        // a caller with enough rows to care about precision is most likely to take.
        BigDecimal[] values =
        [
            BigDecimal.FromScaled(BigInteger.Parse("12345678901234567890123456789012345678", CultureInfo.InvariantCulture), 18),
            BigDecimal.MaxValue,
            BigDecimal.FromScaled(new BigInteger(-1500), 3),
            BigDecimal.NaN,
        ];

        var table = $"bulk_{Guid.NewGuid():N}";

        await using var connection = await server.DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await Execute(connection, $"CREATE TABLE {table} (i int, v numeric)");

        try
        {
            await using (var importer = await connection.BeginBinaryImportAsync(
                $"COPY {table} (i, v) FROM STDIN (FORMAT BINARY)",
                TestContext.Current.CancellationToken))
            {
                for (var index = 0; index < values.Length; index++)
                {
                    await importer.StartRowAsync(TestContext.Current.CancellationToken);
                    await importer.WriteAsync(index, NpgsqlDbType.Integer, TestContext.Current.CancellationToken);
                    await importer.WriteAsync(values[index], TestContext.Current.CancellationToken);
                }

                await importer.CompleteAsync(TestContext.Current.CancellationToken);
            }

            var rendered = new List<string>(values.Length);

            await using var query = new NpgsqlCommand($"SELECT v::text FROM {table} ORDER BY i", connection);
            await using var reader = await query.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                rendered.Add(reader.GetString(0));
            }

            rendered.Should().Equal(
                Observe(values[0]).ToDecimalString(),
                Observe(values[1]).ToDecimalString(),
                Observe(values[2]).ToDecimalString(),
                "NaN");
        }
        finally
        {
            await Drop(connection, table);
        }
    }

    [Fact]
    public async Task TheNonFiniteValues_CrossAsThemselves()
    {
        await server.RequireAsync();

        BigDecimal[] values = [BigDecimal.NaN, BigDecimal.PositiveInfinity, BigDecimal.NegativeInfinity];
        string[] expected = ["NaN", "Infinity", "-Infinity"];

        for (var index = 0; index < values.Length; index++)
        {
            await using var command = server.DataSource.CreateCommand("SELECT $1::numeric::text, $1::numeric");
            command.Parameters.Add(new NpgsqlParameter { Value = values[index] });

            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            await reader.ReadAsync(TestContext.Current.CancellationToken);

            reader.GetString(0).Should().Be(expected[index], "the server stores the value as itself");

            var read = reader.GetBigDecimal(1);

            BigDecimal.IsNaN(read).Should().Be(BigDecimal.IsNaN(values[index]));
            BigDecimal.IsPositiveInfinity(read).Should().Be(BigDecimal.IsPositiveInfinity(values[index]));
            BigDecimal.IsNegativeInfinity(read).Should().Be(BigDecimal.IsNegativeInfinity(values[index]));
        }
    }

    /// <summary>
    /// Describes what the mapping produced, in the mantissa and scale the oracle compares. This is
    /// an observation of a result and never a source of an expected value.
    /// </summary>
    /// <param name="value">The value the mapping produced.</param>
    /// <returns>Its mantissa and scale.</returns>
    private static OracleValue Observe(BigDecimal value) => OracleValue.Observe(value);

    /// <summary>
    /// Drops the table without letting the cleanup speak over the failure that brought us here.
    /// </summary>
    /// <remarks>
    /// A payload the server refuses aborts the transaction, and every later statement on that
    /// connection fails with it. That is exactly the case where the original exception is the one
    /// naming the defect, so this one is swallowed. The table goes away with the container.
    /// </remarks>
    /// <param name="connection">The connection the table was created on.</param>
    /// <param name="table">The table to drop.</param>
    /// <returns>Nothing, and nothing thrown.</returns>
    private static async Task Drop(NpgsqlConnection connection, string table)
    {
        try
        {
            await Execute(connection, $"DROP TABLE IF EXISTS {table}");
        }
        catch (NpgsqlException)
        {
            // The table is in a container that is thrown away with the class.
        }
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
