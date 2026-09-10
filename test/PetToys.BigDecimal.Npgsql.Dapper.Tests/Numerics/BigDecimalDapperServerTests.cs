using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AwesomeAssertions;
using Dapper;
using Npgsql;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The mapping through Dapper, against a real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// Every case goes through the package's own registration over a data source built with the
/// adapter's, so either registration ceasing to take effect fails here rather than being replaced
/// by the test. The oracle is the server: a value read back is compared against the decomposition
/// of the server's own text rendering, never against what the package wrote.
/// </para>
/// <para>
/// The silent failures this layer has are its own, and three of these cases exist for them. A read
/// shape Dapper does not recognise answers a default-constructed value rather than throwing. A
/// column shape the reader fails to recognise is read by the driver as <see cref="decimal"/>, and
/// a narrow value is equal either way, so the assertions are on the CLR type as well as the value.
/// And Dapper caches its materialiser by the reader's column shape rather than by the query text,
/// which is why every case here gets a table of its own.
/// </para>
/// </remarks>
/// <param name="server">The server, one per this class, started on first use.</param>
[Trait(TestCategories.TraitName, TestCategories.Integration)]
public sealed class BigDecimalDapperServerTests(PostgresServer server) : IClassFixture<PostgresServer>
{
    private const string Wide = "123456789012345678901234567890.123456789";

    [Fact]
    public async Task RegisteringTheHandler_LeavesDecimalAsTheDefault()
    {
        await server.RequireAsync();
        await using var connection = await server.DataSource.OpenConnectionAsync(
            TestContext.Current.CancellationToken);

        // The cheapest test in this change and the one guarding the most expensive mistake. Dapper's
        // registry is global to the process, so a default that moved would change what every
        // untyped read in a consumer's application produces, at a cast far from the registration.
        connection.Query<decimal>("SELECT 1.50::numeric AS untouched_decimal").Single()
            .Should().Be(1.50m);

        var dynamicRow = (IDictionary<string, object>)connection
            .Query("SELECT 1.50::numeric AS untouched_dynamic").Single();
        dynamicRow["untouched_dynamic"].Should().BeOfType<decimal>();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1.50::numeric AS untouched_field, ARRAY[1.5]::numeric[] AS untouched_array";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        await reader.ReadAsync(TestContext.Current.CancellationToken);
        reader.GetFieldType(0).Should().Be<decimal>();
        reader.GetValue(1).Should().BeOfType<decimal[]>();
    }

    [Fact]
    public async Task AWideValue_ReadsIntoAPropertyExactly()
    {
        await using var table = await this.ReadyAsync();
        await table.ExecuteAsync($"INSERT INTO {table.Name} (id, v) VALUES (1, {Wide})");

        await using var connection = await this.OpenAsync();

        var row = connection.QueryBigDecimal<NumericRow>(
            $"SELECT id, v, 'property' AS shape FROM {table.Name} WHERE id = 1").Single();

        OracleValue.Observe(row.V).Should()
            .Be(FromServerText(await table.RenderAsync($"SELECT v::text FROM {table.Name} WHERE id = 1")));
    }

    [Fact]
    public async Task AWideValue_ReadsAsAScalarExactly()
    {
        await using var table = await this.ReadyAsync();
        await table.ExecuteAsync($"INSERT INTO {table.Name} (id, v) VALUES (1, {Wide})");

        await using var connection = await this.OpenAsync();

        // The shape that answers a default-constructed value and no exception without the handler.
        var value = connection.QueryBigDecimal<BigDecimal>(
            $"SELECT v FROM {table.Name} WHERE id = 1").Single();

        OracleValue.Observe(value).Should()
            .Be(FromServerText(await table.RenderAsync($"SELECT v::text FROM {table.Name} WHERE id = 1")));
    }

    [Fact]
    public async Task EveryFormOfTheColumn_IsRecognised()
    {
        await using var table = await this.ReadyAsync();
        await table.ExecuteAsync(
            $"INSERT INTO {table.Name} (id, v, f, a, d) VALUES (1, 2.5, 2.5, ARRAY[2.5], 2.5)");

        await using var connection = await this.OpenAsync();

        await using var reader = connection.ExecuteReader(
            $"SELECT v, f, sum(v) OVER () AS aggregated, a, d, id FROM {table.Name} WHERE id = 1")
            .AsBigDecimalReader();

        reader.Read().Should().BeTrue();

        // The type as well as the value. A narrow value read as decimal compares equal to the same
        // value read as this type, so a column the reader missed would pass a value-only assertion
        // - and the faceted column is exactly the one a data type name comparison misses, since the
        // driver renders it "numeric(12, 4)".
        var recognised = Enumerable.Range(0, 5)
            .Select(i => (Name: reader.GetName(i), Type: reader.GetFieldType(i), Value: reader.GetValue(i).GetType()))
            .ToList();

        recognised.Should().AllSatisfy(column =>
        {
            var expected = column.Name == "a" ? typeof(BigDecimal[]) : typeof(BigDecimal);
            column.Type.Should().Be(expected);
            column.Value.Should().Be(expected);
        });

        reader.GetFieldType(5).Should().Be<int>("a column that is not numeric is the driver's");
    }

    [Fact]
    public async Task EveryUntypedRouteThroughTheReader_AnswersTheWideType()
    {
        await using var table = await this.ReadyAsync();
        await table.ExecuteAsync($"INSERT INTO {table.Name} (id, v) VALUES (1, {Wide})");

        await using var connection = await this.OpenAsync();

        await using (var reader = connection
            .ExecuteReader($"SELECT v, id, 'untyped' AS shape FROM {table.Name} WHERE id = 1")
            .AsBigDecimalReader())
        {
            reader.Read().Should().BeTrue();

            // Three routes to the same column, and a reader that answered the driver's decimal on
            // any one of them would be narrowing through its own API.
            reader.GetValue(0).Should().BeOfType<BigDecimal>();
            reader.GetFieldValue<object>(0).Should().BeOfType<BigDecimal>();
            (await reader.GetFieldValueAsync<object>(0, TestContext.Current.CancellationToken))
                .Should().BeOfType<BigDecimal>();

            // A column this reader does not widen is still the driver's, by every route.
            reader.GetFieldValue<object>(1).Should().BeOfType<int>();
        }

        // A reader of its own: the connection carries one at a time, and an enumerator starts from
        // wherever its reader is rather than at the first row.
        await using var enumerated = connection
            .ExecuteReader($"SELECT v, id, 'enumerated' AS shape FROM {table.Name} WHERE id = 1")
            .AsBigDecimalReader();

        var records = enumerated.Cast<DbDataRecord>().ToList();

        records.Should().ContainSingle().Which[0].Should().BeOfType<BigDecimal>(
            "an enumerator built from the driver's reader would answer decimal here");
    }

    [Fact]
    public async Task AnArrayColumn_ReadsAsAnArrayOfTheType()
    {
        await using var table = await this.ReadyAsync();
        await table.ExecuteAsync($"INSERT INTO {table.Name} (id, a) VALUES (1, ARRAY[{Wide}, 1.50])");

        await using var connection = await this.OpenAsync();

        var row = connection.QueryBigDecimal<ArrayRow>(
            $"SELECT id, a, 'array' AS shape FROM {table.Name} WHERE id = 1").Single();

        row.A.Should().NotBeNull();
        row.A!.Select(OracleValue.Observe).Should().Equal(
            FromServerText(await table.RenderAsync($"SELECT a[1]::text FROM {table.Name} WHERE id = 1")),
            FromServerText(await table.RenderAsync($"SELECT a[2]::text FROM {table.Name} WHERE id = 1")));
    }

    [Fact]
    public async Task ADecimalMember_IsExactWhereItFitsAndRefusesWhereItDoesNot()
    {
        await using var table = await this.ReadyAsync();
        await table.ExecuteAsync($"INSERT INTO {table.Name} (id, v) VALUES (1, 1.50), (2, {Wide})");

        await using var connection = await this.OpenAsync();

        // A wide read widens every numeric column of its query, including ones a caller was not
        // thinking about. Nothing narrows quietly: it converts where it fits and throws where it
        // does not.
        connection.QueryBigDecimal<DecimalRow>(
            $"SELECT id, v, 'narrow' AS shape FROM {table.Name} WHERE id = 1").Single().V.Should().Be(1.5m);

        var beyond = () => connection.QueryBigDecimal<DecimalRow>(
            $"SELECT id, v, 'narrow' AS shape FROM {table.Name} WHERE id = 2").ToList();

        beyond.Should().Throw<DataException>()
            .WithInnerException<OverflowException>("a value decimal cannot hold is refused, not rounded");
    }

    [Fact]
    public async Task ARowMixingBothMemberTypes_ReadsBoth()
    {
        await using var table = await this.ReadyAsync();
        await table.ExecuteAsync($"INSERT INTO {table.Name} (id, v) VALUES (1, 1.50)");

        await using var connection = await this.OpenAsync();

        var row = connection.QueryBigDecimal<MixedRow>(
            $"SELECT v AS narrow, v AS wide, 'mixed' AS shape FROM {table.Name} WHERE id = 1").Single();

        row.Narrow.Should().Be(1.5m);
        OracleValue.Observe(row.Wide).Should().Be(new OracleValue(150, 2));
    }

    [Fact]
    public async Task AnOrdinaryDapperRead_FailsRatherThanNarrows()
    {
        await using var table = await this.ReadyAsync();
        await table.ExecuteAsync($"INSERT INTO {table.Name} (id, v) VALUES (1, {Wide}), (2, 'NaN')");

        await using var connection = await this.OpenAsync();

        // What a caller gets without the wide read, and the reason the README pairs each of these
        // with the call that answers it. Both are the driver's refusal, raised before the handler
        // is reached.
        var wide = () => connection.Query<NumericRow>(
            $"SELECT id, v, 'plain' AS shape FROM {table.Name} WHERE id = 1").ToList();

        wide.Should().Throw<DataException>().WithInnerException<OverflowException>();

        var nonFinite = () => connection.Query<NumericRow>(
            $"SELECT id, v, 'plain' AS shape FROM {table.Name} WHERE id = 2").ToList();

        nonFinite.Should().Throw<DataException>().WithInnerException<InvalidCastException>();
    }

    [Fact]
    public async Task AWideValue_IsWrittenExactly()
    {
        await using var table = await this.ReadyAsync();

        await using var connection = await this.OpenAsync();

        var value = BigDecimal.Parse(Wide, CultureInfo.InvariantCulture);
        connection.Execute($"INSERT INTO {table.Name} (id, v) VALUES (1, @v)", new { v = value });

        (await table.RenderAsync($"SELECT v::text FROM {table.Name} WHERE id = 1")).Should().Be(Wide);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public async Task TheNonFiniteValues_CrossBothWays(string text)
    {
        await using var table = await this.ReadyAsync();

        await using var connection = await this.OpenAsync();

        var value = BigDecimal.Parse(text, CultureInfo.InvariantCulture);
        connection.Execute($"INSERT INTO {table.Name} (id, v) VALUES (1, @v)", new { v = value });

        (await table.RenderAsync($"SELECT v::text FROM {table.Name} WHERE id = 1")).Should().Be(text);

        // The driver's own decimal path refuses all three, so this is a value only the wide read
        // can return.
        var read = connection.QueryBigDecimal<NumericRow>(
            $"SELECT id, v, 'nonfinite' AS shape FROM {table.Name} WHERE id = 1").Single();

        read.V.ToString(null, CultureInfo.InvariantCulture).Should().Be(text);
    }

    [Fact]
    public async Task ANullParameter_IsWrittenAsNull()
    {
        await using var table = await this.ReadyAsync();

        await using var connection = await this.OpenAsync();

        connection.Execute(
            $"INSERT INTO {table.Name} (id, v) VALUES (1, @v)",
            new { v = (BigDecimal?)null });

        (await table.RenderAsync($"SELECT v::text FROM {table.Name} WHERE id = 1")).Should().BeNull();
    }

    [Fact]
    public async Task ANullColumn_ReachesBothMemberShapes()
    {
        await using var table = await this.ReadyAsync();
        await table.ExecuteAsync($"INSERT INTO {table.Name} (id, v) VALUES (1, NULL)");

        await using var connection = await this.OpenAsync();

        connection.QueryBigDecimal<NullableNumericRow>(
            $"SELECT id, v, 'nullable' AS shape FROM {table.Name} WHERE id = 1")
            .Single().V.Should().BeNull();

        // Dapper leaves a non-nullable member at its default for a NULL column, which is what it
        // does for System.Decimal too. Pinned so that a change of it is deliberate rather than a
        // surprise in a consumer's row.
        connection.QueryBigDecimal<NumericRow>(
            $"SELECT id, v AS v FROM {table.Name} WHERE id = 1")
            .Single().V.Should().Be(BigDecimal.Zero);
    }

    [Fact]
    public async Task TheColumnsScaleComesBack_NotTheValues()
    {
        await using var table = await this.ReadyAsync();

        await using var connection = await this.OpenAsync();

        connection.Execute(
            $"INSERT INTO {table.Name} (id, f) VALUES (1, @f)",
            new { f = BigDecimal.Parse("1.5", CultureInfo.InvariantCulture) });

        // The package writes at the value's own scale and the server applies the column's. The
        // README states this, and states that the server rounds half away from zero when it does.
        (await table.RenderAsync($"SELECT f::text FROM {table.Name} WHERE id = 1")).Should().Be("1.5000");

        connection.QueryBigDecimal<NumericRow>(
            $"SELECT id, f AS v, 'faceted' AS shape FROM {table.Name} WHERE id = 1").Single().V.Scale.Should().Be(4);
    }

    [Fact]
    public async Task AValueBeyondTheColumn_IsTheServersRefusal()
    {
        await using var table = await this.ReadyAsync();

        await using var connection = await this.OpenAsync();

        var write = () => connection.Execute(
            $"INSERT INTO {table.Name} (id, f) VALUES (1, @f)",
            new { f = BigDecimal.Parse("12345678901234.5", CultureInfo.InvariantCulture) });

        // Relayed, not replaced. The handler is handed a parameter and never learns which column it
        // is bound for, so a client-side refusal here would be guessing at a schema it cannot see.
        write.Should().Throw<PostgresException>().Which.SqlState.Should().Be("22003");
    }

    [Fact]
    public async Task WithoutTheDataSourceRegistration_TheReadFailsByName()
    {
        await using var table = await this.ReadyAsync();
        await table.ExecuteAsync($"INSERT INTO {table.Name} (id, v) VALUES (1, 1.50)");

        await using var plain = new NpgsqlDataSourceBuilder(server.ConnectionString).Build();
        await using var connection = await plain.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var read = () => connection.QueryBigDecimal<NumericRow>(
            $"SELECT id, v, 'unregistered' AS shape FROM {table.Name} WHERE id = 1").ToList();

        read.Should().Throw<DataException>()
            .WithInnerException<InvalidCastException>("the prerequisite cannot be silently absent");
    }

    [Fact]
    public async Task WithoutTheDataSourceRegistration_TheWriteIsRefusedByName()
    {
        await using var table = await this.ReadyAsync();

        await using var plain = new NpgsqlDataSourceBuilder(server.ConnectionString).Build();
        await using var connection = await plain.OpenConnectionAsync(TestContext.Current.CancellationToken);

        var write = () => connection.Execute(
            $"INSERT INTO {table.Name} (id, v) VALUES (1, @v)",
            new { v = BigDecimal.One });

        write.Should().Throw<InvalidCastException>().WithMessage("*BigDecimal*");
    }

    [Fact]
    public async Task TheAsyncForm_ReadsTheSameValues()
    {
        await using var table = await this.ReadyAsync();
        await table.ExecuteAsync($"INSERT INTO {table.Name} (id, v) VALUES (1, {Wide})");

        await using var connection = await this.OpenAsync();

        var rows = await connection.QueryBigDecimalAsync<NumericRow>(
            new CommandDefinition(
                $"SELECT id, v, 'async' AS shape FROM {table.Name} WHERE id = 1",
                cancellationToken: TestContext.Current.CancellationToken));

        OracleValue.Observe(rows.Single().V).Should()
            .Be(FromServerText(await table.RenderAsync($"SELECT v::text FROM {table.Name} WHERE id = 1")));
    }

    [Fact]
    public async Task TheReaderOnItsOwn_IsTheUnbufferedRoute()
    {
        await using var table = await this.ReadyAsync();
        await table.ExecuteAsync($"INSERT INTO {table.Name} (id, v) VALUES (1, {Wide}), (2, 1.50)");

        await using var connection = await this.OpenAsync();

        // The two-line form the README documents for everything QueryBigDecimal does not cover.
        await using var reader = connection
            .ExecuteReader($"SELECT id, v, 'streamed' AS shape FROM {table.Name} ORDER BY id")
            .AsBigDecimalReader();

        var rows = reader.Parse<NumericRow>().ToList();

        rows.Select(row => OracleValue.Observe(row.V)).Should().Equal(
            FromServerText(await table.RenderAsync($"SELECT v::text FROM {table.Name} WHERE id = 1")),
            FromServerText(await table.RenderAsync($"SELECT v::text FROM {table.Name} WHERE id = 2")));
    }

    /// <summary>
    /// Decomposes the server's own rendering, which is the only source of an expected value here.
    /// </summary>
    /// <param name="text">What the server printed.</param>
    /// <returns>The mantissa and scale it describes.</returns>
    private static OracleValue FromServerText(string? text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var negative = text.StartsWith('-');
        var body = negative ? text[1..] : text;
        var point = body.IndexOf('.', StringComparison.Ordinal);
        var digits = point < 0 ? body : body.Remove(point, 1);
        var scale = point < 0 ? 0 : body.Length - point - 1;
        var unscaled = BigInteger.Parse(digits, CultureInfo.InvariantCulture);

        return new OracleValue(negative ? -unscaled : unscaled, scale);
    }

    private async Task<NumericTable> ReadyAsync()
    {
        await server.RequireAsync();

        return await NumericTable.CreateAsync(server.DataSource);
    }

    private async Task<NpgsqlConnection> OpenAsync() =>
        await server.DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
}
