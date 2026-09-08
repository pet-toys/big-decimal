using System;
using System.Globalization;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Checks both wire codecs over randomised values: the bytes against an independent oracle, and the
/// value against itself after a round trip, scale included.
/// </summary>
public sealed class WireFormatFuzzTests
{
    private static readonly int[] Widths =
    [
        ClickHouseDecimal.Decimal32Size,
        ClickHouseDecimal.Decimal64Size,
        ClickHouseDecimal.Decimal128Size,
        ClickHouseDecimal.Decimal256Size,
    ];

    [Theory]
    [FuzzData]
    public void PostgresBytes_MatchTheOracle(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));
        var payload = new byte[PostgresNumeric.MaxByteCount];

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var context = FuzzContext.Of(seed, index, drawn);
            var expected = WireFormatOracle.Postgres(OracleValue.From(drawn));

            PostgresNumeric.GetByteCount(drawn.Value)
                .Should().Be(expected.Length, "{0} needs the bytes the layout calls for", context);
            PostgresNumeric.TryWrite(drawn.Value, payload, out var written)
                .Should().BeTrue("{0} fits MaxByteCount", context);

            Convert.ToHexString(payload.AsSpan(0, written))
                .Should().Be(Convert.ToHexString(expected), "{0} is written as the layout says", context);
        }
    }

    [Theory]
    [FuzzData]
    public void Postgres_RoundTripsEveryValue(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));
        var payload = new byte[PostgresNumeric.MaxByteCount];

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var context = FuzzContext.Of(seed, index, drawn);

            PostgresNumeric.TryWrite(drawn.Value, payload, out var written);

            OracleValue.Observe(PostgresNumeric.Read(payload.AsSpan(0, written)))
                .Should().Be(OracleValue.From(drawn), "{0} comes back as itself", context);
        }
    }

    [Theory]
    [FuzzData]
    public void ClickHouse_MatchesTheOracleAtEveryWidth(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var context = FuzzContext.Of(seed, index, drawn);

            foreach (var size in Widths)
            {
                byte[] expected;
                try
                {
                    // The column carries the value's own scale, so nothing is rescaled here and the
                    // only question the width asks is whether the unscaled value fits it.
                    expected = WireFormatOracle.ClickHouse(drawn.Unscaled, size);
                }
                catch (OverflowException)
                {
                    var refused = () => Write(drawn.Value, drawn.Scale, size);
                    refused.Should().Throw<OverflowException>(
                        "{0} is past {1} bytes, as the oracle says", context, size);
                    continue;
                }

                var payload = Write(drawn.Value, drawn.Scale, size);

                Convert.ToHexString(payload)
                    .Should().Be(Convert.ToHexString(expected), "{0} is two's complement", context);
                OracleValue.Observe(ClickHouseDecimal.Read(payload, drawn.Scale))
                    .Should().Be(OracleValue.From(drawn), "{0} comes back as itself", context);
            }
        }
    }

    [Theory]
    [FuzzData]
    public void ClickHouseWriting_RescalesOnceIntoTheColumn(int seed, int cases)
    {
        // The round-trip above writes at the value's own scale, so neither rescaling path is on
        // it: rounding down into a narrower column and scaling up into a wider one are the two
        // places the writer can be wrong about a value it accepts. Here the column's scale is
        // drawn on its own, and the oracle rounds once from the mantissa the value arrived with.
        var generator = new ValueGenerator(new Random(seed));
        var random = new Random(seed);

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var context = FuzzContext.Of(seed, index, drawn);
            var size = Widths[random.Next(Widths.Length)];

            // 0 to 76 is the range of a ClickHouse column, whichever width declares it.
            var column = random.Next(0, 77);
            var rescaled = WireFormatOracle.Rescale(drawn.Unscaled, drawn.Scale, column);

            byte[] expected;
            try
            {
                expected = WireFormatOracle.ClickHouse(rescaled, size);
            }
            catch (OverflowException)
            {
                var refused = () => Write(drawn.Value, column, size);
                refused.Should().Throw<OverflowException>(
                    "{0} at column scale {1} is past {2} bytes, as the oracle says",
                    context,
                    column,
                    size);
                continue;
            }

            Convert.ToHexString(Write(drawn.Value, column, size))
                .Should().Be(
                    Convert.ToHexString(expected),
                    "{0} is rounded once into column scale {1}",
                    context,
                    column);
        }
    }

    [Theory]
    [FuzzData]
    public void PostgresReading_RoundsOnceIntoTheScaleThatFits(int seed, int cases)
    {
        // Payloads rather than values. A payload this codec wrote is never wider than the type,
        // so the round trip cannot reach the reader's two hard branches - the overflow it decides
        // from the header, and the fraction it rounds away - and the stated cases reach them at
        // two points. These are composed from the layout instead, wide enough that the tail below
        // the accumulator has to be carried as a sticky bit rather than as digits.
        var random = new Random(seed);

        for (var index = 0; index < cases; index++)
        {
            var groups = new ushort[random.Next(1, 41)];
            for (var j = 0; j < groups.Length; j++)
            {
                groups[j] = (ushort)random.Next(0, 10_000);
            }

            var weight = random.Next(-30, 31);
            var dscale = random.Next(0, 301);
            var sign = random.Next(2) == 0
                ? WireFormatOracle.PostgresNegative
                : WireFormatOracle.PostgresPositive;

            var payload = WireFormatOracle.PostgresLayout(groups.Length, weight, sign, dscale, groups);
            var exact = WireFormatOracle.PostgresExact(payload);
            var because = string.Create(
                CultureInfo.InvariantCulture,
                $"seed {seed} case {index}: {groups.Length} groups at weight {weight}, dscale {dscale}");

            OracleValue expected;
            try
            {
                expected = WireFormatOracle.PostgresExpected(exact.Unscaled, exact.Scale, dscale);
            }
            catch (OverflowException)
            {
                var refused = () => PostgresNumeric.Read(payload);
                refused.Should().Throw<OverflowException>("{0} is past the magnitude", because);
                continue;
            }

            OracleValue.Observe(PostgresNumeric.Read(payload))
                .Should().Be(expected, "{0} rounds once into the scale that fits", because);
        }
    }

    [Theory]
    [FuzzData]
    public void ClickHouseDecoding_IsTotal(int seed, int cases)
    {
        // The reader's claim is that it has no failure mode, so the cases here are payloads rather
        // than values: any byte sequence of any width at any scale the type accepts.
        var random = new Random(seed);
        var payload = new byte[ClickHouseDecimal.Decimal256Size];

        for (var index = 0; index < cases; index++)
        {
            var size = Widths[random.Next(Widths.Length)];
            var scale = random.Next(0, BigDecimal.MaxScale + 1);
            random.NextBytes(payload.AsSpan(0, size));

            var expected = WireFormatOracle.ClickHouseValue(payload.AsSpan(0, size));

            OracleValue.Observe(ClickHouseDecimal.Read(payload.AsSpan(0, size), scale))
                .Should().Be(
                    new OracleValue(expected, scale),
                    "seed {0} case {1}: a {2}-byte payload at scale {3} decodes exactly",
                    seed,
                    index,
                    size,
                    scale);
        }
    }

    private static byte[] Write(BigDecimal value, int scale, int size)
    {
        var payload = new byte[size];
        ClickHouseDecimal.Write(value, scale, payload);

        return payload;
    }
}
