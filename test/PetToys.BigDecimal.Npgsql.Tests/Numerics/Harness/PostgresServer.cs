using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

[assembly: AssemblyFixture(typeof(PetToys.BigDecimal.Numerics.Harness.PostgresServer))]

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The PostgreSQL the wire format is checked against, one per assembly.
/// </summary>
/// <remarks>
/// <para>
/// The server is the oracle here, so it is pinned by tag and by digest: these tests assert against
/// its own rendering of a value, and an image that moves underneath them moves the expected result
/// with it. Dependabot in this repository covers workflow actions only, so the bump is a deliberate
/// chore rather than something that arrives on its own.
/// </para>
/// <para>
/// The container starts on first use rather than when the fixture is built. An assembly fixture is
/// initialised before any test in the assembly runs, so an eager start would demand Docker of every
/// leg that merely happens to include this project - including the macOS and Windows legs, which
/// have no Docker at all and which exclude these tests by category anyway. Starting it from the
/// first test that actually needs a server keeps that cost where the need is.
/// </para>
/// <para>
/// Starting the container is how availability is decided, because Testcontainers exposes no
/// supported probe and the question being asked is whether a container can run here rather than
/// whether a socket exists. A failure is recorded rather than thrown, so that a developer without
/// Docker keeps a green suite; on a continuous integration runner, where <c>CI</c> is set, the same
/// failure is a failed job, because a leg that skips everything would otherwise report success.
/// </para>
/// <para>
/// Every payload crosses through a raw binary <c>COPY</c>, which the driver documents as
/// implementing no encoding or decoding of its own. That is the point: the bytes asserted are the
/// bytes on the wire, and no value handler stands between the codec and the server.
/// </para>
/// </remarks>
public sealed class PostgresServer : IAsyncDisposable
{
    // PostgreSQL 14 is the floor: it is where numeric gained the infinities, and the non-finite
    // cases have nothing to assert against on an older server. The module's own default is
    // postgres:15.1, which is not a version anybody here chose.
    private const string Image =
        "postgres:18-alpine@sha256:d3e1620b530c944afa6e887d22eb899824da68e19c52024bf98f5220c88a65b2";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image).Build();

    private readonly SemaphoreSlim gate = new(1, 1);

    private bool attempted;

    private bool started;

    private Exception? failure;

    /// <summary>Why the server cannot be used, or <see langword="null"/> when it can.</summary>
    public string? Unavailable { get; private set; }

    /// <summary>Whether the tests that need a server can run.</summary>
    public bool IsAvailable => this.Unavailable is null;

    private static bool DockerIsRequired => Environment.GetEnvironmentVariable("CI") is { Length: > 0 };

    /// <summary>
    /// Starts the server if it has not been started, then skips the calling test when it cannot be
    /// used, naming why. On a continuous integration runner the same state is a failure instead: a
    /// leg that skipped every server test would otherwise report success over a suite that ran
    /// nothing.
    /// </summary>
    /// <returns>Nothing; the call either returns, skips the test or fails it.</returns>
    public async ValueTask RequireAsync()
    {
        await this.gate.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            if (!this.attempted)
            {
                this.attempted = true;
                try
                {
                    await this.container.StartAsync(TestContext.Current.CancellationToken);
                    this.started = true;
                }
                catch (Exception exception)
                {
                    this.failure = exception;
                    this.Unavailable =
                        $"PostgreSQL could not be started, so the wire format has nothing to be checked against: {exception.Message}";
                }
            }
        }
        finally
        {
            this.gate.Release();
        }

        if (this.Unavailable is not null && DockerIsRequired)
        {
            throw new InvalidOperationException(this.Unavailable, this.failure);
        }

        Assert.SkipUnless(this.IsAvailable, this.Unavailable ?? string.Empty);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        this.gate.Dispose();
        if (this.started)
        {
            await this.container.DisposeAsync();
        }
    }

    /// <summary>Asks the server for its own binary representation of values it composed itself.</summary>
    /// <param name="literals">Decimal literals, or <c>NaN</c> and the infinities, as the server
    /// would be given them by a person.</param>
    /// <returns>The payloads, in the order the literals were given.</returns>
    /// <remarks>
    /// This is the direction that settles the question. The payloads are the server's, so a
    /// misreading of the layout in our decoder has nothing of ours to agree with.
    /// </remarks>
    public async Task<IReadOnlyList<byte[]>> ExportAsync(IReadOnlyList<string> literals)
    {
        ArgumentNullException.ThrowIfNull(literals);

        var rows = string.Join(
            ",",
            literals.Select((literal, index) =>
                string.Create(CultureInfo.InvariantCulture, $"({index},'{literal}'::numeric)")));

        await using var connection = await this.OpenAsync();
        await using var stream = await connection.BeginRawBinaryCopyAsync(
            $"COPY (SELECT v FROM (VALUES {rows}) AS t(i, v) ORDER BY i) TO STDOUT (FORMAT BINARY)",
            TestContext.Current.CancellationToken);

        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, TestContext.Current.CancellationToken);

        return CopyBinaryFrame.ReadRows(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    /// <summary>Sends payloads to the server and asks it to render what it stored.</summary>
    /// <param name="payloads">The bytes to put in a <c>numeric</c> column, in order.</param>
    /// <returns>The server's own text renderings, in the same order.</returns>
    public async Task<IReadOnlyList<string>> ImportAndRenderAsync(IReadOnlyList<byte[]> payloads)
    {
        var table = $"wire_{Guid.NewGuid():N}";

        await using var connection = await this.OpenAsync();
        await Execute(connection, $"CREATE TABLE {table} (i int, v numeric)");

        try
        {
            await using (var stream = await connection.BeginRawBinaryCopyAsync(
                $"COPY {table} (i, v) FROM STDIN (FORMAT BINARY)",
                TestContext.Current.CancellationToken))
            {
                await stream.WriteAsync(
                    CopyBinaryFrame.IndexedRows(payloads),
                    TestContext.Current.CancellationToken);
            }

            var rendered = new List<string>(payloads.Count);

            await using var command = new NpgsqlCommand($"SELECT v::text FROM {table} ORDER BY i", connection);
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                rendered.Add(reader.GetString(0));
            }

            return rendered;
        }
        finally
        {
            await Execute(connection, $"DROP TABLE IF EXISTS {table}");
        }
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(this.container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        return connection;
    }
}
