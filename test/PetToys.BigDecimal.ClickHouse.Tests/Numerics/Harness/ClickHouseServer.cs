using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Testcontainers.ClickHouse;
using Xunit;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The ClickHouse the wire format is checked against, one per test class that asks for it.
/// </summary>
/// <remarks>
/// <para>
/// The server is the oracle here, so it is pinned by tag and by digest: these tests assert against
/// its own rendering of a value, and an image that moves underneath them moves the expected result
/// with it. Dependabot in this repository covers workflow actions only, so the bump is a deliberate
/// chore rather than something that arrives on its own.
/// </para>
/// <para>
/// Everything goes over the HTTP interface with <c>FORMAT RowBinary</c>, where a <c>Decimal</c> is
/// its underlying integer, little-endian and fixed width, which is exactly what the codec produces.
/// <c>ClickHouse.Driver</c> takes no part: it converts, and what is being checked here is the bytes
/// on the wire. The driver's own mapping belongs to the adapter that will ship it.
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
/// </remarks>
public sealed class ClickHouseServer : IAsyncDisposable
{
    // The module's own default is clickhouse/clickhouse-server:23.6-alpine, which is not a version
    // anybody here chose. Decimal256 and RowBinary predate both, so the floor is nothing in
    // particular and the pin is about holding the oracle still.
    private const string Image =
        "clickhouse/clickhouse-server:25.8-alpine@sha256:87e0a5b72f5465b18eacca7c76850e7ff551c9795c50e451f5646299e5e24146";

    private const string User = "wire";
    private const string Password = "wire";
    private const string Database = "wire";

    /// <summary>The port the HTTP interface listens on inside the container.</summary>
    private const ushort HttpPort = 8123;

    private ClickHouseContainer? container;

    private readonly HttpClient client = new();

    private readonly SemaphoreSlim gate = new(1, 1);

    private bool attempted;

    private Exception? failure;

    /// <summary>Why the server cannot be used, or <see langword="null"/> when it can.</summary>
    public string? Unavailable { get; private set; }

    /// <summary>Whether the tests that need a server can run.</summary>
    public bool IsAvailable => this.Unavailable is null;

    private static bool DockerIsRequired => Environment.GetEnvironmentVariable("CI") is { Length: > 0 };

    private ClickHouseContainer Started =>
        this.container ?? throw new InvalidOperationException("The server is used before RequireAsync has started it.");

    private Uri Endpoint => new(
        $"http://{this.Started.Hostname}:{this.Started.GetMappedPublicPort(HttpPort)}/?user={User}&password={Password}&database={Database}");

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
                    this.container ??= new ClickHouseBuilder(Image)
                        .WithUsername(User)
                        .WithPassword(Password)
                        .WithDatabase(Database)
                        .Build();
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
                        $"ClickHouse could not be started, so the wire format has nothing to be checked against: {exception.Message}";
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
        this.client.Dispose();
        this.gate.Dispose();

        // Whatever was built, started or half-started, is disposed. A container that never
        // reached the daemon disposes to nothing, and one a cancelled attempt left behind is the
        // case worth covering.
        if (this.container is not null)
        {
            await this.container.DisposeAsync();
        }
    }

    /// <summary>Asks the server for its own binary representation of values it composed itself.</summary>
    /// <param name="columnType">The column's declared type, such as <c>Decimal128(20)</c>.</param>
    /// <param name="width">The payload width of that type in bytes.</param>
    /// <param name="literals">Decimal literals, as the server would be given them by a person.</param>
    /// <returns>The payloads, in the order the literals were given.</returns>
    /// <remarks>
    /// This is the direction that settles the question. The payloads are the server's, so a
    /// misreading of the layout in our decoder has nothing of ours to agree with.
    /// </remarks>
    public async Task<IReadOnlyList<byte[]>> ExportAsync(string columnType, int width, IReadOnlyList<string> literals)
    {
        ArgumentNullException.ThrowIfNull(literals);

        // An empty batch would compose "VALUES " and come back as a syntax error saying nothing
        // about the caller. A batch of nothing is a test that asserts nothing, which is the failure
        // this layer exists to prevent, so it is refused here rather than at the server.
        if (literals.Count == 0)
        {
            throw new ArgumentException("A batch has to carry at least one value.", nameof(literals));
        }

        var table = await this.CreateTableAsync(columnType);

        try
        {
            // Quoted, because an unquoted decimal literal is parsed as a Float64 first and would
            // arrive rounded. A string is parsed into the column's own type exactly.
            var rows = string.Join(
                ",",
                literals.Select((literal, index) =>
                    string.Create(CultureInfo.InvariantCulture, $"({index},'{literal}')")));

            await this.ExecuteAsync($"INSERT INTO {table} (i, v) VALUES {rows}");

            var payload = await this.ReadBytesAsync($"SELECT v FROM {table} ORDER BY i FORMAT RowBinary");

            return Split(payload, width, literals.Count);
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    /// <summary>Sends payloads to the server and asks it to render what it stored.</summary>
    /// <param name="columnType">The column's declared type, such as <c>Decimal128(20)</c>.</param>
    /// <param name="payloads">The bytes to put in the column, in order.</param>
    /// <returns>The server's own text renderings, in the same order.</returns>
    public async Task<IReadOnlyList<string>> ImportAndRenderAsync(string columnType, IReadOnlyList<byte[]> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);

        if (payloads.Count == 0)
        {
            throw new ArgumentException("A batch has to carry at least one value.", nameof(payloads));
        }

        var table = await this.CreateTableAsync(columnType);

        try
        {
            // RowBinary has no envelope at all: a row is its fields back to back, and a stream is
            // its rows back to back. The ordinal is the UInt32 in front of each payload.
            var body = new List<byte>();
            for (var index = 0; index < payloads.Count; index++)
            {
                body.AddRange([(byte)index, (byte)(index >> 8), (byte)(index >> 16), (byte)(index >> 24)]);
                body.AddRange(payloads[index]);
            }

            await this.PostAsync($"INSERT INTO {table} (i, v) FORMAT RowBinary", [.. body]);

            var rendered = await this.ReadTextAsync($"SELECT toString(v) FROM {table} ORDER BY i FORMAT TabSeparated");

            return Same(rendered.Split('\n', StringSplitOptions.RemoveEmptyEntries), payloads.Count);
        }
        finally
        {
            await this.DropAsync(table);
        }
    }

    /// <summary>
    /// Checks that the server answered with a row per row it was given, so that a batch which did
    /// not round-trip says so here rather than as an index out of range in a caller's loop.
    /// </summary>
    /// <param name="rows">What the server answered.</param>
    /// <param name="expected">How many rows it was given.</param>
    /// <returns>The rows.</returns>
    private static string[] Same(string[] rows, int expected) =>
        rows.Length == expected
            ? rows
            : throw new InvalidOperationException(
                $"The batch carried {expected} values and the server answered with {rows.Length}.");

    /// <summary>
    /// Drops the table without letting the cleanup speak over the failure that brought us here.
    /// </summary>
    /// <param name="table">The table to drop.</param>
    /// <returns>Nothing, and nothing thrown.</returns>
    private async Task DropAsync(string table)
    {
        try
        {
            await this.ExecuteAsync($"DROP TABLE IF EXISTS {table}");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            // The table is in a container that is thrown away with the assembly.
        }
    }

    private static List<byte[]> Split(byte[] payload, int width, int expected)
    {
        if (payload.Length != expected * width)
        {
            throw new InvalidOperationException(
                $"The batch carried {expected} values, so {expected * width} bytes were expected and the server answered with {payload.Length}.");
        }

        var rows = new List<byte[]>(payload.Length / width);
        for (var offset = 0; offset < payload.Length; offset += width)
        {
            rows.Add(payload.AsSpan(offset, width).ToArray());
        }

        return rows;
    }

    private async Task<string> CreateTableAsync(string columnType)
    {
        var table = $"wire_{Guid.NewGuid():N}";
        await this.ExecuteAsync($"CREATE TABLE {table} (i UInt32, v {columnType}) ENGINE = Memory");

        return table;
    }

    private async Task ExecuteAsync(string statement) => await this.ReadTextAsync(statement);

    private async Task<string> ReadTextAsync(string statement)
    {
        using var response = await this.client.PostAsync(
            this.Endpoint,
            new StringContent(statement),
            TestContext.Current.CancellationToken);

        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ClickHouse refused the statement: {text}");
        }

        return text;
    }

    private async Task<byte[]> ReadBytesAsync(string statement)
    {
        using var response = await this.client.PostAsync(
            this.Endpoint,
            new StringContent(statement),
            TestContext.Current.CancellationToken);

        var payload = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ClickHouse refused the statement: {Encoding.UTF8.GetString(payload)}");
        }

        return payload;
    }

    private async Task PostAsync(string statement, byte[] body)
    {
        using var content = new ByteArrayContent(body);
        using var response = await this.client.PostAsync(
            new Uri($"{this.Endpoint}&query={Uri.EscapeDataString(statement)}"),
            content,
            TestContext.Current.CancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            throw new InvalidOperationException($"ClickHouse refused the payload: {text}");
        }
    }
}
