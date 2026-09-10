using System;
using System.Globalization;
using ClickHouse.Driver.ADO.Readers;
using DriverDecimal = ClickHouse.Driver.Numerics.ClickHouseDecimal;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Maps what the driver read from a decimal column onto <see cref="BigDecimal"/>.
/// </summary>
/// <remarks>
/// Consulted once per value, for every column of every row, including the ones this package has
/// nothing to do with: it recognises the two shapes a decimal column arrives in and returns
/// everything else exactly as it came, <see cref="DBNull"/> included. Stateless, so one instance
/// serves every query.
/// </remarks>
internal sealed class BigDecimalReadValueConverter : IReadValueConverter
{
    /// <summary>The only instance there is any reason to have.</summary>
    internal static readonly BigDecimalReadValueConverter Instance = new();

    private BigDecimalReadValueConverter()
    {
    }

    /// <summary>Maps one column of one row.</summary>
    /// <param name="value">What the driver decoded.</param>
    /// <param name="name">The column's name.</param>
    /// <param name="type">The column's declared type, in the normalised spelling.</param>
    /// <returns>
    /// A <see cref="BigDecimal"/> or an array of them for a decimal column, and
    /// <paramref name="value"/> unchanged for anything else.
    /// </returns>
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

    /// <summary>Returns the value unchanged, because this overload cannot do anything else.</summary>
    /// <typeparam name="T">The type the caller asked for.</typeparam>
    /// <param name="value">The value the driver already cast to <typeparamref name="T"/>.</param>
    /// <param name="name">The column's name.</param>
    /// <param name="type">The column's declared type.</param>
    /// <returns><paramref name="value"/>.</returns>
    /// <remarks>
    /// This is not an unfinished implementation. The signature returns the type it was given, so it
    /// cannot map one type onto another, and the driver has already cast its own value to
    /// <typeparamref name="T"/> before calling: by the time this runs, a caller asking for
    /// <see cref="BigDecimal"/> has already failed. It is also called for reads that have nothing
    /// to do with this package, which is why it returns rather than throws.
    /// </remarks>
    public T ConvertValue<T>(T value, string name, string type) => value;

    /// <summary>Refuses a read the driver decoded through <see cref="decimal"/>.</summary>
    /// <param name="name">The column's name.</param>
    /// <returns>Nothing; this always throws.</returns>
    /// <remarks>
    /// Silence here would be worse than the failure. With the driver's own arbitrary-precision
    /// decimals switched off, a narrow value decodes without complaint and a wide one raises inside
    /// the driver before this hook is reached, so a query that works today fails on the day a wider
    /// row arrives, in a place that says nothing about this package.
    /// </remarks>
    private static object ThrowCustomDecimalsAreOff(string name) =>
        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Column '{name}' was decoded as System.Decimal, so the mapping cannot read it exactly. Set UseCustomDecimals=true on the connection, which the connection-wide registration does for you."));
}
