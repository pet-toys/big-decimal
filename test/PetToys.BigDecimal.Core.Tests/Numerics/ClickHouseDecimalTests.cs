using System;
using System.Globalization;
using System.Numerics;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The stated cases of the ClickHouse decimal codec: the four widths, the two's complement layout,
/// the reader's totality, and the two ways a write fails. The property over the corpus lives in
/// <see cref="WireFormatFuzzTests"/>.
/// </summary>
public sealed class ClickHouseDecimalTests
{
    private static readonly int[] Widths =
    [
        ClickHouseDecimal.Decimal32Size,
        ClickHouseDecimal.Decimal64Size,
        ClickHouseDecimal.Decimal128Size,
        ClickHouseDecimal.Decimal256Size,
    ];

    private static BigDecimal Parse(string text) => BigDecimal.Parse(text, CultureInfo.InvariantCulture);

    private static string Text(BigDecimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static byte[] Write(BigDecimal value, int scale, int size)
    {
        var payload = new byte[size];
        ClickHouseDecimal.Write(value, scale, payload);

        return payload;
    }

    [Theory]
    [InlineData(ClickHouseDecimal.Decimal32Size, "01000000")]
    [InlineData(ClickHouseDecimal.Decimal64Size, "0100000000000000")]
    [InlineData(ClickHouseDecimal.Decimal128Size, "01000000000000000000000000000000")]
    public void APositiveValue_IsLittleEndian(int size, string expected) =>
        Convert.ToHexString(Write(Parse("0.01"), 2, size)).Should().Be(expected);

    [Theory]
    [InlineData(ClickHouseDecimal.Decimal32Size, "FFFFFFFF")]
    [InlineData(ClickHouseDecimal.Decimal64Size, "FFFFFFFFFFFFFFFF")]
    [InlineData(ClickHouseDecimal.Decimal128Size, "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")]
    public void ANegativeValue_IsTwosComplement(int size, string expected) =>
        Convert.ToHexString(Write(Parse("-0.01"), 2, size)).Should().Be(expected);

    [Theory]
    [InlineData(ClickHouseDecimal.Decimal32Size)]
    [InlineData(ClickHouseDecimal.Decimal64Size)]
    [InlineData(ClickHouseDecimal.Decimal128Size)]
    [InlineData(ClickHouseDecimal.Decimal256Size)]
    public void EachWidth_RoundTripsAValueAndItsScale(int size)
    {
        var value = Parse("-12.3456");

        var payload = Write(value, 4, size);

        var read = ClickHouseDecimal.Read(payload, 4);
        Text(read).Should().Be("-12.3456");
        read.Scale.Should().Be(4);
    }

    [Theory]
    [InlineData(ClickHouseDecimal.Decimal32Size)]
    [InlineData(ClickHouseDecimal.Decimal64Size)]
    [InlineData(ClickHouseDecimal.Decimal128Size)]
    [InlineData(ClickHouseDecimal.Decimal256Size)]
    public void TheExtremesOfAWidth_DecodeAndNeitherThrows(int size)
    {
        // The reader is total: every two's complement value of every width has a representation
        // here, so there is nothing at the edges for it to refuse.
        var largest = (BigInteger.One << ((size * 8) - 1)) - BigInteger.One;
        var smallest = -(BigInteger.One << ((size * 8) - 1));

        foreach (var scale in (int[])[0, size == ClickHouseDecimal.Decimal32Size ? 9 : 76])
        {
            foreach (var unscaled in (BigInteger[])[largest, smallest])
            {
                var read = ClickHouseDecimal.Read(WireFormatOracle.ClickHouse(unscaled, size), scale);

                OracleValue.Observe(read).Should().Be(new OracleValue(unscaled, scale));
            }
        }
    }

    [Fact]
    public void TheSmallestValueOfAWidth_IsHeldOnlyBecauseItIsNegative()
    {
        // The two's complement range is asymmetric by one, so -2^127 fits Decimal128 and +2^127
        // does not. A codec that checked the magnitude alone would take both or neither.
        var edge = BigInteger.One << 127;

        var written = Write(
            BigDecimal.FromScaled(-edge, 0), 0, ClickHouseDecimal.Decimal128Size);
        Convert.ToHexString(written).Should().Be("00000000000000000000000000000080");

        var refused = () => Write(BigDecimal.FromScaled(edge, 0), 0, ClickHouseDecimal.Decimal128Size);
        refused.Should().Throw<OverflowException>();
    }

    [Fact]
    public void AValueOneUnitPastTheWidth_Overflows()
    {
        var edge = (BigInteger.One << 31) - BigInteger.One;

        Write(BigDecimal.FromScaled(edge, 0), 0, ClickHouseDecimal.Decimal32Size)
            .Should().NotBeNull();

        var refused = () => Write(BigDecimal.FromScaled(edge + 1, 0), 0, ClickHouseDecimal.Decimal32Size);
        refused.Should().Throw<OverflowException>();
    }

    [Fact]
    public void ALowerColumnScale_RoundsHalfToEven()
    {
        // 1.005 to two places: the discarded digit is a five with nothing below it, so the tie is
        // broken by the parity of what stays. 1.00 stays, 1.02 does not become 1.03.
        Convert.ToHexString(Write(Parse("1.005"), 2, ClickHouseDecimal.Decimal32Size))
            .Should().Be("64000000");
        Convert.ToHexString(Write(Parse("1.015"), 2, ClickHouseDecimal.Decimal32Size))
            .Should().Be("66000000");
    }

    [Fact]
    public void AHigherColumnScale_ScalesUpAndCanOverflow()
    {
        Convert.ToHexString(Write(Parse("1.5"), 3, ClickHouseDecimal.Decimal32Size))
            .Should().Be("DC050000");

        // 10 at scale 76 is 1e77. The type holds it, since its magnitude reaches 2^256-1 and that
        // is about 1.157e77, and Decimal256 does not, since its own ceiling is 2^255-1 and that is
        // about 5.79e76. So the scale-up succeeds and the width refuses it.
        var refused = () => Write(Parse("10"), 76, ClickHouseDecimal.Decimal256Size);
        refused.Should().Throw<OverflowException>();
    }

    [Fact]
    public void ANonFiniteValue_IsRefusedForEveryWidthByName()
    {
        BigDecimal[] values = [BigDecimal.NaN, BigDecimal.PositiveInfinity, BigDecimal.NegativeInfinity];
        string[] names = ["NaN", "Infinity", "-Infinity"];

        for (var i = 0; i < values.Length; i++)
        {
            foreach (var size in Widths)
            {
                var refused = () => Write(values[i], 0, size);

                var column = "Decimal" + (size * 8).ToString(CultureInfo.InvariantCulture);

                refused.Should().Throw<NotSupportedException>()
                    .WithMessage(names[i] + "*" + column + "*");
            }
        }
    }

    [Fact]
    public void AWidthTheFormatDoesNotHave_IsRefused()
    {
        var readIt = () => ClickHouseDecimal.Read(new byte[12], 0);
        readIt.Should().Throw<ArgumentOutOfRangeException>();

        var writeIt = () => Write(BigDecimal.One, 0, 12);
        writeIt.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AScaleOutsideTheType_IsRefusedBeforeAnythingIsRead()
    {
        var tooWide = () => ClickHouseDecimal.Read(new byte[8], BigDecimal.MaxScale + 1);
        tooWide.Should().Throw<ArgumentOutOfRangeException>();

        var negative = () => ClickHouseDecimal.Read(new byte[8], -1);
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AZeroPayload_IsZeroAtTheColumnScale()
    {
        var read = ClickHouseDecimal.Read(new byte[ClickHouseDecimal.Decimal64Size], 9);

        read.IsZero.Should().BeTrue();
        read.Scale.Should().Be(9);
    }
}
