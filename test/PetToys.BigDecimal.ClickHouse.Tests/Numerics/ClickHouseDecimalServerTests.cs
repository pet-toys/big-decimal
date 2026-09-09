using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The ClickHouse codec against a real ClickHouse, which is the only thing that disproves a
/// misreading of the layout.
/// </summary>
/// <remarks>
/// <para>
/// The core suite checks these bytes against <see cref="BigInteger"/>, whose two's complement round
/// trip is exactly the format. That is a strong oracle for the payload and says nothing about how a
/// server reads it: the scale lives in the column rather than in the bytes, and only a column can
/// say what it does with one. Here the server composes the payload and renders the result.
/// </para>
/// <para>
/// ClickHouse does not render the scale, which is the sharpest difference from PostgreSQL here and
/// was found by this suite rather than assumed. <c>toString</c> trims trailing zeros and prints a
/// bare <c>0</c> for zero, so a column of <c>Decimal64(4)</c> holding 1.5000 renders as <c>1.5</c>
/// and one holding zero renders as <c>0</c>. The scale lives in the column type and nowhere else,
/// so the text is compared against that convention rather than against the padded form a
/// <c>numeric</c> would print.
/// </para>
/// <para>
/// The ranges are not the same on both sides, and the tests below use each one where it applies.
/// The codec's boundary is the two's complement range of the width; ClickHouse's own is a precision
/// of 9, 18, 38 or 76 digits, which is narrower. Values that sit between the two are the adapter's
/// problem to name, not this layer's.
/// </para>
/// </remarks>
/// <param name="server">The server, one per this class, started on first use.</param>
[Trait(TestCategories.TraitName, TestCategories.Integration)]
public sealed class ClickHouseDecimalServerTests(ClickHouseServer server) : IClassFixture<ClickHouseServer>
{
    /// <summary>Each width with the column it is declared as, the scale used here, and the number
    /// of digits ClickHouse itself allows at that width.</summary>
    public static TheoryData<int, int, int> Columns =>
        new()
        {
            { ClickHouseDecimal.Decimal32Size, 4, 9 },
            { ClickHouseDecimal.Decimal64Size, 8, 18 },
            { ClickHouseDecimal.Decimal128Size, 16, 38 },
            { ClickHouseDecimal.Decimal256Size, 32, 76 },
        };

    [Theory]
    [MemberData(nameof(Columns))]
    public async Task TheServersOwnPayload_MatchesTheOracleByteForByte(int width, int scale, int precision)
    {
        await server.RequireAsync();

        var values = Shapes(scale, precision);

        var payloads = await server.ExportAsync(Column(width, scale), width, Literals(values));

        for (var index = 0; index < values.Count; index++)
        {
            Convert.ToHexString(payloads[index])
                .Should().Be(
                    Convert.ToHexString(WireFormatOracle.ClickHouse(values[index].Unscaled, width)),
                    "the server lays {0} out the way the oracle composes it",
                    values[index]);
        }
    }

    [Theory]
    [MemberData(nameof(Columns))]
    public async Task AServerPayload_DecodesToTheValueTheServerWasGiven(int width, int scale, int precision)
    {
        await server.RequireAsync();

        var values = Shapes(scale, precision);

        var payloads = await server.ExportAsync(Column(width, scale), width, Literals(values));

        for (var index = 0; index < values.Count; index++)
        {
            OracleValue.Observe(ClickHouseDecimal.Read(payloads[index], scale))
                .Should().Be(values[index], "the server's own payload for {0} reads back as it", values[index]);
        }
    }

    [Theory]
    [MemberData(nameof(Columns))]
    public async Task AnEncodedValue_IsRenderedBackByTheServerAsItself(int width, int scale, int precision)
    {
        await server.RequireAsync();

        var values = Shapes(scale, precision);

        var rendered = await server.ImportAndRenderAsync(
            Column(width, scale),
            [.. values.Select(value => Encode(value, width))]);

        for (var index = 0; index < values.Count; index++)
        {
            rendered[index]
                .Should().Be(
                    Rendered(values[index]),
                    "the server reads back what the codec wrote for {0}",
                    values[index]);
        }
    }

    [Theory]
    [FuzzData]
    public async Task EveryDrawnValue_SurvivesTheServerInBothDirections(int seed, int cases)
    {
        await server.RequireAsync();

        // Decimal256 only. The narrower widths hold 9, 18 and 38 digits, and a corpus drawn from
        // this type's own range would be filtered down to almost nothing against them; their
        // coverage is the shapes above and the extremes below.
        const int Scale = 32;
        const int Precision = 76;

        var generator = new ValueGenerator(new Random(seed));
        var drawn = new List<FuzzValue>(cases);
        for (var index = 0; index < cases; index++)
        {
            var value = generator.Next(Classes[index % Classes.Length], Scale);
            if (BigInteger.Abs(value.Unscaled) < BigInteger.Pow(10, Precision))
            {
                drawn.Add(value);
            }
        }

        // The filter is what keeps the batch inside what ClickHouse holds, and a batch it emptied
        // would be a corpus test asserting nothing. Say so rather than pass.
        drawn.Should().NotBeEmpty("{0} draws left nothing inside Decimal256's own precision", cases);

        var expected = drawn.Select(OracleValue.From).ToArray();
        var column = Column(ClickHouseDecimal.Decimal256Size, Scale);

        var payloads = await server.ExportAsync(column, ClickHouseDecimal.Decimal256Size, Literals(expected));
        for (var index = 0; index < expected.Length; index++)
        {
            OracleValue.Observe(ClickHouseDecimal.Read(payloads[index], Scale))
                .Should().Be(expected[index], "{0} comes back from the server as itself", FuzzContext.Of(seed, index, drawn[index]));
        }

        var rendered = await server.ImportAndRenderAsync(
            column,
            [.. expected.Select(value => Encode(value, ClickHouseDecimal.Decimal256Size))]);

        for (var index = 0; index < expected.Length; index++)
        {
            rendered[index]
                .Should().Be(
                    Rendered(expected[index]),
                    "the server renders what the codec wrote for {0}",
                    FuzzContext.Of(seed, index, drawn[index]));
        }
    }

    [Theory]
    [MemberData(nameof(Columns))]
    public async Task EachWidth_IsExercisedAtItsOwnExtreme(int width, int scale, int precision)
    {
        await server.RequireAsync();

        var largest = BigInteger.Pow(10, precision) - BigInteger.One;
        OracleValue[] values = [new(largest, scale), new(-largest, scale)];

        var payloads = await server.ExportAsync(Column(width, scale), width, Literals(values));
        for (var index = 0; index < values.Length; index++)
        {
            OracleValue.Observe(ClickHouseDecimal.Read(payloads[index], scale))
                .Should().Be(values[index], "the extreme {0} reads back as itself", values[index]);
        }

        var rendered = await server.ImportAndRenderAsync(
            Column(width, scale),
            [.. values.Select(value => Encode(value, width))]);

        rendered.Should().Equal([.. values.Select(Rendered)]);
    }

    private static ValueClass[] Classes { get; } =
    [
        ValueClass.OneWord,
        ValueClass.TwoWords,
        ValueClass.ThreeWords,
        ValueClass.PowerOfTen,
        ValueClass.TrailingZeros,
        ValueClass.Zero,
    ];

    private static string Column(int width, int scale) => width switch
    {
        ClickHouseDecimal.Decimal32Size => $"Decimal32({scale})",
        ClickHouseDecimal.Decimal64Size => $"Decimal64({scale})",
        ClickHouseDecimal.Decimal128Size => $"Decimal128({scale})",
        _ => $"Decimal256({scale})",
    };

    private static IReadOnlyList<OracleValue> Shapes(int scale, int precision)
    {
        // A digit short of the column's own limit, so that the shape list says nothing about the
        // boundary: that is its own test.
        var large = BigInteger.Pow(10, precision - 1);

        return
        [
            new(BigInteger.Zero, scale),
            new(BigInteger.One, scale),
            new(BigInteger.MinusOne, scale),
            new(BigInteger.Pow(10, scale), scale),
            new(-BigInteger.Pow(10, scale), scale),
            new(large, scale),
            new(-large, scale),
        ];
    }

    /// <summary>
    /// What ClickHouse prints for a value, which is not what the value's own scale would print.
    /// </summary>
    /// <remarks>
    /// Established against the server rather than assumed: <c>toString</c> on a <c>Decimal</c>
    /// trims trailing zeros and answers <c>0</c> for zero, so the column's scale is invisible in
    /// the text. Nothing else here depends on it, and the reading direction is unaffected: a
    /// payload still decodes at the scale the column declares.
    /// </remarks>
    /// <param name="value">The value the column holds.</param>
    /// <returns>The server's own rendering of it.</returns>
    private static string Rendered(OracleValue value)
    {
        var text = value.ToDecimalString();
        if (!text.Contains('.', StringComparison.Ordinal))
        {
            return text;
        }

        var trimmed = text.TrimEnd('0').TrimEnd('.');

        return trimmed is "" or "-" ? "0" : trimmed;
    }

    private static IReadOnlyList<string> Literals(IReadOnlyList<OracleValue> values) =>
        [.. values.Select(value => value.ToDecimalString())];

    private static byte[] Encode(OracleValue value, int width)
    {
        var payload = new byte[width];
        ClickHouseDecimal.Write(BigDecimal.FromScaled(value.Unscaled, value.Scale), value.Scale, payload);

        return payload;
    }
}
