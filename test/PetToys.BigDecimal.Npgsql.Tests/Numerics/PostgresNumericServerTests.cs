using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The PostgreSQL codec against a real PostgreSQL, which is the only thing that disproves a
/// misreading of the layout.
/// </summary>
/// <remarks>
/// <para>
/// The core suite checks these bytes against hand-computed vectors and against an oracle that
/// composes the same documented layout independently. Both of those share our reading of the format
/// with the code they check, so a misunderstanding of <c>weight</c> or <c>dscale</c> would be
/// invisible to them. Here the server composes the payload and renders the result, and nothing our
/// codec produced takes part in deciding what is expected.
/// </para>
/// <para>
/// The reading direction is the interesting one for that reason, and it comes first below.
/// </para>
/// </remarks>
/// <param name="server">The server, one per this class, started on first use.</param>
[Trait(TestCategories.TraitName, TestCategories.Integration)]
public sealed class PostgresNumericServerTests(PostgresServer server) : IClassFixture<PostgresServer>
{
    /// <summary>The largest magnitude the type holds: every 77-digit value fits, and this
    /// 78-digit one is the last that does.</summary>
    private static BigInteger MaxMagnitude { get; } = (BigInteger.One << 256) - BigInteger.One;

    [Fact]
    public async Task TheServersOwnPayload_MatchesTheOracleByteForByte()
    {
        await server.RequireAsync();

        // The shapes a person can check, and the ones the byte-level layers were built around: a
        // negative weight, trailing zeros that only dscale carries, and the group boundary.
        OracleValue[] values =
        [
            new(0, 0),
            new(1, 0),
            new(-1, 0),
            new(5, 1),
            new(100, 2),
            new(12345678, 4),
            new(-9999, 0),
            new(10000, 0),
            new(1, 8),
        ];

        var payloads = await server.ExportAsync([.. values.Select(value => value.ToDecimalString())]);

        for (var index = 0; index < values.Length; index++)
        {
            Convert.ToHexString(payloads[index])
                .Should().Be(
                    Convert.ToHexString(WireFormatOracle.Postgres(values[index])),
                    "the server lays {0} out the way the oracle composes it",
                    values[index]);
        }
    }

    [Fact]
    public async Task AServerPayload_DecodesToTheValueTheServerWasGiven()
    {
        await server.RequireAsync();

        OracleValue[] values =
        [
            new(0, 0),
            new(0, 4),
            new(1, 0),
            new(-1, 0),
            new(5, 1),
            new(-5, 1),
            new(100, 2),
            new(1, 255),
            new(MaxMagnitude, 0),
            new(-MaxMagnitude, 0),
            new(12345678, 4),
            new(10000, 4),
        ];

        var payloads = await server.ExportAsync([.. values.Select(value => value.ToDecimalString())]);

        for (var index = 0; index < values.Length; index++)
        {
            OracleValue.Observe(PostgresNumeric.Read(payloads[index]))
                .Should().Be(values[index], "the server's own payload for {0} reads back as it", values[index]);
        }
    }

    [Fact]
    public async Task AnEncodedValue_IsRenderedBackByTheServerAsItself()
    {
        await server.RequireAsync();

        OracleValue[] values =
        [
            new(0, 0),
            new(0, 4),
            new(1, 0),
            new(-1, 0),
            new(5, 1),
            new(-5, 1),
            new(100, 2),
            new(1, 255),
            new(MaxMagnitude, 0),
            new(-MaxMagnitude, 0),
            new(12345678, 4),
            new(10000, 4),
        ];

        var rendered = await server.ImportAndRenderAsync([.. values.Select(Encode)]);

        for (var index = 0; index < values.Length; index++)
        {
            rendered[index]
                .Should().Be(values[index].ToDecimalString(), "the server reads back what the codec wrote for {0}", values[index]);
        }
    }

    [Theory]
    [FuzzData]
    public async Task EveryDrawnValue_SurvivesTheServerInBothDirections(int seed, int cases)
    {
        await server.RequireAsync();

        var generator = new ValueGenerator(new Random(seed));
        var drawn = new List<FuzzValue>(cases);
        for (var index = 0; index < cases; index++)
        {
            drawn.Add(generator.Next());
        }

        var expected = drawn.Select(OracleValue.From).ToArray();

        var payloads = await server.ExportAsync([.. expected.Select(value => value.ToDecimalString())]);
        for (var index = 0; index < expected.Length; index++)
        {
            OracleValue.Observe(PostgresNumeric.Read(payloads[index]))
                .Should().Be(expected[index], "{0} comes back from the server as itself", FuzzContext.Of(seed, index, drawn[index]));
        }

        var rendered = await server.ImportAndRenderAsync([.. expected.Select(Encode)]);
        for (var index = 0; index < expected.Length; index++)
        {
            rendered[index]
                .Should().Be(
                    expected[index].ToDecimalString(),
                    "the server renders what the codec wrote for {0}",
                    FuzzContext.Of(seed, index, drawn[index]));
        }
    }

    [Fact]
    public async Task TheThreeNonFiniteValues_CrossAsThemselves()
    {
        await server.RequireAsync();

        string[] literals = ["NaN", "Infinity", "-Infinity"];

        var payloads = await server.ExportAsync(literals);

        PostgresNumeric.Read(payloads[0]).Should().Match<BigDecimal>(value => BigDecimal.IsNaN(value));
        PostgresNumeric.Read(payloads[1]).Should().Be(BigDecimal.PositiveInfinity);
        PostgresNumeric.Read(payloads[2]).Should().Be(BigDecimal.NegativeInfinity);

        var rendered = await server.ImportAndRenderAsync(
            [Encode(BigDecimal.NaN), Encode(BigDecimal.PositiveInfinity), Encode(BigDecimal.NegativeInfinity)]);

        rendered.Should().Equal(literals);
    }

    [Fact]
    public async Task AValueTheServerHoldsAndTheTypeCannot_OverflowsOnDecode()
    {
        await server.RequireAsync();

        // The boundary is exact and both sides of it come from the server: PostgreSQL holds 131072
        // integer digits, so it composes the payload one unit past our largest magnitude without
        // any help from us.
        var payloads = await server.ExportAsync(
        [
            MaxMagnitude.ToString(CultureInfo.InvariantCulture),
            (MaxMagnitude + BigInteger.One).ToString(CultureInfo.InvariantCulture),
        ]);

        OracleValue.Observe(PostgresNumeric.Read(payloads[0])).Should().Be(new OracleValue(MaxMagnitude, 0));

        var beyond = () => PostgresNumeric.Read(payloads[1]);

        beyond.Should().Throw<OverflowException>();
    }

    [Fact]
    public async Task AFractionLongerThanTheScaleCanCarry_IsRoundedOnce()
    {
        await server.RequireAsync();

        // 260 fractional digits, five past the scale the type carries, with the digits given up
        // sitting exactly on a tie so that half to even is distinguishable from anything else.
        var fraction = string.Concat(new string('1', 255), "50000");
        var literal = "0." + fraction;

        var payload = (await server.ExportAsync([literal]))[0];

        var (unscaled, exactScale) = WireFormatOracle.PostgresExact(payload);
        var displayScale = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(6));
        var expected = WireFormatOracle.PostgresExpected(unscaled, exactScale, displayScale);

        OracleValue.Observe(PostgresNumeric.Read(payload)).Should().Be(expected);
    }

    private static byte[] Encode(OracleValue value) =>
        Encode(BigDecimal.FromScaled(value.Unscaled, value.Scale));

    private static byte[] Encode(BigDecimal value)
    {
        var payload = new byte[PostgresNumeric.MaxByteCount];
        PostgresNumeric.TryWrite(value, payload, out var written).Should().BeTrue();

        return payload.AsSpan(0, written).ToArray();
    }
}
