using System;
using System.Globalization;
using System.Numerics;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The stated cases of the PostgreSQL <c>numeric</c> codec. The byte sequences here are worked out
/// from the documented layout rather than taken from an observed output; each one carries its
/// derivation. The property over the corpus lives in <see cref="WireFormatFuzzTests"/>.
/// </summary>
public sealed class PostgresNumericTests
{
    private static BigDecimal Parse(string text) => BigDecimal.Parse(text, CultureInfo.InvariantCulture);

    private static string Text(BigDecimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Write(BigDecimal value)
    {
        var payload = new byte[PostgresNumeric.MaxByteCount];
        PostgresNumeric.TryWrite(value, payload, out var written).Should().BeTrue();
        written.Should().Be(PostgresNumeric.GetByteCount(value));

        return Convert.ToHexString(payload.AsSpan(0, written));
    }

    // ndigits, weight, sign, dscale, then the groups. Every expectation below reads left to right
    // in those fields.
    [Theory]

    // Zero carries no groups at all: the scale lives in dscale and there is nothing to strip to.
    [InlineData("0", "0000" + "0000" + "0000" + "0000")]

    // One is a single group of 1 at weight 0, dscale 0.
    [InlineData("1", "0001" + "0000" + "0000" + "0000" + "0001")]

    // The magnitude is unchanged by the sign; only the sign word moves, to 0x4000.
    [InlineData("-1", "0001" + "0000" + "4000" + "0000" + "0001")]

    // 0.5 at scale 1 pads by three digits to 5000 and sits one group below the point, so the
    // weight is -1 and the single group is 5000 = 0x1388.
    [InlineData("0.5", "0001" + "FFFF" + "0000" + "0001" + "1388")]

    // 1.00 pads by two to 10000, which is one group of 1 at weight 0 and a group of zero below it.
    // The zero group is stripped and dscale carries the two trailing zeros instead.
    [InlineData("1.00", "0001" + "0000" + "0000" + "0002" + "0001")]

    // 1234.5678 is exactly two groups on the grid: 1234 = 0x04D2 above the point, 5678 = 0x162E
    // below it, so the weight is 0 and dscale is 4.
    [InlineData("1234.5678", "0002" + "0000" + "0000" + "0004" + "04D2" + "162E")]
    public void AValue_IsWrittenToTheDocumentedLayout(string text, string expected) =>
        Write(Parse(text)).Should().Be(expected);

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("-1")]
    [InlineData("0.5")]
    [InlineData("1.00")]
    [InlineData("1234.5678")]
    [InlineData("-0.000000000000000000000001")]
    public void AValue_RoundTripsWithItsScale(string text)
    {
        var value = Parse(text);
        var payload = new byte[PostgresNumeric.MaxByteCount];
        PostgresNumeric.TryWrite(value, payload, out var written);

        var read = PostgresNumeric.Read(payload.AsSpan(0, written));

        Text(read).Should().Be(text);
        read.Scale.Should().Be(value.Scale);
    }

    [Theory]
    [InlineData("NaN", "0000" + "0000" + "C000" + "0000")]
    [InlineData("Infinity", "0000" + "0000" + "D000" + "0000")]
    [InlineData("-Infinity", "0000" + "0000" + "F000" + "0000")]
    public void ANonFiniteValue_IsTheHeaderAloneWithItsSignCode(string text, string expected)
    {
        var value = Parse(text);

        Write(value).Should().Be(expected);
        PostgresNumeric.GetByteCount(value).Should().Be(PostgresNumeric.HeaderSize);

        var read = PostgresNumeric.Read(Convert.FromHexString(expected));
        Text(read).Should().Be(text);
    }

    [Fact]
    public void TheSizeIsAnsweredBeforeTheWrite()
    {
        var value = Parse("1234.5678");
        var need = PostgresNumeric.GetByteCount(value);

        need.Should().Be(12);
        PostgresNumeric.TryWrite(value, new byte[need], out var written).Should().BeTrue();
        written.Should().Be(need);
    }

    [Fact]
    public void ADestinationOneByteShort_IsRefusedRatherThanThrowing()
    {
        var value = Parse("1234.5678");
        var need = PostgresNumeric.GetByteCount(value);

        PostgresNumeric.TryWrite(value, new byte[need - 1], out var written).Should().BeFalse();
        written.Should().Be(0);
    }

    [Fact]
    public void EveryValueOfTheType_CanBeWritten()
    {
        // The claim is that writing has no failure mode of its own, so the widest and the narrowest
        // values of the type are written rather than asserted about.
        BigDecimal[] extremes =
        [
            BigDecimal.MaxValue,
            BigDecimal.MinValue,
            BigDecimal.Zero,
            BigDecimal.FromScaled(BigInteger.One, BigDecimal.MaxScale),
        ];

        foreach (var value in extremes)
        {
            var payload = new byte[PostgresNumeric.MaxByteCount];
            PostgresNumeric.TryWrite(value, payload, out var written).Should().BeTrue();
            written.Should().BeLessThanOrEqualTo(PostgresNumeric.MaxByteCount);

            OracleValue.Observe(PostgresNumeric.Read(payload.AsSpan(0, written)))
                .Should().Be(OracleValue.Observe(value));
        }
    }

    [Fact]
    public void AnIntegerPartBeyondTheMagnitude_Overflows()
    {
        var refused = () => PostgresNumeric.Read(Payload(BigInteger.Pow(10, 199), 0));

        refused.Should().Throw<OverflowException>();
    }

    [Fact]
    public void PostgreSqlsOwnLimitOfIntegerDigits_IsRefusedWithoutBeingSizedFrom()
    {
        // 32767 groups is 131068 integer digits: the widest payload the format can express at all,
        // since ndigits is a signed 16-bit field and PostgreSQL's own limit of 131072 digits is
        // four digits past what it can count. A 64 KB payload, for a value the type has no
        // representation of at any scale.
        var payload = Groups(count: 32767, weight: 32766, dscale: 0, group: 1);

        var refused = () => PostgresNumeric.Read(payload);

        refused.Should().Throw<OverflowException>();
    }

    [Fact]
    public void PostgreSqlsOwnLimitOfFractionalDigits_IsReadAndRounded()
    {
        // A 16383-digit fraction, the other of PostgreSQL's limits, is 4096 groups entirely below
        // the point. Nothing about it is refused: a fraction is what this type gives up, so the
        // whole payload is consumed and rounded once into the scale that fits.
        var payload = Groups(count: 4096, weight: -1, dscale: 16383, group: 1);

        var exact = WireFormatOracle.PostgresExact(payload);
        var expected = WireFormatOracle.PostgresExpected(exact.Unscaled, exact.Scale, 16383);

        var read = PostgresNumeric.Read(payload);

        OracleValue.Observe(read).Should().Be(expected);

        // 81, not 255. The value is a shade above 1e-4, so 81 fractional digits is already 78
        // significant ones and the mantissa binds long before the scale cap does.
        read.Scale.Should().Be(81);
    }

    [Fact]
    public void TheOverflowEdgeIsExact()
    {
        var largest = (BigInteger.One << 256) - BigInteger.One;

        OracleValue.Observe(PostgresNumeric.Read(Payload(largest, 0)))
            .Should().Be(new OracleValue(largest, 0));

        var refused = () => PostgresNumeric.Read(Payload(largest + 1, 0));
        refused.Should().Throw<OverflowException>();
    }

    [Fact]
    public void AFractionPastTheMantissa_RoundsHalfToEvenInOneStep()
    {
        // 79 significant digits below the point, the last of them a five with nothing under it.
        // The result keeps 78, which is what the magnitude holds in this range, and the tie is
        // broken by the parity of the digit that stays, so the two cases differ only in that digit.
        var even = BigInteger.Pow(10, 78) + (2 * 10) + 5;
        var odd = BigInteger.Pow(10, 78) + (3 * 10) + 5;

        OracleValue.Observe(PostgresNumeric.Read(Payload(even, 79)))
            .Should().Be(WireFormatOracle.PostgresExpected(even, 79, 79));
        OracleValue.Observe(PostgresNumeric.Read(Payload(odd, 79)))
            .Should().Be(WireFormatOracle.PostgresExpected(odd, 79, 79));

        // Stated as digits as well as against the oracle, so that a change to both at once is
        // still visible: the even case stays and the odd case carries.
        OracleValue.Observe(PostgresNumeric.Read(Payload(even, 79)))
            .Should().Be(new OracleValue(BigInteger.Pow(10, 77) + 2, 78));
        OracleValue.Observe(PostgresNumeric.Read(Payload(odd, 79)))
            .Should().Be(new OracleValue(BigInteger.Pow(10, 77) + 4, 78));
    }

    [Fact]
    public void ADisplayScalePastTheMaximum_IsCapped()
    {
        var read = PostgresNumeric.Read(Payload(BigInteger.One, 300));

        read.IsZero.Should().BeTrue();
        read.Scale.Should().Be(BigDecimal.MaxScale);
    }

    [Theory]
    [InlineData("00000000")]
    [InlineData("0001" + "0000" + "0000" + "0000")]
    [InlineData("0000" + "0000" + "1234" + "0000")]
    [InlineData("0001" + "0000" + "0000" + "0000" + "2710")]
    [InlineData("0001" + "0064" + "0000" + "0000" + "2710")]
    [InlineData("FFFF" + "0000" + "0000" + "0000")]
    [InlineData("0000" + "0000" + "C000" + "0000" + "0001")]
    public void AMalformedPayload_IsRefusedAsOne(string hex)
    {
        var refused = () => PostgresNumeric.Read(Convert.FromHexString(hex));

        refused.Should().Throw<FormatException>();
    }

    [Fact]
    public void ALeadingZeroGroup_DoesNotMoveTheLeadingDigit()
    {
        // The server strips them; a payload from anywhere else may not have, and a reader that
        // counted from the first group rather than the first significant one would place the value
        // 10000 times too high.
        var stripped = Convert.FromHexString("0001" + "0000" + "0000" + "0000" + "0001");
        var padded = Convert.FromHexString("0002" + "0001" + "0000" + "0000" + "0000" + "0001");

        OracleValue.Observe(PostgresNumeric.Read(padded))
            .Should().Be(OracleValue.Observe(PostgresNumeric.Read(stripped)));
    }

    private static byte[] Payload(BigInteger unscaled, int scale) =>
        WireFormatOracle.Postgres(new OracleValue(unscaled, scale));

    /// <summary>
    /// Lays out a payload of a given group count directly, without going through a
    /// <see cref="BigInteger"/> of the same size. PostgreSQL's own limits are tens of thousands of
    /// groups, and decomposing a number that wide costs more than the case is worth.
    /// </summary>
    private static byte[] Groups(int count, int weight, int dscale, ushort group)
    {
        var groups = new ushort[count];
        Array.Fill(groups, group);

        return WireFormatOracle.PostgresLayout(
            count, weight, WireFormatOracle.PostgresPositive, dscale, groups);
    }
}
