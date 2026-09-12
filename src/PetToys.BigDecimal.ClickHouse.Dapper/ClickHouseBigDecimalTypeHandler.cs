using System;
using System.Data;
using System.Globalization;
using System.Numerics;
using ClickHouse.Driver.Numerics;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // Dapper's namespace, where a caller registering a handler already is.

namespace Dapper;

/// <summary>
/// Maps <see cref="BigDecimal"/> for Dapper over ClickHouse: reads a decimal column the driver
/// has already decoded, and carries a parameter of the type.
/// </summary>
/// <remarks>
/// The name carries the driver's, unlike the PostgreSQL package's, because both publish into the
/// <c>Dapper</c> namespace. Unlike that handler, this one is the whole read side: the driver
/// decodes a decimal column into its own arbitrary-precision type at the column's scale, so
/// there is nothing to widen and no reader to wrap.
/// </remarks>
public sealed class ClickHouseBigDecimalTypeHandler : SqlMapper.TypeHandler<BigDecimal>
{
    /// <summary>Converts what the driver decoded into a <see cref="BigDecimal"/>.</summary>
    /// <param name="value">What Dapper read from the column.</param>
    /// <returns>The value, at the column's scale.</returns>
    /// <exception cref="InvalidCastException">
    /// The column produced a type this handler cannot convert exactly. The message names it.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The column was decoded through <see cref="decimal"/>, which means the connection's
    /// <c>UseCustomDecimals</c> option is off.
    /// </exception>
    /// <exception cref="FormatException">The column produced text that is not a number.</exception>
    /// <exception cref="OverflowException">
    /// The column's integer part is larger than this type's magnitude.
    /// </exception>
    /// <remarks>
    /// A <see cref="decimal"/> is refused: it carries the value's scale rather than the column's,
    /// and a row wider than one raises inside the driver before this runs, so accepting it buys a
    /// query that breaks on the day a wider row arrives.
    /// </remarks>
    public override BigDecimal Parse(object value) => value switch
    {
        ClickHouseDecimal driverDecimal => BigDecimal.FromScaled(driverDecimal.Mantissa, driverDecimal.Scale),
        BigDecimal wide => wide,
        string text => BigDecimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
        // Every integer width ClickHouse has; Int128, Int256 and UInt256 arrive as BigInteger.
        BigInteger integer => (BigDecimal)integer,
        long integer => integer,
        ulong integer => integer,
        int integer => integer,
        uint integer => integer,
        short integer => integer,
        ushort integer => integer,
        sbyte integer => integer,
        byte integer => integer,
        decimal => throw CustomDecimalsAreOff(),
        null or DBNull => throw new InvalidCastException(
            "A NULL column cannot be read as BigDecimal. Read it as BigDecimal? instead."),
        _ => throw new InvalidCastException(
            string.Format(
                CultureInfo.InvariantCulture,
                "A column of CLR type {0} cannot be read as BigDecimal exactly. A decimal column "
                + "reads through this handler; anything else is a conversion the caller has to "
                + "choose.",
                value.GetType())),
    };

    /// <summary>Puts the value on the parameter, naming no database type.</summary>
    /// <param name="parameter">The parameter Dapper created.</param>
    /// <param name="value">The value to write.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parameter"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The column's type comes from the statement, <c>{total:Decimal128(10)}</c>, and reaches the
    /// parameter formatter from there; a type guessed here from the value would let the server
    /// truncate at the column's scale.
    /// </remarks>
    public override void SetValue(IDbDataParameter parameter, BigDecimal value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = value;
    }

    internal static InvalidOperationException CustomDecimalsAreOff() =>
        new("A decimal column was decoded through System.Decimal, so it cannot be read exactly. "
            + "Set UseCustomDecimals=true on the connection, which UseBigDecimalForDapper does "
            + "for you.");
}
