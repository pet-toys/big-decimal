using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ClickHouse.Driver;
using PetToys.BigDecimal.Numerics;

namespace BigDecimalProbes;

/// <summary>
/// Publishes trimmed and Native AOT, and answers whether the ClickHouse mapping still works when
/// reflection is gone. The server is the workflow's; the probe only needs a connection string.
/// </summary>
internal static class Program
{
    private const int DeclaredChecks = 3;

    private const string ConnectionVariable = "BIGDECIMAL_PROBE_CLICKHOUSE";

    private const string Table = "bigdecimal_probe";

    private static async Task<int> Main()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine(
                "FAIL clickhouse: " + ConnectionVariable + " is not set. A probe that reached no server " +
                "fails; it does not skip, because a leg that can report success over a probe that " +
                "connected to nothing is not a check.");
            return 1;
        }

        var invariant = CultureInfo.InvariantCulture;

        using var client = new ClickHouseClient(connectionString);

        await Execute(client, "DROP TABLE IF EXISTS " + Table);
        await Execute(
            client,
            "CREATE TABLE " + Table +
            " (i UInt32, wide Decimal256(6), rounded Decimal128(6), scaled Decimal128(18)) ENGINE = Memory");

        var wide = BigDecimal.Parse(WideText, invariant);
        var halfway = BigDecimal.Parse(HalfwayText, invariant);
        var scaled = BigDecimal.Parse(ScaledText, invariant);

        await client.InsertBigDecimalAsync(
            Table,
            ["i", "wide", "rounded", "scaled"],
            [[1u, wide, halfway, scaled]],
            options: null,
            CancellationToken.None);

        // The mapping is asked for by the query, which is the scope the package leads with.
        await using var reader = await client.ExecuteReaderAsync(
            "SELECT wide, rounded, scaled FROM " + Table + " WHERE i = 1",
            null,
            ClickHouseBigDecimal.CreateQueryOptions(),
            CancellationToken.None);

        await reader.ReadAsync(CancellationToken.None);

        Probe.Check("clickhouse.roundtrip", WideText, reader.GetBigDecimal("wide").ToString(invariant));

        // The package rescales before the driver or the server sees the value, and both of those
        // truncate toward zero where this type rounds half to even. Truncation would answer with a
        // 7 in the last place; this expectation is the rounded one, captured from the jitted run.
        Probe.Check("clickhouse.rescale", ExpectedRounded, reader.GetBigDecimal("rounded").ToString(invariant));

        // The scale that comes back is the column's, not the value's, which is the contract the
        // README states rather than a defect: in ClickHouse the scale lives in the column type.
        Probe.Check("clickhouse.columnscale", ExpectedScaled, reader.GetBigDecimal("scaled").ToString(invariant));

        return Probe.Done("clickhouse", DeclaredChecks);
    }

    private static async Task Execute(ClickHouseClient client, string sql) =>
        await client.ExecuteNonQueryAsync(sql, null, new QueryOptions(), CancellationToken.None);

    /// <summary>Wider than <see cref="decimal"/>, inside <c>Decimal256(6)</c>.</summary>
    private const string WideText = "1234567890123456789012345678901234567890123456789012345678901234567890.123456";

    /// <summary>One digit past the column's scale, exactly at the midpoint.</summary>
    private const string HalfwayText = "1.2345675";

    private const string ExpectedRounded = "1.234568";

    private const string ScaledText = "1.5";

    private const string ExpectedScaled = "1.500000000000000000";
}
