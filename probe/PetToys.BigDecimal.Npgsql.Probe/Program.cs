using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Npgsql;
using PetToys.BigDecimal.Numerics;

namespace BigDecimalProbes;

/// <summary>
/// Publishes trimmed and Native AOT, and answers whether the PostgreSQL mapping still works when
/// reflection is gone. The server is the workflow's, not this process's: a container library
/// inside the binary would bring a reflection-heavy closure into the thing being measured.
/// </summary>
internal static class Program
{
    private const int DeclaredChecks = 3;

    private const string ConnectionVariable = "BIGDECIMAL_PROBE_POSTGRES";

    private static async Task<int> Main()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.Error.WriteLine(
                "FAIL npgsql: " + ConnectionVariable + " is not set. A probe that reached no server fails; " +
                "it does not skip, because a leg that can report success over a probe that connected to " +
                "nothing is not a check.");
            return 1;
        }

        var invariant = CultureInfo.InvariantCulture;

        // Which builder a consumer must use is decided by the publish, not by taste, so the probe
        // decides it the same way the runtime does. Native AOT reports no dynamic code, and there
        // the slim builder is Npgsql's documented route; a trimmed build still supports dynamic
        // code and takes the full one, so one leg covers both overloads of UseBigDecimal.
        var dynamicCode = RuntimeFeature.IsDynamicCodeSupported;
        Console.WriteLine(
            "route: " + (dynamicCode ? "NpgsqlDataSourceBuilder" : "NpgsqlSlimDataSourceBuilder") +
            " (IsDynamicCodeSupported=" + dynamicCode.ToString(invariant) + ")");

        await using var source = Build(connectionString, dynamicCode);

        await Execute(source, "DROP TABLE IF EXISTS bigdecimal_probe");
        await Execute(
            source,
            "CREATE TABLE bigdecimal_probe (id int PRIMARY KEY, total numeric(58, 18), totals numeric[])");

        var wide = BigDecimal.Parse(WideText, invariant);
        var zeros = BigDecimal.Parse(TrailingZerosText, invariant);

        await using (var insert = source.CreateCommand(
            "INSERT INTO bigdecimal_probe (id, total, totals) VALUES (1, $1, $2)"))
        {
            insert.Parameters.Add(new NpgsqlParameter { Value = wide });
            insert.Parameters.Add(new NpgsqlParameter { Value = new[] { wide, zeros } });
            await insert.ExecuteNonQueryAsync();
        }

        await using var command = source.CreateCommand("SELECT total, totals FROM bigdecimal_probe WHERE id = 1");
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        var total = reader.GetBigDecimal("total");
        Probe.Check("npgsql.roundtrip", WideText, total.ToString(invariant));
        Probe.Check("npgsql.scale", ExpectedScale, total.Scale.ToString(invariant));

        var totals = reader.GetFieldValue<BigDecimal[]>(1);
        Probe.Check(
            "npgsql.array",
            WideText + ";" + TrailingZerosText,
            totals[0].ToString(invariant) + ";" + totals[1].ToString(invariant));

        return Probe.Done("npgsql", DeclaredChecks);
    }

    private static NpgsqlDataSource Build(string connectionString, bool dynamicCode)
    {
        if (dynamicCode)
        {
            return new NpgsqlDataSourceBuilder(connectionString).UseBigDecimal().Build();
        }

        // The slim builder starts with nothing enabled, so the array handler has to be asked for.
        // That is the trade it exists to offer, and a probe that only read scalars would not show
        // it: an application meeting numeric[] under Native AOT needs this line.
        var slim = new NpgsqlSlimDataSourceBuilder(connectionString);
        slim.EnableArrays();
        return slim.UseBigDecimal().Build();
    }

    private static async Task Execute(NpgsqlDataSource source, string sql)
    {
        await using var command = source.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A value with more digits than <see cref="decimal"/> can hold, in both parts.</summary>
    private const string WideText = "12345678901234567890123456789012345678.123456789012345678";

    private const string ExpectedScale = "18";

    /// <summary>Trailing zeros the display scale carries and the mapping must not drop.</summary>
    private const string TrailingZerosText = "1.500000000000000000";
}
