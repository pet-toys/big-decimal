using System;
using System.Data;
using System.Globalization;
using AwesomeAssertions;
using Dapper;
using Npgsql;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The handler on its own, over each CLR type Dapper can hand it and one it cannot.
/// </summary>
/// <remarks>
/// These need no server: what a column produced is Dapper's business, and what this type does with
/// what it was handed is this package's.
/// </remarks>
public sealed class BigDecimalTypeHandlerTests
{
    private static BigDecimalTypeHandler Handler { get; } = new();

    [Fact]
    public void AValueOfTheType_IsPassedThrough()
    {
        var value = BigDecimal.Parse("1.50", CultureInfo.InvariantCulture);

        OracleValue.Observe(Handler.Parse(value)).Should().Be(new OracleValue(150, 2));
    }

    [Fact]
    public void ADecimal_KeepsItsScale()
    {
        // What an ordinary Dapper read hands the handler over a numeric column. The trailing zero
        // is part of the value on both sides, so a conversion that dropped it would be a loss.
        OracleValue.Observe(Handler.Parse(1.50m)).Should().Be(new OracleValue(150, 2));
    }

    [Fact]
    public void Text_IsReadAtItsOwnScale()
    {
        // The ::text route: the caller cast the column, so the value arrives whole however wide it
        // is, and the server renders it invariantly whatever the thread's culture is.
        const string wide = "123456789012345678901234567890.123456789";

        Handler.Parse(wide).ToString(null, CultureInfo.InvariantCulture).Should().Be(wide);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void TheNonFiniteText_IsRead(string text) =>
        Handler.Parse(text).ToString(null, CultureInfo.InvariantCulture).Should().Be(text);

    [Fact]
    public void AnInteger_IsExact() =>
        OracleValue.Observe(Handler.Parse(7L)).Should().Be(new OracleValue(7, 0));

    [Fact]
    public void ABinaryFloat_IsRefusedByName()
    {
        // Not a mapping this package can make exactly: it is a real or double precision column
        // read as this type, and what the caller wants there is a decision about precision.
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
        var parameter = new NpgsqlParameter();

        Handler.SetValue(parameter, BigDecimal.One);

        // Naming a type here would be this package deciding what the adapter's resolver already
        // decides from the CLR type, and under one MatchRequirement it refuses a named one.
        parameter.Value.Should().BeOfType<BigDecimal>();
        parameter.DataTypeName.Should().BeNull();
    }

    [Fact]
    public void SetValue_RefusesANullParameter()
    {
        var set = () => Handler.SetValue(null!, BigDecimal.One);

        set.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AsBigDecimalReader_RefusesAReaderFromAnotherProvider()
    {
        using var table = new System.Data.DataTable("t");
        table.Locale = CultureInfo.InvariantCulture;
        using var reader = table.CreateDataReader();

        var wrap = () => reader.AsBigDecimalReader();

        // The failure names what it was handed rather than widening nothing and handing back a
        // reader that looks like it works.
        wrap.Should().Throw<ArgumentException>().WithMessage("*DataTableReader*");
    }

    [Fact]
    public void AsBigDecimalReader_RefusesNull()
    {
        var wrap = () => ((IDataReader)null!).AsBigDecimalReader();

        wrap.Should().Throw<ArgumentNullException>();
    }
}
