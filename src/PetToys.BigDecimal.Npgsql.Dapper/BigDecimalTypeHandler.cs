using System;
using System.Data;
using System.Globalization;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // Dapper's namespace, where a caller registering a handler already is.

namespace Dapper;

/// <summary>
/// Maps <see cref="BigDecimal"/> for Dapper: writes a parameter carrying one, and reads a value
/// Dapper has already materialised.
/// </summary>
/// <remarks>
/// What this handler cannot do is read a value wider than <see cref="decimal"/> from a
/// <c>numeric</c> column: Dapper hands <see cref="Parse"/> whatever <c>GetValue</c> produced, and
/// a wider value throws inside the driver first. The exact read is <c>QueryBigDecimal</c> on
/// <see cref="SqlMapperBigDecimalExtensions"/>.
/// </remarks>
public sealed class BigDecimalTypeHandler : SqlMapper.TypeHandler<BigDecimal>
{
    /// <summary>Converts what Dapper materialised into a <see cref="BigDecimal"/>.</summary>
    /// <remarks>
    /// Every accepted form converts exactly. A binary floating-point value is refused: the
    /// conversion a caller wants there is a decision about precision rather than a mapping.
    /// </remarks>
    /// <param name="value">What Dapper read from the column.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidCastException">The column produced a type this handler cannot
    /// convert exactly. The message names it.</exception>
    /// <exception cref="FormatException">The column produced text that is not a number.</exception>
    /// <exception cref="OverflowException">The column's integer part is larger than this type's
    /// magnitude.</exception>
    public override BigDecimal Parse(object value) => value switch
    {
        BigDecimal wide => wide,
        decimal narrow => narrow,
        string text => BigDecimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
        long integer => integer,
        int integer => integer,
        short integer => integer,
        byte integer => integer,
        null or DBNull => throw new InvalidCastException(
            "A NULL column cannot be read as BigDecimal. Read it as BigDecimal? instead."),
        _ => throw new InvalidCastException(
            string.Format(
                CultureInfo.InvariantCulture,
                "A column of CLR type {0} cannot be read as BigDecimal exactly. A numeric column "
                + "reads through this package's own query methods; anything else is a conversion "
                + "the caller has to choose.",
                value.GetType())),
    };

    /// <summary>Puts the value on the parameter, naming no database type.</summary>
    /// <remarks>
    /// The mapping on the data source resolves a parameter by its CLR type; without it the driver
    /// refuses the parameter by name.
    /// </remarks>
    /// <param name="parameter">The parameter Dapper created.</param>
    /// <param name="value">The value to write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parameter"/> is
    /// <see langword="null"/>.</exception>
    public override void SetValue(IDbDataParameter parameter, BigDecimal value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = value;
    }
}
