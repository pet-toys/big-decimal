using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Reading a declared ClickHouse type, which the package has to do in two spellings.
/// </summary>
/// <remarks>
/// The two paths are handed different ones and neither is a choice we make: a read hook is given
/// the normalised <c>Decimal(38, 10)</c>, and a query annotates a parameter with the width-named
/// <c>Decimal128(10)</c>. Both were observed against the driver, and both have to mean the same
/// column here.
/// </remarks>
public sealed class ClickHouseColumnTypeTests
{
    /// <summary>Each spelling of the four widths, with the precision and width it implies.</summary>
    public static TheoryData<string, int, int, int> Spellings =>
        new()
        {
            { "Decimal32(4)", 9, 4, ClickHouseDecimal.Decimal32Size },
            { "Decimal64(8)", 18, 8, ClickHouseDecimal.Decimal64Size },
            { "Decimal128(20)", 38, 20, ClickHouseDecimal.Decimal128Size },
            { "Decimal256(40)", 76, 40, ClickHouseDecimal.Decimal256Size },
            { "Decimal(9, 4)", 9, 4, ClickHouseDecimal.Decimal32Size },
            { "Decimal(18, 8)", 18, 8, ClickHouseDecimal.Decimal64Size },
            { "Decimal(38, 20)", 38, 20, ClickHouseDecimal.Decimal128Size },
            { "Decimal(76, 40)", 76, 40, ClickHouseDecimal.Decimal256Size },
        };

    /// <summary>Types that are not a decimal column, and malformed ones.</summary>
    public static TheoryData<string?> NotDecimals =>
    [
        (string?)null,
        string.Empty,
        "String",
        "UInt32",
        "Array(Decimal(18, 4))",
        "Decimal",
        "Decimal()",
        "Decimal(18)",
        "Decimal32(4, 2)",
        "Decimal(0, 0)",
        "Decimal(77, 0)",
        "Decimal(18, 19)",
        "Decimal(-1, 0)",
        "Decimal(x, y)",
    ];

    [Theory]
    [MemberData(nameof(Spellings))]
    public void BothSpellings_DescribeTheSameColumn(string declared, int precision, int scale, int width)
    {
        var parsed = ClickHouseColumnType.TryParse(declared, out var type);

        parsed.Should().BeTrue();
        type.Precision.Should().Be(precision);
        type.Scale.Should().Be(scale);
        type.Width.Should().Be(width, "the width follows from the precision rather than being stated");
    }

    [Theory]
    [MemberData(nameof(NotDecimals))]
    public void AnythingElse_IsAnswerNotAnError(string? declared)
    {
        var parsed = ClickHouseColumnType.TryParse(declared, out _);

        parsed.Should().BeFalse();
    }

    [Fact]
    public void ANullableColumn_IsTheColumnUnderIt()
    {
        var parsed = ClickHouseColumnType.TryParse("Nullable(Decimal64(4))", out var type);

        parsed.Should().BeTrue();
        type.Precision.Should().Be(18);
        type.Scale.Should().Be(4);
    }

    [Fact]
    public void SurroundingSpace_IsNotPartOfTheType()
    {
        var parsed = ClickHouseColumnType.TryParse("  Decimal( 38 , 10 )  ", out var type);

        parsed.Should().BeTrue();
        type.Precision.Should().Be(38);
        type.Scale.Should().Be(10);
    }

    [Fact]
    public void AScaleOfZero_IsAScale()
    {
        var parsed = ClickHouseColumnType.TryParse("Decimal256(0)", out var type);

        parsed.Should().BeTrue();
        type.Scale.Should().Be(0);
        type.Declared.Should().Be("Decimal(76, 0)");
    }
}
