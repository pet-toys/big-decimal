using System;
using System.Data;
using System.Globalization;
using System.Numerics;
using ClickHouse.Driver.Numerics;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // See the remarks: the namespace is Dapper's on purpose.

namespace Dapper;

/// <summary>
/// Maps <see cref="BigDecimal"/> for Dapper over ClickHouse: reads a decimal column the driver
/// has already decoded, and carries a parameter of the type.
/// </summary>
/// <remarks>
/// <para>
/// In Dapper's own namespace, which is where a caller registering a handler already is, so that
/// <c>SqlMapper.AddTypeHandler(new ClickHouseBigDecimalTypeHandler())</c> - the line every Dapper
/// plugin's documentation carries - needs no <c>using</c> beyond the one they have. IDE0130 is
/// suppressed here rather than for the project for that reason.
/// </para>
/// <para>
/// The name carries the driver's, unlike the PostgreSQL package's, because both packages publish
/// into the <c>Dapper</c> namespace and an application talking to both databases would otherwise
/// reference two assemblies declaring one type name.
/// </para>
/// <para>
/// Unlike the PostgreSQL package's handler, this one is the whole read side. The driver decodes a
/// decimal column into its own arbitrary-precision type, carrying the column's mantissa and the
/// column's scale, and Dapper hands that over untouched - so there is nothing to widen and no
/// reader to wrap.
/// </para>
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
    /// The driver's own decimal is the ordinary case. A <see cref="BigDecimal"/> is what a caller
    /// who also installed the adapter's connection-wide mapping produces, since its read hook has
    /// already converted the value. Every integer width and decimal text convert exactly and are
    /// accepted as they are. A <see cref="decimal"/> means <c>UseCustomDecimals</c> is off
    /// and is refused: it carries the value's scale rather than the column's, and a row wider than
    /// <see cref="decimal"/> raises inside the driver before this runs, so accepting it buys a
    /// query that breaks on the day a wider row arrives.
    /// </remarks>
    public override BigDecimal Parse(object value) => value switch
    {
        ClickHouseDecimal driverDecimal => BigDecimal.FromScaled(driverDecimal.Mantissa, driverDecimal.Scale),
        BigDecimal wide => wide,
        string text => BigDecimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
        // Every integer width ClickHouse has, because they are what its columns are made of and
        // each one converts exactly. Int128, Int256 and UInt256 arrive as BigInteger, and the
        // largest of them, 2^256-1, is exactly this type's largest magnitude.
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
    /// The column's type comes from the statement - <c>{total:Decimal128(10)}</c> - and reaches
    /// the parameter formatter from there, which is what lets this package rescale at the column's
    /// scale and refuse a value the column cannot hold. Naming a type here instead would mean
    /// guessing one from the value, after which the server applies the column's scale by
    /// truncating rather than by this type's rounding rule, so nothing is named.
    /// </remarks>
    public override void SetValue(IDbDataParameter parameter, BigDecimal value)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        parameter.Value = value;
    }

    /// <summary>Refuses a value the driver decoded through <see cref="decimal"/>.</summary>
    /// <returns>The exception to throw.</returns>
    internal static InvalidOperationException CustomDecimalsAreOff() =>
        new("A decimal column was decoded through System.Decimal, so it cannot be read exactly. "
            + "Set UseCustomDecimals=true on the connection, which UseBigDecimalForDapper does "
            + "for you.");
}
