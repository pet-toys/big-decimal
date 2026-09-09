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

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The PostgreSQL the wire format is checked against, one per test class that asks for it.
/// </summary>
/// <remarks>
/// <para>
/// The server is the oracle here, so it is pinned by tag and by digest: these tests assert against
/// its own rendering of a value, and an image that moves underneath them moves the expected result
/// with it. Dependabot in this repository covers workflow actions only, so the bump is a deliberate
/// chore rather than something that arrives on its own.
/// </para>
/// <para>
/// It is a class fixture rather than an assembly one, which is how the rest of the fleet scopes a
/// container. A class the category filter removed is never constructed, so a leg that excludes
/// these tests never reaches Docker at all, whatever the runner has. The container is also created
/// and started on first use rather than when the fixture is built, because building one resolves
/// the Docker endpoint and throws where there is none: that keeps a developer without Docker on a
/// skip rather than on a constructor that fails.
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

    private PostgreSqlContainer? container;

    private NpgsqlDataSource? dataSource;

    private readonly SemaphoreSlim gate = new(1, 1);

    private bool attempted;

    private Exception? failure;

    /// <summary>Why the server cannot be used, or <see langword="null"/> when it can.</summary>
    public string? Unavailable { get; private set; }

    /// <summary>Whether the tests that need a server can run.</summary>
    public bool IsAvailable => this.Unavailable is null;

    private static bool DockerIsRequired => Environment.GetEnvironmentVariable("CI") is { Length: > 0 };

    private PostgreSqlContainer Started =>
        this.container ?? throw new InvalidOperationException("The server is used before RequireAsync has started it.");

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
                    // Built here rather than in a field, because Build() resolves the Docker
                    // endpoint: on a machine without one it throws, and a fixture that throws in
                    // its constructor takes every test in the assembly with it, including the ones
                    // that never wanted a server.
                    this.container ??= new PostgreSqlBuilder(Image).Build();
                    await this.container.StartAsync(TestContext.Current.CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // The run is being cancelled, not the container refusing to start. Recording it
                    // as unavailable would latch: every later test would skip, or fail on a runner,
                    // naming a cause that is not the cause. Let the next caller try again - against
                    // this same instance, which is why the build above is conditional: a second one
                    // would overwrite whatever the cancelled attempt had already created, and
                    // nothing would be left holding it.
                    this.attempted = false;

                    throw;
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

    /// <summary>
    /// The connection string for this container, password included.
    /// </summary>
    /// <remarks>
    /// Taken from the container rather than from <see cref="DataSource"/>: Npgsql redacts the
    /// password from a data source's own connection string, so a second data source built from
    /// that one cannot authenticate.
    /// </remarks>
    public string ConnectionString => this.Started.GetConnectionString();

    /// <summary>
    /// A data source over this container with the package's own registration applied, for the
    /// tests that go through the driver rather than around it.
    /// </summary>
    /// <remarks>
    /// Built through <c>UseBigDecimal</c> and not by hand, so that a registration which stopped
    /// taking effect fails the suite rather than being replaced by the suite. It stands beside the
    /// raw <c>COPY</c> helpers rather than replacing them: those travel by a route that converts
    /// nothing, which is what makes a byte assertion mean anything, and this one exists to exercise
    /// the layer that route deliberately bypasses.
    /// </remarks>
    public NpgsqlDataSource DataSource =>
        this.dataSource ??= new NpgsqlDataSourceBuilder(this.ConnectionString)
            .UseBigDecimal()
            .Build();

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        this.gate.Dispose();

        if (this.dataSource is not null)
        {
            await this.dataSource.DisposeAsync();
        }

        // Whatever was built, started or half-started, is disposed. A container that never
        // reached the daemon disposes to nothing, and one a cancelled attempt left behind is the
        // case worth covering.
        if (this.container is not null)
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

        // An empty batch would compose "VALUES )" and come back as a syntax error saying nothing
        // about the caller. A batch of nothing is a test that asserts nothing, which is the failure
        // this layer exists to prevent, so it is refused here rather than at the server.
        if (literals.Count == 0)
        {
            throw new ArgumentException("A batch has to carry at least one value.", nameof(literals));
        }

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

        var payloads = CopyBinaryFrame.ReadRows(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));

        return Same(payloads, literals.Count);
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

            return Same(rendered, payloads.Count);
        }
        finally
        {
            await Drop(connection, table);
        }
    }

    /// <summary>
    /// Checks that the server answered with a row per row it was given, so that a batch which did
    /// not round-trip says so here rather than as an index out of range in a caller's loop.
    /// </summary>
    /// <typeparam name="T">What a row came back as.</typeparam>
    /// <param name="rows">What the server answered.</param>
    /// <param name="expected">How many rows it was given.</param>
    /// <returns>The rows.</returns>
    private static IReadOnlyList<T> Same<T>(IReadOnlyList<T> rows, int expected) =>
        rows.Count == expected
            ? rows
            : throw new InvalidOperationException(
                $"The batch carried {expected} values and the server answered with {rows.Count}.");

    /// <summary>
    /// Drops the table without letting the cleanup speak over the failure that brought us here.
    /// </summary>
    /// <remarks>
    /// A payload the server refuses leaves the connection in a state where this can fail too, and
    /// that is exactly the case where the original exception is the one naming the defect.
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
            // The table is in a container that is thrown away with the assembly.
        }
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(this.Started.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        return connection;
    }
}
