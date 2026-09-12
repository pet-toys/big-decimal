using System;
using System.Data;
using System.Globalization;
using ClickHouse.Driver.Numerics;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // Dapper's namespace, where a caller registering a handler already is.

namespace Dapper;

/// <summary>
/// Maps an <c>Array(Decimal...)</c> column onto <see cref="BigDecimal"/><c>[]</c> for Dapper.
/// </summary>
/// <remarks>
/// A separate handler because Dapper looks one up by the member's exact type.
/// </remarks>
public sealed class ClickHouseBigDecimalArrayTypeHandler : SqlMapper.TypeHandler<BigDecimal[]>
{
    /// <summary>Converts what the driver decoded into an array of values.</summary>
    /// <param name="value">What Dapper read from the column.</param>
    /// <returns>The elements, in order, each at the column's scale.</returns>
    /// <exception cref="InvalidCastException">
    /// The column produced a type this handler cannot convert exactly. The message names it.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The column was decoded through <see cref="decimal"/>, which means the connection's
    /// <c>UseCustomDecimals</c> option is off.
    /// </exception>
    public override BigDecimal[] Parse(object value) => value switch
    {
        ClickHouseDecimal[] many => Array.ConvertAll(
            many,
            element => BigDecimal.FromScaled(element.Mantissa, element.Scale)),
        BigDecimal[] wide => wide,
        decimal[] => throw ClickHouseBigDecimalTypeHandler.CustomDecimalsAreOff(),
        // Unreachable - a ClickHouse Array column is never NULL - but the alternative is a NullReferenceException.
        null or DBNull => throw new InvalidCastException(
            "A NULL column cannot be read as BigDecimal[]."),
        _ => throw new InvalidCastException(
            string.Format(
                CultureInfo.InvariantCulture,
                "A column of CLR type {0} cannot be read as BigDecimal[] exactly.",
                value.GetType())),
    };

    /// <summary>Puts the value on the parameter, naming no database type.</summary>
    /// <param name="parameter">The parameter Dapper created.</param>
    /// <param name="value">The value to write.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parameter"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// Here because the base type declares it: Dapper expands a collection parameter into one
    /// scalar per element before a handler is consulted, so the array never arrives whole. The
    /// many-rows path is <c>InsertBigDecimalAsync</c> on the adapter.
    /// </remarks>
    public override void SetValue(IDbDataParameter parameter, BigDecimal[]? value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = (object?)value ?? DBNull.Value;
    }
}
