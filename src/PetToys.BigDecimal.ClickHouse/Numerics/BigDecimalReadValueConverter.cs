using System;
using System.Globalization;
using ClickHouse.Driver.ADO.Readers;
using DriverDecimal = ClickHouse.Driver.Numerics.ClickHouseDecimal;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Maps what the driver read from a decimal column onto <see cref="BigDecimal"/>.
/// </summary>
/// <remarks>
/// Consulted once per value for every column of every row: it recognises the two shapes a decimal
/// column arrives in and returns everything else as it came, <see cref="DBNull"/> included.
/// </remarks>
internal sealed class BigDecimalReadValueConverter : IReadValueConverter
{
    internal static readonly BigDecimalReadValueConverter Instance = new();

    private BigDecimalReadValueConverter()
    {
    }

    /// <summary>Maps one column of one row.</summary>
    /// <exception cref="InvalidOperationException">
    /// The driver decoded a decimal column into <see cref="decimal"/>, which means its
    /// <c>UseCustomDecimals</c> option is off.
    /// </exception>
    public object ConvertValue(object value, string name, string type) => value switch
    {
        DriverDecimal one => BigDecimalColumnCodec.FromColumn(one),
        DriverDecimal[] many => BigDecimalColumnCodec.FromColumn(many),
        decimal or decimal[] => ThrowCustomDecimalsAreOff(name),
        _ => value,
    };

    /// <summary>Returns the value unchanged: the driver has already cast it to <typeparamref name="T"/> before calling.</summary>
    public T ConvertValue<T>(T value, string name, string type) => value;

    // With UseCustomDecimals off a narrow value decodes and a wide one raises inside the driver,
    // so a query that works today fails on the day a wider row arrives.
    private static object ThrowCustomDecimalsAreOff(string name) =>
        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Column '{name}' was decoded as System.Decimal, so the mapping cannot read it exactly. Set UseCustomDecimals=true on the connection, which the connection-wide registration does for you."));
}
