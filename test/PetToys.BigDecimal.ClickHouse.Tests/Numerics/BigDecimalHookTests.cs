using System;
using System.Numerics;
using AwesomeAssertions;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using Xunit;
using DriverDecimal = ClickHouse.Driver.Numerics.ClickHouseDecimal;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The two hooks the driver calls, exercised directly rather than through a server.
/// </summary>
/// <remarks>
/// Both are handed values by the driver and both see everything, not only what this package maps,
/// so what matters here is as much what they leave alone as what they convert.
/// </remarks>
public sealed class BigDecimalHookTests
{
    /// <summary>Values the read hook has to hand back exactly as they came.</summary>
    public static TheoryData<object> Untouched =>
    [
        DBNull.Value,
        "text",
        42,
        new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc),
        new[] { "a", "b" },
    ];

    [Fact]
    public void AReadDecimal_BecomesTheValue()
    {
        var converted = Convert(new DriverDecimal(new BigInteger(12345), 4), "total", "Decimal(38, 4)");

        converted.Should().BeOfType<BigDecimal>().Which.Should().Be(BigDecimal.Parse("1.2345", null));
    }

    [Fact]
    public void AReadArray_BecomesAnArrayOfValues()
    {
        DriverDecimal[] read = [new(new BigInteger(15), 1), new(new BigInteger(25), 1)];

        var converted = Convert(read, "totals", "Array(Decimal(18, 1))");

        converted.Should().BeOfType<BigDecimal[]>().Which.Should().Equal(
            BigDecimal.Parse("1.5", null),
            BigDecimal.Parse("2.5", null));
    }

    [Theory]
    [MemberData(nameof(Untouched))]
    public void EverythingElse_IsHandedBackAsItCame(object value)
    {
        var converted = Convert(value, "c", "whatever");

        converted.Should().BeSameAs(value);
    }

    [Fact]
    public void AColumnDecodedThroughSystemDecimal_NamesTheDriverOption()
    {
        // With the driver's own arbitrary-precision decimals off, a narrow value decodes without
        // complaint and a wide one raises inside the driver before this hook is reached. Passing
        // the narrow one through would leave the same query to fail on the day a wider row arrives.
        var converting = () => Convert(1.5m, "total", "Decimal(18, 4)");

        converting.Should().Throw<InvalidOperationException>()
            .WithMessage("*total*")
            .WithMessage("*UseCustomDecimals*");
    }

    [Fact]
    public void TheGenericHook_CannotAndDoesNotChangeAnything()
    {
        // It returns the type it was given, so it could not map one type onto another even if it
        // wanted to, and the driver has already cast its own value before calling it.
        var value = new DriverDecimal(new BigInteger(1), 0);

        BigDecimalReadValueConverter.Instance.ConvertValue(value, "c", "Decimal(9, 0)").Should().Be(value);
        BigDecimalReadValueConverter.Instance.ConvertValue(7, "c", "Int32").Should().Be(7);
    }

    /// <summary>Calls the hook the driver calls for an untyped read.</summary>
    /// <param name="value">The value the driver decoded.</param>
    /// <param name="name">The column's name.</param>
    /// <param name="type">The column's declared type.</param>
    /// <returns>What the hook makes of it.</returns>
    /// <remarks>
    /// The cast is load bearing. The two overloads differ only in whether the argument is typed, so
    /// a statically typed value binds to the generic one, which returns what it was given: a test
    /// calling it directly would pass while asserting nothing.
    /// </remarks>
    private static object Convert(object value, string name, string type) =>
        BigDecimalReadValueConverter.Instance.ConvertValue(value, name, type);

    [Fact]
    public void TheConnectionWideRegistration_SwitchesTheDriverOptionOnAndLeavesTheOriginalAlone()
    {
        // No server takes part, so this belongs on the legs that exclude the integration category:
        // the three required checks are all of them, and a guarantee only the container leg covers
        // is a guarantee nothing in CI covers.
        var settings = new ClickHouseClientSettings("Host=localhost;UseCustomDecimals=false");

        settings.UseCustomDecimals.Should().BeFalse();

        var registered = settings.UseBigDecimal();

        registered.UseCustomDecimals.Should().BeTrue("the mapping cannot work without it");
        registered.ReadValueConverter.Should().BeSameAs(ClickHouseBigDecimal.ReadValueConverter);
        registered.ParameterFormatter.Should().BeSameAs(ClickHouseBigDecimal.ParameterFormatter);
        settings.UseCustomDecimals.Should().BeFalse("the caller's own settings are not modified");
        settings.ReadValueConverter.Should().BeNull();
    }

    [Fact]
    public void AParameter_IsRenderedAtTheColumnsScale()
    {
        var text = BigDecimalParameterFormatter.Instance.Format(
            BigDecimal.Parse("1.5", null), "Decimal256(6)", "total");

        text.Should().Be("1.500000");
    }

    [Fact]
    public void TheArgumentsAreReadByShapeRatherThanByPosition()
    {
        // The interface declares (value, name, type) and the driver passes (value, type, name).
        // Reading whichever argument parses as a type is what keeps this working either way, and
        // this case is what would still pass if the driver's signature were corrected.
        var swapped = BigDecimalParameterFormatter.Instance.Format(
            BigDecimal.Parse("1.5", null), "total", "Decimal256(6)");

        swapped.Should().Be("1.500000");
    }

    [Fact]
    public void AValueThisPackageDoesNotMap_IsLeftToTheDriver()
    {
        // A formatter is consulted for every parameter on the connection, so refusing loudly here
        // would break every other type. Null is how the driver's own dictionary formatter answers
        // for a type it does not carry.
        BigDecimalParameterFormatter.Instance.Format(42, "Int32", "n").Should().BeNull();
        BigDecimalParameterFormatter.Instance.Format("text", "String", "s").Should().BeNull();
    }

    [Fact]
    public void AParameterWithNoDeclaredType_IsRefusedRatherThanGuessed()
    {
        // Falling back to the value's own scale would hand the server a value it silently truncates.
        var formatting = () => BigDecimalParameterFormatter.Instance.Format(
            BigDecimal.Parse("1.5", null), "total", "not a type");

        formatting.Should().Throw<InvalidOperationException>().WithMessage("*Decimal256(6)*");
    }

    [Fact]
    public void AParameterBeyondTheColumn_NamesTheParameter()
    {
        var formatting = () => BigDecimalParameterFormatter.Instance.Format(
            BigDecimal.Parse("1000000000", null), "Decimal32(4)", "total");

        formatting.Should().Throw<OverflowException>().WithMessage("*total*");
    }
}
