using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using AwesomeAssertions;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using Dapper;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;
using DriverDecimal = ClickHouse.Driver.Numerics.ClickHouseDecimal;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The package through Dapper, against a real ClickHouse.
/// </summary>
/// <remarks>
/// <para>
/// The oracle is the server's own rendering, read back over the fixture's HTTP interface, in which
/// no part of this package takes part. A case that read back through Dapper would be the package
/// grading itself.
/// </para>
/// <para>
/// Every case gives its projection a marker column of its own. Dapper caches its materialiser by
/// the reader's column shape rather than by the query text, so two cases over the same shape can
/// answer from whichever ran first - which has already produced a confident wrong answer inside
/// this repository's own measurement. An alias on the value column is not a distinct shape: Dapper
/// maps a member by column name, so renaming one only makes the member stop being filled.
/// </para>
/// </remarks>
/// <param name="server">The server, one per this class, started on first use.</param>
[Trait(TestCategories.TraitName, TestCategories.Integration)]
public sealed class BigDecimalDapperServerTests(ClickHouseServer server) : IClassFixture<ClickHouseServer>
{
    private const string WideText = "12345678901234567890123.1234567890";

    /// <summary>Each width as a column, with the precision it carries.</summary>
    public static TheoryData<string, int> Columns =>
        new()
        {
            { "Decimal32(4)", 9 },
            { "Decimal64(8)", 18 },
            { "Decimal128(20)", 38 },
            { "Decimal256(40)", 76 },
        };

    [Theory]
    [MemberData(nameof(Columns))]
    public async Task AColumnOfEachWidth_ReadsExactly(string declared, int precision)
    {
        await server.RequireAsync();

        // A digit short of the column's own limit: the boundary is the write side's test below.
        var mantissa = BigInteger.Pow(10, precision - 1);
        var scale = Scale(declared);
        var table = await this.CreateAsync(declared, Rendered(mantissa, scale));

        try
        {
            await using var connection = this.Open();

            var row = connection.QuerySingle<DecimalRow>(
                $"SELECT i AS id, v, 'width{precision}' AS shape FROM {table} WHERE i = 1");

            OracleValue.Observe(row.V).Should().Be(new OracleValue(mantissa, scale));
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AValueWiderThanDecimal_ReadsExactlyInBothShapes()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal128(10)", WideText);

        try
        {
            await using var connection = this.Open();

            connection.QuerySingle<DecimalRow>(
                $"SELECT i AS id, v, 'object' AS shape FROM {table} WHERE i = 1")
                .V.ToString(null, CultureInfo.InvariantCulture).Should().Be(WideText);

            // The shape that answers a default-constructed value and no exception without the
            // handler, which is the state this package exists to end.
            connection.QuerySingle<BigDecimal>($"SELECT v FROM {table} WHERE i = 1")
                .ToString(null, CultureInfo.InvariantCulture).Should().Be(WideText);
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task TheColumnsScaleComesBack_NotTheValues()
    {
        await server.RequireAsync();

        // ClickHouse keeps the scale in the column type and nowhere in the value, so 1.5 written
        // into a Decimal64(4) column is 1.5000 on the way back whatever it was on the way in.
        var table = await this.CreateAsync("Decimal64(4)", "1.5");

        try
        {
            await using var connection = this.Open();

            var row = connection.QuerySingle<DecimalRow>(
                $"SELECT i AS id, v, 'scale' AS shape FROM {table} WHERE i = 1");

            OracleValue.Observe(row.V).Should().Be(new OracleValue(15000, 4));
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AnIntegerExpression_ReadsAsWell()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal64(4)", "1.5");

        try
        {
            await using var connection = this.Open();

            // count() is a UInt64 and is the most ordinary expression there is here, so a handler
            // that only knew the signed widths a PostgreSQL driver produces would refuse the first
            // aggregate a caller writes.
            connection.QuerySingle<BigDecimal>(
                $"SELECT count() AS v, 'counted' AS shape FROM {table}")
                .Should().Be(BigDecimal.One);
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task TheRegistration_MovesNoDefault()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal64(4)", "1.5");

        try
        {
            await using var connection = this.Open();

            // The cheapest test here and the one guarding the most expensive mistake: with the
            // handlers registered, code that never asks for BigDecimal reads what it always read.
            connection.QuerySingle<decimal>($"SELECT v, 'narrow' AS shape FROM {table} WHERE i = 1")
                .Should().Be(1.5000m);

            var dynamicRow = (IDictionary<string, object>)connection.QuerySingle(
                $"SELECT v, 'dynamic' AS shape FROM {table} WHERE i = 1");
            dynamicRow["v"].Should().BeOfType<DriverDecimal>();

            using var reader = connection.ExecuteReader(
                $"SELECT v, 'fieldtype' AS shape FROM {table} WHERE i = 1");
            reader.Read();
            reader.GetFieldType(0).Should().Be<DriverDecimal>();
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task ANullColumn_ReachesBothMemberShapes()
    {
        await server.RequireAsync();

        var table = $"dapper_{Guid.NewGuid():N}";
        await server.RunAsync($"CREATE TABLE {table} (i UInt32, v Nullable(Decimal64(4))) ENGINE = Memory");

        try
        {
            await server.RunAsync($"INSERT INTO {table} (i, v) VALUES (1, NULL), (2, '2.25')");

            await using var connection = this.Open();

            connection.QuerySingle<NullableDecimalRow>(
                $"SELECT i AS id, v, 'nullable' AS shape FROM {table} WHERE i = 1").V.Should().BeNull();

            OracleValue.Observe(connection.QuerySingle<NullableDecimalRow>(
                $"SELECT i AS id, v, 'nullable' AS shape FROM {table} WHERE i = 2").V!.Value)
                .Should().Be(new OracleValue(22500, 4));

            // Dapper leaves a non-nullable member at its default for a NULL column, which is what
            // it does for System.Decimal too. Pinned so that a change of it is deliberate.
            connection.QuerySingle<DecimalRow>(
                $"SELECT i AS id, v, 'notnull' AS shape FROM {table} WHERE i = 1")
                .V.Should().Be(BigDecimal.Zero);
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AnArrayColumn_ReadsAtTheColumnsScale()
    {
        await server.RequireAsync();

        var table = $"dapper_{Guid.NewGuid():N}";
        await server.RunAsync($"CREATE TABLE {table} (i UInt32, a Array(Decimal64(4))) ENGINE = Memory");

        try
        {
            await server.RunAsync($"INSERT INTO {table} (i, a) VALUES (1, ['1.5', '2.25'])");

            await using var connection = this.Open();

            var row = connection.QuerySingle<DecimalArrayRow>(
                $"SELECT i AS id, a, 'array' AS shape FROM {table} WHERE i = 1");

            row.A.Should().NotBeNull();
            row.A!.Select(OracleValue.Observe).Should().Equal(
                [new OracleValue(15000, 4), new OracleValue(22500, 4)]);
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task ARowMixingBothMemberTypes_Works()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal64(4)", "1.5");

        try
        {
            await using var connection = this.Open();

            var row = connection.QuerySingle<MixedDecimalRow>(
                $"SELECT v AS narrow, v AS wide, 'mixed' AS shape FROM {table} WHERE i = 1");

            row.Narrow.Should().Be(1.5000m);
            OracleValue.Observe(row.Wide).Should().Be(new OracleValue(15000, 4));
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task ANarrowMemberOverAWideValue_Throws()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal128(10)", WideText);

        try
        {
            await using var connection = this.Open();

            // Nothing narrows quietly: the driver refuses the conversion the member asked for.
            var read = () => connection.QuerySingle<decimal>(
                $"SELECT v, 'toonarrow' AS shape FROM {table} WHERE i = 1");

            read.Should().Throw<Exception>();
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AConnectionWithoutCustomDecimals_SaysWhichOptionIsOff()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal64(4)", "1.5");

        try
        {
            await using var connection = new ClickHouseConnection(server.ConnectionString(useCustomDecimals: false));

            var read = () => connection.QuerySingle<DecimalRow>(
                $"SELECT i AS id, v, 'nocustom' AS shape FROM {table} WHERE i = 1");

            read.Should().Throw<Exception>().WithInnerExceptionExactly<InvalidOperationException>()
                .WithMessage("*UseCustomDecimals*");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AnAnnotatedParameter_WritesExactly()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal128(10)");

        try
        {
            await using var connection = this.Open();
            var parameters = new DynamicParameters();
            parameters.Add("v", BigDecimal.Parse(WideText, CultureInfo.InvariantCulture));

            connection.Execute(
                $"INSERT INTO {table} (i, v) VALUES (1, {{v:Decimal128(10)}})",
                parameters);

            (await this.StoredAsync(table)).Should().Be(Rendered(
                BigInteger.Parse("123456789012345678901231234567890", CultureInfo.InvariantCulture),
                10));
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AParameterDapperRemoves_FailsAtTheServer()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal64(4)");

        try
        {
            await using var connection = this.Open();

            // Dapper binds a parameter only when it finds its name in the statement as @v, :v or
            // ?v, and ClickHouse's own {v:Type} is none of those, so an anonymous parameter object
            // loses the parameter before any handler is consulted. Loud, and the reason every
            // documented example passes parameters the other way.
            var write = () => connection.Execute(
                $"INSERT INTO {table} (i, v) VALUES (1, {{v:Decimal64(4)}})",
                new { v = BigDecimal.One });

            write.Should().Throw<Exception>().WithMessage("*not set*");

            (await this.StoredAsync(table)).Should().BeEmpty();
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AValueBelowTheColumnsScale_RoundsHalfToEvenRatherThanTruncating()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal64(4)");
        var control = await this.CreateAsync("Decimal64(4)");

        try
        {
            await using var connection = this.Open();
            var parameters = new DynamicParameters();
            parameters.Add("a", BigDecimal.Parse("1.00005", CultureInfo.InvariantCulture));
            parameters.Add("b", BigDecimal.Parse("1.00015", CultureInfo.InvariantCulture));
            parameters.Add("c", BigDecimal.Parse("1.00025", CultureInfo.InvariantCulture));

            connection.Execute(
                $"INSERT INTO {table} (i, v) VALUES (1, {{a:Decimal64(4)}}), (2, {{b:Decimal64(4)}}), (3, {{c:Decimal64(4)}})",
                parameters);

            // Both halves, because two components agreeing by accident and two disagreeing
            // silently look identical from inside one of them. The control is the same three
            // values handed to the server as text, which is what happens with no formatter in the
            // path: it truncates toward zero where this package rounds half to even.
            await server.RunAsync(
                $"INSERT INTO {control} (i, v) VALUES (1, '1.00005'), (2, '1.00015'), (3, '1.00025')");

            (await server.RenderAsync($"SELECT v FROM {table} ORDER BY i"))
                .Should().Equal(["1", "1.0002", "1.0002"]);
            (await server.RenderAsync($"SELECT v FROM {control} ORDER BY i"))
                .Should().Equal(["1", "1.0001", "1.0002"]);
        }
        finally
        {
            await this.DropAsync(table);
            await this.DropAsync(control);
        }
    }

    [Fact]
    public async Task AValueBeyondTheColumn_IsRefusedByNameBeforeItIsSent()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal64(4)");

        try
        {
            await using var connection = this.Open();
            var parameters = new DynamicParameters();
            parameters.Add("v", BigDecimal.Parse(WideText, CultureInfo.InvariantCulture));

            var write = () => connection.Execute(
                $"INSERT INTO {table} (i, v) VALUES (1, {{v:Decimal64(4)}})",
                parameters);

            write.Should().Throw<OverflowException>().WithMessage("*'v' of type Decimal(18, 4)*");

            (await this.StoredAsync(table)).Should().BeEmpty();
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public async Task ANonFiniteValue_IsRefused(string text)
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal64(4)");

        try
        {
            await using var connection = this.Open();
            var parameters = new DynamicParameters();
            parameters.Add("v", BigDecimal.Parse(text, CultureInfo.InvariantCulture));

            var write = () => connection.Execute(
                $"INSERT INTO {table} (i, v) VALUES (1, {{v:Decimal64(4)}})",
                parameters);

            write.Should().Throw<NotSupportedException>();

            (await this.StoredAsync(table)).Should().BeEmpty();
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task TheAdaptersOwnConnectionCall_ServesTheWriteSideToo()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal64(4)");

        try
        {
            // A caller who installed the adapter's wide mapping already has this package's
            // formatter, so the two calls compose rather than conflict.
            await using var connection = new ClickHouseConnection(
                new ClickHouseClientSettings(server.ConnectionString()).UseBigDecimal());
            var parameters = new DynamicParameters();
            parameters.Add("v", BigDecimal.Parse("1.00025", CultureInfo.InvariantCulture));

            connection.Execute(
                $"INSERT INTO {table} (i, v) VALUES (1, {{v:Decimal64(4)}})",
                parameters);

            (await this.StoredAsync(table)).Should().Be("1.0002");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task WithoutAFormatter_AWriteIsRefusedRatherThanNarrowed()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal128(10)");

        try
        {
            // The failure a caller who skipped the connection call meets, and it is loud. The
            // driver converts a value it has no formatter for through Convert.ToDecimal, and
            // BigDecimal implements no IConvertible, so the cast fails before anything is sent
            // rather than storing a value with digits missing.
            await using var connection = new ClickHouseConnection(server.ConnectionString());
            var parameters = new DynamicParameters();
            parameters.Add("v", BigDecimal.Parse(WideText, CultureInfo.InvariantCulture));

            var write = () => connection.Execute(
                $"INSERT INTO {table} (i, v) VALUES (1, {{v:Decimal128(10)}})",
                parameters);

            write.Should().Throw<InvalidCastException>().WithMessage("*IConvertible*");

            (await this.StoredAsync(table)).Should().BeEmpty();
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    /// <summary>The scale out of a width-named declared type, for the theory above.</summary>
    /// <param name="declared">The declared type.</param>
    /// <returns>Its scale.</returns>
    private static int Scale(string declared) =>
        int.Parse(
            declared[(declared.IndexOf('(', StringComparison.Ordinal) + 1)..^1],
            CultureInfo.InvariantCulture);

    /// <summary>Renders a value the way the server renders one.</summary>
    /// <param name="mantissa">The unscaled value.</param>
    /// <param name="scale">The scale.</param>
    /// <returns>The text the server would answer.</returns>
    /// <remarks>
    /// ClickHouse trims: <c>toString</c> on a decimal drops trailing zeros and answers a bare
    /// <c>0</c> for zero, which the adapter's byte-level suite established against the server.
    /// </remarks>
    private static string Rendered(BigInteger mantissa, int scale)
    {
        var digits = BigInteger.Abs(mantissa).ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        var sign = mantissa.Sign < 0 ? "-" : string.Empty;
        var text = scale == 0 ? digits : digits[..^scale] + "." + digits[^scale..];

        if (text.Contains('.', StringComparison.Ordinal))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text.Length == 0 ? "0" : sign + text;
    }

    [Fact]
    public async Task AnUnannotatedParameter_IsRefusedByThisRepositoryRatherThanTheDriver()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal64(4)");

        try
        {
            await using var connection = this.Open();
            var parameters = new DynamicParameters();
            parameters.Add("v", BigDecimal.Parse("1.00015", CultureInfo.InvariantCulture));

            var writing = () => connection.Execute(
                $"INSERT INTO {table} (i, v) VALUES (1, @v)",
                parameters);

            // The parameter survives Dapper - it is named in Dapper's own syntax - and reaches the
            // driver with no ClickHouse type on it. That is the case the driver answers with a bare
            // Unknown type, and the case this repository's resolver answers by name.
            writing.Should().Throw<InvalidOperationException>()
                .WithMessage("*'v'*")
                .WithMessage("*{v:Decimal256(6)}*")
                .WithMessage("*DynamicParameters.Add*");

            (await this.StoredAsync(table)).Should().BeEmpty("nothing was sent");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    /// <summary>Opens a connection configured the way this package's documentation says.</summary>
    /// <returns>The connection.</returns>
    private ClickHouseConnection Open() =>
        new(new ClickHouseClientSettings(server.ConnectionString()).UseBigDecimalForDapper());

    /// <summary>Reads the value column back over the fixture's own interface.</summary>
    /// <param name="table">The table.</param>
    /// <returns>The server's rendering of the single row, or an empty string for no rows.</returns>
    private async Task<string> StoredAsync(string table)
    {
        var rows = await server.RenderAsync($"SELECT v FROM {table} ORDER BY i");

        return rows.Count == 0 ? string.Empty : rows[0];
    }

    /// <summary>Creates a two-column table, optionally with one row in it.</summary>
    /// <param name="declared">The declared type of the value column.</param>
    /// <param name="literal">A decimal literal to insert, or nothing for an empty table.</param>
    /// <returns>The table's name.</returns>
    private async Task<string> CreateAsync(string declared, string? literal = null)
    {
        var table = $"dapper_{Guid.NewGuid():N}";
        await server.RunAsync($"CREATE TABLE {table} (i UInt32, v {declared}) ENGINE = Memory");

        if (literal is not null)
        {
            await server.RunAsync($"INSERT INTO {table} (i, v) VALUES (1, '{literal}')");
        }

        return table;
    }

    /// <summary>Drops a table without letting the cleanup speak over the failure that brought us here.</summary>
    /// <param name="table">The table to drop.</param>
    /// <returns>Nothing, and nothing thrown.</returns>
    private async Task DropAsync(string table)
    {
        try
        {
            await server.RunAsync($"DROP TABLE IF EXISTS {table}");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            // The table is in a container that is thrown away with the assembly.
        }
    }
}
