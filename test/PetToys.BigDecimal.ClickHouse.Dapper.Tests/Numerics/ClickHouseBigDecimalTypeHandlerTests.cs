using System;
using System.Globalization;
using System.Numerics;
using AwesomeAssertions;
using ClickHouse.Driver.ADO.Parameters;
using Dapper;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;
using DriverDecimal = ClickHouse.Driver.Numerics.ClickHouseDecimal;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The two handlers on their own, over each CLR type Dapper can hand them and the ones they refuse.
/// </summary>
/// <remarks>
/// These need no server: what a column produced is the driver's business, and what these types do
/// with what they were handed is this package's.
/// </remarks>
public sealed class ClickHouseBigDecimalTypeHandlerTests
{
    private static ClickHouseBigDecimalTypeHandler Handler { get; } = new();

    private static ClickHouseBigDecimalArrayTypeHandler ArrayHandler { get; } = new();

    /// <summary>One value of each CLR type a ClickHouse integer column decodes into.</summary>
    /// <remarks>
    /// Measured against the server rather than taken from the driver's documentation: an unsigned
    /// column decodes to the unsigned CLR type, and Int128, Int256 and UInt256 all arrive as
    /// <see cref="BigInteger"/>. None of these is what a PostgreSQL driver produces, which is why
    /// the set differs from the sibling package's.
    /// </remarks>
    public static TheoryData<object> Integers =>
        new()
        {
            (BigInteger)7,
            7L,
            7UL,
            7,
            7U,
            (short)7,
            (ushort)7,
            (sbyte)7,
            (byte)7,
        };

    [Fact]
    public void TheDriversDecimal_KeepsTheColumnsScale()
    {
        // What an ordinary Dapper read hands the handler over a Decimal64(4) column holding 1.5.
        // The scale is the column's and the trailing zeros are part of it, so a conversion that
        // dropped them would report a different column type than the one that was read.
        var value = Handler.Parse(new DriverDecimal(15000, 4));

        OracleValue.Observe(value).Should().Be(new OracleValue(15000, 4));
    }

    [Fact]
    public void AWideDriverDecimal_IsExact()
    {
        var mantissa = BigInteger.Parse("123456789012345678901231234567890", CultureInfo.InvariantCulture);

        var value = Handler.Parse(new DriverDecimal(mantissa, 10));

        OracleValue.Observe(value).Should().Be(new OracleValue(mantissa, 10));
    }

    [Fact]
    public void AValueOfTheType_IsPassedThrough()
    {
        // The shape a caller who also installed the adapter's connection-wide mapping produces:
        // its read hook has already converted the value before Dapper sees it.
        var value = BigDecimal.Parse("1.50", CultureInfo.InvariantCulture);

        OracleValue.Observe(Handler.Parse(value)).Should().Be(new OracleValue(150, 2));
    }

    [Fact]
    public void ADecimal_SaysTheOptionIsOff()
    {
        // The one form that means the connection is misconfigured. Accepting it would buy a query
        // that works today and raises inside the driver on the day a wider row arrives.
        var parse = () => Handler.Parse(1.50m);

        parse.Should().Throw<InvalidOperationException>().WithMessage("*UseCustomDecimals*");
    }

    [Fact]
    public void Text_IsReadAtItsOwnScale()
    {
        const string wide = "123456789012345678901234567890.123456789";

        Handler.Parse(wide).ToString(null, CultureInfo.InvariantCulture).Should().Be(wide);
    }

    [Theory]
    [MemberData(nameof(Integers))]
    public void EveryIntegerWidthClickHouseHas_IsExact(object value) =>
        OracleValue.Observe(Handler.Parse(value)).Should().Be(new OracleValue(7, 0));

    [Fact]
    public void TheLargestUnsignedValueClickHouseHolds_IsExact()
    {
        // UInt256's largest value is exactly this type's largest magnitude, so the widest integer
        // column ClickHouse has converts at its boundary rather than near it.
        var largest = BigInteger.Pow(2, 256) - BigInteger.One;

        OracleValue.Observe(Handler.Parse(largest)).Should().Be(new OracleValue(largest, 0));
    }

    [Fact]
    public void ABinaryFloat_IsRefusedByName()
    {
        var parse = () => Handler.Parse(1.5d);

        parse.Should().Throw<InvalidCastException>().WithMessage("*System.Double*");
    }

    [Fact]
    public void ANullColumn_SaysWhatToDoInstead()
    {
        var parse = () => Handler.Parse(DBNull.Value);

        parse.Should().Throw<InvalidCastException>().WithMessage("*BigDecimal?*");
    }

    [Fact]
    public void SetValue_PutsTheValueOnTheParameterAndNamesNoType()
    {
        // The type comes from the statement, not from here. Naming one would mean guessing it from
        // the value, after which the server rescales by truncating instead of this type rounding.
        var parameter = new ClickHouseDbParameter { ParameterName = "v" };

        Handler.SetValue(parameter, BigDecimal.One);

        parameter.Value.Should().Be(BigDecimal.One);
        parameter.ClickHouseType.Should().BeNull();
    }

    [Fact]
    public void AnArrayColumn_KeepsEveryElementsScale()
    {
        var values = ArrayHandler.Parse(new[] { new DriverDecimal(15000, 4), new DriverDecimal(22500, 4) });

        values.Should().SatisfyRespectively(
            first => OracleValue.Observe(first).Should().Be(new OracleValue(15000, 4)),
            second => OracleValue.Observe(second).Should().Be(new OracleValue(22500, 4)));
    }

    [Fact]
    public void AnArrayOfDecimals_SaysTheOptionIsOff()
    {
        decimal[] narrow = [1.5m];

        var parse = () => ArrayHandler.Parse(narrow);

        parse.Should().Throw<InvalidOperationException>().WithMessage("*UseCustomDecimals*");
    }

    [Fact]
    public void AnArrayOfAnotherType_IsRefusedByName()
    {
        double[] binary = [1.5d];

        var parse = () => ArrayHandler.Parse(binary);

        parse.Should().Throw<InvalidCastException>().WithMessage("*System.Double[]*");
    }

    [Fact]
    public void TheRegistrationIsIdempotent()
    {
        // The assembly fixture has already run it once. Dapper accepts a second registration and
        // replaces silently, so what this pins is that calling it twice is not an error.
        var register = ClickHouseSqlMapperBigDecimalExtensions.UseBigDecimal;

        register.Should().NotThrow();
        register.Should().NotThrow();
    }
}
