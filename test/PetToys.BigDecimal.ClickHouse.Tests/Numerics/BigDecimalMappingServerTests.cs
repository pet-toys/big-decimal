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
using ClickHouse.Driver.Utility;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;
using DriverDecimal = ClickHouse.Driver.Numerics.ClickHouseDecimal;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The adapter through the driver, against a real ClickHouse.
/// </summary>
/// <remarks>
/// <para>
/// A layer above the byte-level suite beside it, not a replacement for it. That one travels by a
/// route that converts nothing, which is what makes its assertions about bytes mean anything; this
/// one covers exactly what that route bypasses, which is the registration, the two hooks, and the
/// driver's own machinery around them.
/// </para>
/// <para>
/// The oracle is the server's own rendering compared against a <see cref="BigInteger"/>
/// decomposition, never the package reading back what the package wrote. Tables are created and
/// read back over the fixture's HTTP interface, in which no part of the adapter takes part.
/// </para>
/// </remarks>
/// <remarks>
/// <para>
/// The absent <c>using</c> is deliberate and is the only pin there can be on it. This file reaches
/// its readers through <c>var</c> and never imports <c>ClickHouse.Driver.ADO.Readers</c>, which is
/// the call shape a caller has: if the accessors moved back to the namespace their receiver lives
/// in, this file would stop compiling rather than quietly stop being reachable.
/// </para>
/// </remarks>
/// <param name="server">The server, one per this class, started on first use.</param>
[Trait(TestCategories.TraitName, TestCategories.Integration)]
public sealed class BigDecimalMappingServerTests(ClickHouseServer server) : IClassFixture<ClickHouseServer>
{
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

        // A digit short of the column's own limit: the boundary is its own test below.
        var mantissa = BigInteger.Pow(10, precision - 1);
        var scale = Scale(declared);
        var table = await this.CreateAsync(declared, Rendered(mantissa, scale));

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());
            var value = await ReadOneAsync(client, table);

            value.Should().Be(BigDecimal.FromScaled(mantissa, scale));
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task TheWidestValueTheWidestColumnHolds_ReadsExactly()
    {
        await server.RequireAsync();

        var mantissa = BigInteger.Pow(10, 76) - BigInteger.One;
        var table = await this.CreateAsync("Decimal256(0)", Rendered(mantissa, 0));

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());
            var value = await ReadOneAsync(client, table);

            value.Should().Be(BigDecimal.FromScaled(mantissa, 0));
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task TheLongestFractionTheWidestColumnHolds_ReadsExactly()
    {
        await server.RequireAsync();

        var mantissa = BigInteger.Pow(10, 76) - BigInteger.One;
        var table = await this.CreateAsync("Decimal256(76)", Rendered(mantissa, 76));

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());
            var value = await ReadOneAsync(client, table);

            value.Scale.Should().Be(76);
            value.Should().Be(BigDecimal.FromScaled(mantissa, 76));
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AValueWiderThanDecimal_ReadsWhereTheDriversOwnAccessorRefuses()
    {
        await server.RequireAsync();

        var mantissa = BigInteger.Pow(10, 70);
        var table = await this.CreateAsync("Decimal256(6)", Rendered(mantissa, 6));

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());
            await using var reader = await client.ExecuteReaderAsync(
                $"SELECT v FROM {table}",
                null,
                ClickHouseBigDecimal.CreateQueryOptions(),
                TestContext.Current.CancellationToken);
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

            reader.GetBigDecimal(0).Should().Be(BigDecimal.FromScaled(mantissa, 6));

            // The same column without the mapping, which is where the driver's own accessor was
            // measured. Asserting it on the mapped reader would pin a guess: what GetDecimal does
            // with a BigDecimal handed to it is the driver's business and was not observed.
            await using var unmapped = await client.ExecuteReaderAsync(
                $"SELECT v FROM {table}",
                null,
                new QueryOptions(),
                TestContext.Current.CancellationToken);
            (await unmapped.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

            var narrowing = () => unmapped.GetDecimal(0);
            narrowing.Should().Throw<OverflowException>("System.Decimal cannot hold 71 digits");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task TheNarrowRegistration_StopsAtItsOwnQuery()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal128(4)", "1.5");

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());

            await using (var mapped = await client.ExecuteReaderAsync(
                $"SELECT v FROM {table}",
                null,
                ClickHouseBigDecimal.CreateQueryOptions(),
                TestContext.Current.CancellationToken))
            {
                (await mapped.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
                mapped.GetValue(0).Should().BeOfType<BigDecimal>();
            }

            await using var plain = await client.ExecuteReaderAsync(
                $"SELECT v FROM {table}",
                null,
                new QueryOptions(),
                TestContext.Current.CancellationToken);
            (await plain.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
            plain.GetValue(0).Should().BeOfType<DriverDecimal>("a neighbouring query is untouched");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task TheWideRegistration_ReachesTheCommandThatHasNoNarrowForm()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal128(4)", "1.5");

        try
        {
            await using var connection = new ClickHouseConnection(Wide());
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand($"SELECT v FROM {table}");
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

            reader.GetValue(0).Should().BeOfType<BigDecimal>();
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task TheReaderKeepsReportingTheDriversOwnType_WhichIsPinnedRatherThanFixed()
    {
        await server.RequireAsync();

        // Not a defect this package can repair: GetFieldType is answered from the driver's own type
        // registry, which no hook reaches. It is asserted so that a driver change is noticed and so
        // that the README's warning stays true.
        var table = await this.CreateAsync("Decimal128(4)", "1.5");

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());
            await using var reader = await client.ExecuteReaderAsync(
                $"SELECT v FROM {table}",
                null,
                ClickHouseBigDecimal.CreateQueryOptions(),
                TestContext.Current.CancellationToken);

            reader.GetFieldType(0).Should().Be<DriverDecimal>();
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
            reader.GetValue(0).Should().BeOfType<BigDecimal>("which is what the reader disagrees with itself about");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task WithoutTheDriversOwnDecimals_TheReadIsRefusedByName()
    {
        await server.RequireAsync();

        // The value is narrow on purpose. A wide one raises inside the driver before any hook is
        // reached, which is exactly why a narrow one must not pass silently.
        var table = await this.CreateAsync("Decimal64(2)", "1.5");

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString(useCustomDecimals: false));
            await using var reader = await client.ExecuteReaderAsync(
                $"SELECT v FROM {table}",
                null,
                ClickHouseBigDecimal.CreateQueryOptions(),
                TestContext.Current.CancellationToken);
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

            var reading = () => reader.GetValue(0);

            reading.Should().Throw<InvalidOperationException>().WithMessage("*UseCustomDecimals*");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AParameter_IsWrittenAtFullPrecision()
    {
        await server.RequireAsync();

        var mantissa = BigInteger.Pow(10, 70) + BigInteger.One;
        var table = await this.CreateAsync("Decimal256(6)");

        try
        {
            await using var connection = new ClickHouseConnection(Wide());
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand(
                $"INSERT INTO {table} (i, v) VALUES (1, {{v:Decimal256(6)}})");
            command.AddParameter("v", BigDecimal.FromScaled(mantissa, 6));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            var stored = await server.RenderAsync($"SELECT toString(v) FROM {table}");

            stored.Should().Equal(Rendered(mantissa, 6));
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AParameterThisPackageDoesNotMap_StillReachesTheServer()
    {
        await server.RequireAsync();

        // The formatter is consulted for every parameter on the connection, not only ours, and its
        // null has to mean "not mine" rather than "no text" or the registration would break every
        // other parameter type. Read off the driver's own DictionaryParameterFormatter, and
        // measured here.
        var table = $"adapter_{Guid.NewGuid():N}";
        await server.RunAsync(
            $"CREATE TABLE {table} (i UInt32, v Decimal128(4), s String) ENGINE = Memory");

        try
        {
            await using var connection = new ClickHouseConnection(Wide());
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand(
                $"INSERT INTO {table} (i, v, s) VALUES ({{i:UInt32}}, {{v:Decimal128(4)}}, {{s:String}})");
            command.AddParameter("i", 7U);
            command.AddParameter("v", BigDecimal.Parse("1.5", CultureInfo.InvariantCulture));
            command.AddParameter("s", "text");
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            var stored = await server.RenderAsync(
                $"SELECT concat(toString(i), '|', toString(v), '|', s) FROM {table}");

            stored.Should().Equal(["7|1.5|text"], "the other two parameters are untouched by this package");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AParameterInAComparison_MatchesTheStoredValue()
    {
        await server.RequireAsync();

        var mantissa = BigInteger.Pow(10, 70) + BigInteger.One;
        var table = await this.CreateAsync("Decimal256(6)", Rendered(mantissa, 6));

        try
        {
            await using var connection = new ClickHouseConnection(Wide());
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand(
                $"SELECT count() FROM {table} WHERE v = {{v:Decimal256(6)}}");
            command.AddParameter("v", BigDecimal.FromScaled(mantissa, 6));

            var matched = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

            System.Convert.ToInt64(matched, CultureInfo.InvariantCulture).Should().Be(1);
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AMidpoint_RoundsHalfToEvenWhereTheServerAloneTruncates()
    {
        await server.RequireAsync();

        // Written through the package, 0.135 becomes 0.14; parsed by the server itself it becomes
        // 0.13, and so would the driver's own rescale. Both halves are asserted so an agreement by
        // accident is not mistaken for one by design.
        var mapped = await this.CreateAsync("Decimal64(2)");
        var direct = await this.CreateAsync("Decimal64(2)");

        try
        {
            await using var connection = new ClickHouseConnection(Wide());
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand(
                $"INSERT INTO {mapped} (i, v) VALUES (1, {{a:Decimal64(2)}}), (2, {{b:Decimal64(2)}})");
            command.AddParameter("a", BigDecimal.Parse("0.125", CultureInfo.InvariantCulture));
            command.AddParameter("b", BigDecimal.Parse("0.135", CultureInfo.InvariantCulture));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            await server.RunAsync($"INSERT INTO {direct} (i, v) VALUES (1,'0.125'),(2,'0.135')");

            var throughThePackage = await server.RenderAsync($"SELECT toString(v) FROM {mapped} ORDER BY i");
            var throughTheServer = await server.RenderAsync($"SELECT toString(v) FROM {direct} ORDER BY i");

            throughThePackage.Should().Equal("0.12", "0.14");
            throughTheServer.Should().Equal(["0.12", "0.13"], "the server truncates toward zero");
        }
        finally
        {
            await this.DropAsync(mapped);
            await this.DropAsync(direct);
        }
    }

    [Theory]
    [MemberData(nameof(Columns))]
    public async Task TheLargestValueOfEachColumn_Writes(string declared, int precision)
    {
        await server.RequireAsync();

        var scale = Scale(declared);
        var largest = BigInteger.Pow(10, precision) - BigInteger.One;
        var table = await this.CreateAsync(declared);

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());
            await client.InsertBigDecimalAsync(
                table,
                ["i", "v"],
                [[1U, BigDecimal.FromScaled(largest, scale)]],
                null,
                TestContext.Current.CancellationToken);

            var stored = await server.RenderAsync($"SELECT toString(v) FROM {table}");

            stored.Should().Equal(Rendered(largest, scale));
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Theory]
    [MemberData(nameof(Columns))]
    public async Task OneStepBeyondEachColumn_IsRefusedNamingTheColumn(string declared, int precision)
    {
        await server.RequireAsync();

        var scale = Scale(declared);
        var beyond = BigDecimal.FromScaled(BigInteger.Pow(10, precision), scale);
        var table = await this.CreateAsync(declared);

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());

            var writing = async () => await client.InsertBigDecimalAsync(
                table,
                ["i", "v"],
                [[1U, beyond]],
                null,
                TestContext.Current.CancellationToken);

            (await writing.Should().ThrowAsync<Exception>())
                .Which.Should().Match<Exception>(
                    exception => Names(exception, "'v'"),
                    "the failure names the column rather than the value");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task ANonFiniteValue_NeverReachesTheServer()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal64(4)");

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());

            foreach (var value in new[] { BigDecimal.NaN, BigDecimal.PositiveInfinity, BigDecimal.NegativeInfinity })
            {
                var writing = async () => await client.InsertBigDecimalAsync(
                    table,
                    ["i", "v"],
                    [[1U, value]],
                    null,
                    TestContext.Current.CancellationToken);

                var thrown = await writing.Should().ThrowAsync<Exception>();

                // Any exception would satisfy the assertion above, including one from resolving the
                // column types, which would leave the row count trivially zero and the test proving
                // nothing. The refusal has to be the one this package raises.
                thrown.Which.Should().Match<Exception>(
                    exception => Names(exception, "no ClickHouse decimal type represents it"),
                    "the refusal is the codec's, by name");
            }

            var stored = await server.RenderAsync($"SELECT count() FROM {table}");

            stored.Should().Equal(["0"], "nothing was sent");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task ABulkLoad_StoresValuesTheDriversOwnPathCannotCarry()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal256(6)");

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());
            var rows = Enumerable.Range(1, 5)
                .Select(i => new object[] { (uint)i, BigDecimal.FromScaled(BigInteger.Pow(10, 70) + i, 6) })
                .ToList();

            var written = await client.InsertBigDecimalAsync(
                table, ["i", "v"], rows, null, TestContext.Current.CancellationToken);

            written.Should().Be(5);

            var stored = await server.RenderAsync($"SELECT toString(v) FROM {table} ORDER BY i");

            stored.Should().Equal([.. Enumerable.Range(1, 5).Select(i => Rendered(BigInteger.Pow(10, 70) + i, 6))]);
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task ABulkLoadOfAMixedRow_LeavesEveryOtherCellToTheDriver()
    {
        await server.RequireAsync();

        var table = $"adapter_{Guid.NewGuid():N}";
        await server.RunAsync(
            $"CREATE TABLE {table} (i UInt32, v Decimal128(4), s String, n Nullable(Decimal64(2))) ENGINE = Memory");

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());
            await client.InsertBigDecimalAsync(
                table,
                ["i", "v", "s", "n"],
                [[1U, BigDecimal.Parse("1.5", CultureInfo.InvariantCulture), "text", null]],
                null,
                TestContext.Current.CancellationToken);

            var stored = await server.RenderAsync(
                $"SELECT concat(toString(v), '|', s, '|', toString(isNull(n))) FROM {table}");

            stored.Should().Equal("1.5|text|1");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task ADeclaredTypeWithSurroundingSpace_IsRefusedByTheDriverBeforeAnyRow()
    {
        await server.RequireAsync();

        // Recorded because a review asked whether an untrimmed declared type would make a decimal
        // array fall through this package quietly. It cannot: the driver builds its schema from
        // InsertOptions.ColumnTypes before a row is serialised and refuses the string itself.
        var table = $"adapter_{Guid.NewGuid():N}";
        await server.RunAsync($"CREATE TABLE {table} (i UInt32, a Array(Decimal64(4))) ENGINE = Memory");

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());
            var options = new InsertOptions
            {
                ColumnTypes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["i"] = " UInt32 ",
                    ["a"] = " Array(Decimal64(4)) ",
                },
            };

            var writing = async () => await client.InsertBigDecimalAsync(
                table,
                ["i", "a"],
                [[1U, new[] { BigDecimal.Parse("1.5", CultureInfo.InvariantCulture) }]],
                options,
                TestContext.Current.CancellationToken);

            await writing.Should().ThrowAsync<ArgumentException>();

            var stored = await server.RenderAsync($"SELECT count() FROM {table}");

            stored.Should().Equal(["0"], "the refusal comes before anything is written");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AValueAimedAtAColumnThisPackageCannotRead_IsRefusedRatherThanNarrowed()
    {
        await server.RequireAsync();

        // A type constructor around a decimal that this package does not read and the server
        // accepts. Handing the value on would put it through IConvertible and store whatever
        // System.Decimal made of it, which is the one outcome that must never happen quietly.
        var table = $"adapter_{Guid.NewGuid():N}";
        await server.RunAsync(
            $"CREATE TABLE {table} (k UInt32, v SimpleAggregateFunction(max, Decimal64(4))) ENGINE = AggregatingMergeTree ORDER BY k");

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());

            var writing = async () => await client.InsertBigDecimalAsync(
                table,
                ["k", "v"],
                [[1U, BigDecimal.Parse("1.5", CultureInfo.InvariantCulture)]],
                null,
                TestContext.Current.CancellationToken);

            var thrown = await writing.Should().ThrowAsync<Exception>();

            thrown.Which.Should().Match<Exception>(
                exception => Names(exception, "'v'") && Names(exception, "SimpleAggregateFunction"),
                "the refusal names the column and how it is declared");

            var stored = await server.RenderAsync($"SELECT count() FROM {table}");

            stored.Should().Equal(["0"]);
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task ANullableColumnAndAnArrayColumn_KeepTheirShape()
    {
        await server.RequireAsync();

        var table = $"adapter_{Guid.NewGuid():N}";
        await server.RunAsync(
            $"CREATE TABLE {table} (i UInt32, n Nullable(Decimal64(4)), a Array(Decimal64(4)), s String) ENGINE = Memory");
        await server.RunAsync($"INSERT INTO {table} VALUES (1, NULL, ['1.5','2.5'], 'text')");

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());
            await using var reader = await client.ExecuteReaderAsync(
                $"SELECT n, a, s FROM {table}",
                null,
                ClickHouseBigDecimal.CreateQueryOptions(),
                TestContext.Current.CancellationToken);
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

            reader.IsDBNull(0).Should().BeTrue();
            reader.GetNullableBigDecimal(0).Should().BeNull();
            reader.GetValue(1).Should().BeOfType<BigDecimal[]>()
                .Which.Should().Equal(
                    BigDecimal.Parse("1.5", CultureInfo.InvariantCulture),
                    BigDecimal.Parse("2.5", CultureInfo.InvariantCulture));
            reader.GetValue(2).Should().Be("text", "a column this package does not map is untouched");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AValueWrittenAtOneScale_ComesBackAtTheColumns()
    {
        await server.RequireAsync();

        // Not something the package can hide, and not something it should: ClickHouse keeps the
        // scale in the column type and nowhere else.
        var table = await this.CreateAsync("Decimal64(4)");

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());
            await client.InsertBigDecimalAsync(
                table,
                ["i", "v"],
                [[1U, BigDecimal.Parse("1.5", CultureInfo.InvariantCulture)]],
                null,
                TestContext.Current.CancellationToken);

            var read = await ReadOneAsync(client, table);

            read.Scale.Should().Be(4);
            read.Should().Be(BigDecimal.Parse("1.5", CultureInfo.InvariantCulture), "the values are equal");
            read.ToString(null, CultureInfo.InvariantCulture).Should().Be("1.5000");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    [Fact]
    public async Task AnUnmappedRead_NamesTheColumnAndTheRegistration()
    {
        await server.RequireAsync();

        var table = await this.CreateAsync("Decimal64(4)", "1.5");

        try
        {
            using var client = new ClickHouseClient(server.ConnectionString());
            await using var reader = await client.ExecuteReaderAsync(
                $"SELECT v FROM {table}",
                null,
                new QueryOptions(),
                TestContext.Current.CancellationToken);
            (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

            var reading = () => reader.GetBigDecimal(0);

            reading.Should().Throw<InvalidCastException>()
                .WithMessage("*'v'*")
                .WithMessage("*CreateQueryOptions*");
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    /// <summary>Whether an exception, or anything it wraps, carries a phrase.</summary>
    /// <param name="exception">The exception the driver surfaced.</param>
    /// <param name="phrase">What the message should contain.</param>
    /// <returns><see langword="true"/> when the phrase is somewhere in the chain.</returns>
    /// <remarks>
    /// The driver wraps what a serialiser throws, so the assertion is about the chain rather than
    /// about the outermost message.
    /// </remarks>
    private static bool Names(Exception? exception, string phrase)
    {
        for (; exception is not null; exception = exception.InnerException)
        {
            if (exception.Message.Contains(phrase, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the one value of a one-row table through the narrow registration.</summary>
    /// <param name="client">The client.</param>
    /// <param name="table">The table.</param>
    /// <returns>The value.</returns>
    private static async Task<BigDecimal> ReadOneAsync(ClickHouseClient client, string table)
    {
        await using var reader = await client.ExecuteReaderAsync(
            $"SELECT v FROM {table}",
            null,
            ClickHouseBigDecimal.CreateQueryOptions(),
            TestContext.Current.CancellationToken);

        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

        return reader.GetBigDecimal(0);
    }

    /// <summary>The scale a declared column type carries.</summary>
    /// <param name="declared">The column type.</param>
    /// <returns>Its scale.</returns>
    private static int Scale(string declared)
    {
        ClickHouseColumnType.TryParse(declared, out var type).Should().BeTrue();

        return type.Scale;
    }

    /// <summary>
    /// What the server prints for a mantissa at a scale, which is the oracle for every assertion
    /// here.
    /// </summary>
    /// <param name="mantissa">The unscaled value.</param>
    /// <param name="scale">The column's scale.</param>
    /// <returns>The server's own rendering.</returns>
    /// <remarks>
    /// Composed from <see cref="BigInteger"/> rather than from the package, and trimmed the way
    /// ClickHouse trims: <c>toString</c> on a decimal drops trailing zeros and answers a bare
    /// <c>0</c> for zero, which the byte-level suite beside this one established against the
    /// server.
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

    /// <summary>The connection-wide registration, which is what an ADO caller has to use.</summary>
    /// <returns>Settings carrying the mapping.</returns>
    private ClickHouseClientSettings Wide() =>
        new ClickHouseClientSettings(server.ConnectionString()).UseBigDecimal();

    /// <summary>Creates a two-column table, optionally with one row in it.</summary>
    /// <param name="declared">The declared type of the value column.</param>
    /// <param name="literal">A decimal literal to insert, or nothing for an empty table.</param>
    /// <returns>The table's name.</returns>
    private async Task<string> CreateAsync(string declared, string? literal = null)
    {
        var table = $"adapter_{Guid.NewGuid():N}";
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
    /// <remarks>
    /// The fixture documents the same hazard for the same reason: a statement the server refused
    /// can leave the drop failing too, and an exception raised in a finally block replaces the one
    /// that names the defect.
    /// </remarks>
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
