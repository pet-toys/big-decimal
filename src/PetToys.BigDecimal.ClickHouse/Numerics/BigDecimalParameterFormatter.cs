using System;
using System.Globalization;
using ClickHouse.Driver.ADO.Parameters;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Renders a <see cref="BigDecimal"/> parameter as the decimal text a statement carries.
/// </summary>
/// <remarks>
/// <para>
/// Without a formatter the driver hands a value it does not recognise to
/// <see cref="IConvertible.ToDecimal"/>, and <see cref="BigDecimal"/> implements
/// <see cref="IConvertible"/>. An unregistered value therefore does not fail as unmapped: it goes
/// through <see cref="decimal"/> when it fits and raises this type's own overflow when it does not,
/// and neither outcome mentions this package. That is the reason this class exists, beyond
/// rescaling.
/// </para>
/// <para>
/// A value this formatter does not handle is answered with <see langword="null"/>, which is how the
/// driver's own <see cref="DictionaryParameterFormatter"/> answers for a type its dictionary does
/// not carry: a formatter is consulted for every parameter, so refusing loudly would break every
/// other type on the connection.
/// </para>
/// <para>
/// It is stateless, so one instance serves every query and every connection.
/// </para>
/// </remarks>
internal sealed class BigDecimalParameterFormatter : IParameterFormatter
{
    /// <summary>The only instance there is any reason to have.</summary>
    internal static readonly BigDecimalParameterFormatter Instance = new();

    private BigDecimalParameterFormatter()
    {
    }

    /// <summary>Renders one parameter.</summary>
    /// <param name="value">The parameter's value.</param>
    /// <param name="first">
    /// The second argument the driver passes. See the remarks: it is the ClickHouse type, whatever
    /// the interface calls it.
    /// </param>
    /// <param name="second">The third argument the driver passes, which is the parameter's name.</param>
    /// <returns>
    /// The value at the column's scale, in plain decimal notation, or <see langword="null"/> when
    /// the value is not one this package maps.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Neither argument names a decimal type, so the column's scale cannot be established.
    /// </exception>
    /// <exception cref="OverflowException">
    /// The value at the column's scale is outside the column's width or its declared precision.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="value"/> is NaN or an infinity, which no ClickHouse decimal represents.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <c>IParameterFormatter.Format</c> declares its arguments as <c>(value, name, type)</c> and
    /// the driver passes <c>(value, type, name)</c>. The parameter names here are deliberately
    /// neutral and the type is taken from whichever argument parses as one, so that this keeps
    /// working whether or not the driver's signature is corrected. A test pins the order actually
    /// observed, which is what would report the change.
    /// </para>
    /// <para>
    /// When neither argument names a decimal type this raises rather than falling back to the
    /// value's own scale. The fallback would be a value the server silently truncates, which is the
    /// failure this package exists to prevent.
    /// </para>
    /// </remarks>
    public string? Format(object value, string first, string second)
    {
        if (value is not BigDecimal number)
        {
            return null;
        }

        if (ClickHouseColumnType.TryParse(first, out var type))
        {
            return BigDecimalColumnCodec.ToText(number, type, second ?? first);
        }

        if (ClickHouseColumnType.TryParse(second, out type))
        {
            return BigDecimalColumnCodec.ToText(number, type, first ?? second);
        }

        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"A BigDecimal parameter needs the column's decimal type, and neither '{first}' nor '{second}' is one. Annotate the parameter in the statement, as in {{name:Decimal256(6)}}."));
    }
}
