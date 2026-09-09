using System;
using System.Globalization;
using System.Numerics;
using AwesomeAssertions;
using Xunit;
using DriverDecimal = ClickHouse.Driver.Numerics.ClickHouseDecimal;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The conversion pair, which is where the whole contract of the adapter lives.
/// </summary>
/// <remarks>
/// <para>
/// No server takes part. What is asserted here is the rounding, the two boundaries and the
/// refusals, all of which are decided before anything is sent, and the container leg is the slow
/// one. The suite against a live server checks that these outcomes survive the driver.
/// </para>
/// <para>
/// The rounding cases are the point of this file. The driver truncates toward zero when it lowers a
/// scale and so does the server when it parses decimal text, both measured; this type rounds half to
/// even, and the package rescales itself so that neither of the other two ever gets the chance.
/// </para>
/// </remarks>
public sealed class BigDecimalColumnCodecTests
{
    /// <summary>Each width, spelled as a column, with the precision it carries.</summary>
    public static TheoryData<string, int> Columns =>
        new()
        {
            { "Decimal32(4)", 9 },
            { "Decimal64(8)", 18 },
            { "Decimal128(20)", 38 },
            { "Decimal256(40)", 76 },
        };

    /// <summary>
    /// Midpoints at scale 2, with what half to even makes of them. Truncation toward zero, which is
    /// what both the driver and the server do unaided, would give 0.12, 0.13, -0.12 and -0.13.
    /// </summary>
    public static TheoryData<string, string> Midpoints =>
        new()
        {
            { "0.125", "0.12" },
            { "0.135", "0.14" },
            { "-0.125", "-0.12" },
            { "-0.135", "-0.14" },
            { "0.145", "0.14" },
            { "0.155", "0.16" },
        };

    [Theory]
    [MemberData(nameof(Columns))]
    public void AValueAtTheColumnsScale_SurvivesBothDirections(string declared, int precision)
    {
        _ = precision;
        ClickHouseColumnType.TryParse(declared, out var type).Should().BeTrue();

        var value = BigDecimal.FromScaled(BigInteger.Parse("12345", CultureInfo.InvariantCulture), type.Scale);

        var written = BigDecimalColumnCodec.ToColumn(value, type, "v");
        var read = BigDecimalColumnCodec.FromColumn(written);

        read.Should().Be(value);
        written.Scale.Should().Be(type.Scale, "the value is handed over already at the column's scale");
    }

    [Theory]
    [MemberData(nameof(Midpoints))]
    public void AMidpoint_RoundsToEvenRatherThanTowardZero(string literal, string expected)
    {
        ClickHouseColumnType.TryParse("Decimal64(2)", out var type).Should().BeTrue();

        var text = BigDecimalColumnCodec.ToText(BigDecimal.Parse(literal, null), type, "v");

        text.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(Columns))]
    public void TheLargestValueOfTheDeclaredPrecision_Fits(string declared, int precision)
    {
        ClickHouseColumnType.TryParse(declared, out var type).Should().BeTrue();

        var largest = BigInteger.Pow(10, precision) - BigInteger.One;
        var value = BigDecimal.FromScaled(largest, type.Scale);

        var written = BigDecimalColumnCodec.ToColumn(value, type, "v");

        written.Mantissa.Should().Be(largest);
        BigDecimalColumnCodec.FromColumn(written).Should().Be(value);
    }

    [Theory]
    [MemberData(nameof(Columns))]
    public void OneStepBeyondTheDeclaredPrecision_IsRefused(string declared, int precision)
    {
        ClickHouseColumnType.TryParse(declared, out var type).Should().BeTrue();

        var beyond = BigDecimal.FromScaled(BigInteger.Pow(10, precision), type.Scale);

        var writing = () => BigDecimalColumnCodec.ToColumn(beyond, type, "total");

        writing.Should().Throw<OverflowException>()
            .WithMessage("*total*")
            .WithMessage("*precision*");
    }

    [Fact]
    public void AMagnitudeInsideTheWidthAndOutsideThePrecision_NamesThePrecision()
    {
        // Decimal64(2) is eight bytes, holding a magnitude up to about 9.22e18, and eighteen digits,
        // holding one up to 1e18. This mantissa sits between the two, where the server itself
        // answers "Too many digits".
        ClickHouseColumnType.TryParse("Decimal64(2)", out var type).Should().BeTrue();

        var mantissa = BigInteger.Pow(10, 18);
        mantissa.Should().BeLessThan((BigInteger.One << 63) - BigInteger.One, "the value fits the width");

        var writing = () => BigDecimalColumnCodec.ToColumn(BigDecimal.FromScaled(mantissa, 2), type, "total");

        writing.Should().Throw<OverflowException>().WithMessage("*declared precision of 18*");
    }

    [Fact]
    public void AMagnitudeBeyondTheWidth_NamesTheWidth()
    {
        // Decimal(76, 0) is the widest column there is, so the value that overruns it has to come
        // from this type's own range rather than from a larger precision.
        ClickHouseColumnType.TryParse("Decimal256(0)", out var type).Should().BeTrue();

        var beyond = BigDecimal.MaxValue;

        var writing = () => BigDecimalColumnCodec.ToColumn(beyond, type, "total");

        writing.Should().Throw<OverflowException>()
            .WithMessage("*total*")
            .WithMessage("*32 bytes*")
            .WithInnerException<OverflowException>("the codec's own refusal is kept");
    }

    [Theory]
    [InlineData("Decimal32(4)")]
    [InlineData("Decimal64(8)")]
    [InlineData("Decimal128(20)")]
    [InlineData("Decimal256(40)")]
    public void ANonFiniteValue_IsRefusedByName(string declared)
    {
        ClickHouseColumnType.TryParse(declared, out var type).Should().BeTrue();

        foreach (var value in new[] { BigDecimal.NaN, BigDecimal.PositiveInfinity, BigDecimal.NegativeInfinity })
        {
            var writing = () => BigDecimalColumnCodec.ToColumn(value, type, "total");

            writing.Should().Throw<NotSupportedException>()
                .WithMessage("*total*")
                .WithInnerException<NotSupportedException>("the codec names the value and the width");
        }
    }

    [Fact]
    public void AMantissaNoColumnCouldProduce_IsRefusedRatherThanTruncated()
    {
        // Unreachable from a server, which sends at most 256 bits. Reachable from a value composed
        // by hand, and the alternative to raising is silently storing a different number.
        var impossible = new DriverDecimal(BigInteger.One << 256, 0);

        var reading = () => BigDecimalColumnCodec.FromColumn(impossible);

        reading.Should().Throw<OverflowException>();
    }

    [Fact]
    public void TheWidestValueAColumnCanHold_Reads()
    {
        var widest = BigInteger.Pow(10, 76) - BigInteger.One;

        var read = BigDecimalColumnCodec.FromColumn(new DriverDecimal(widest, 0));

        read.Should().Be(BigDecimal.FromScaled(widest, 0));
    }

    [Fact]
    public void TheLongestFractionAColumnCanHold_Reads()
    {
        var mantissa = BigInteger.Pow(10, 76) - BigInteger.One;

        var read = BigDecimalColumnCodec.FromColumn(new DriverDecimal(mantissa, 76));

        read.Scale.Should().Be(76);
        read.Should().Be(BigDecimal.FromScaled(mantissa, 76));
    }

    [Fact]
    public void TheText_IsPlainAndInvariant()
    {
        ClickHouseColumnType.TryParse("Decimal128(4)", out var type).Should().BeTrue();

        BigDecimalColumnCodec.ToText(BigDecimal.Parse("1.5", null), type, "v").Should().Be("1.5000");
        BigDecimalColumnCodec.ToText(BigDecimal.Parse("-1.5", null), type, "v").Should().Be("-1.5000");
        BigDecimalColumnCodec.ToText(BigDecimal.Zero, type, "v").Should().Be("0.0000");
    }

    [Fact]
    public void TheTextOfAScalelessColumn_CarriesNoPoint()
    {
        ClickHouseColumnType.TryParse("Decimal64(0)", out var type).Should().BeTrue();

        BigDecimalColumnCodec.ToText(BigDecimal.Parse("42", null), type, "v").Should().Be("42");
        BigDecimalColumnCodec.ToText(BigDecimal.Parse("-42", null), type, "v").Should().Be("-42");
    }

    [Fact]
    public void AValueShorterThanTheColumn_IsPaddedRatherThanRescaledByTheDriver()
    {
        // Scaling up is exact, but doing it here rather than in the driver is what keeps the value
        // handed over at exactly the column's scale, which is what makes the driver's own rescale
        // unreachable on every path this package owns.
        ClickHouseColumnType.TryParse("Decimal64(4)", out var type).Should().BeTrue();

        var written = BigDecimalColumnCodec.ToColumn(BigDecimal.Parse("1.5", null), type, "v");

        written.Scale.Should().Be(4);
        written.Mantissa.Should().Be(new BigInteger(15000));
    }
}
