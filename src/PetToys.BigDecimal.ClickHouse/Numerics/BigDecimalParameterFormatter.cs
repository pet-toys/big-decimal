using System;
using System.Globalization;
using ClickHouse.Driver.ADO.Parameters;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Renders a <see cref="BigDecimal"/> parameter as the decimal text a statement carries.
/// </summary>
/// <remarks>
/// Without it the driver converts the value through <see cref="IConvertible"/>, which
/// <see cref="BigDecimal"/> does not implement, and the write fails naming neither the column nor
/// the value. A value it does not handle is answered with <see langword="null"/>, as the driver's
/// own <see cref="DictionaryParameterFormatter"/> does.
/// </remarks>
internal sealed class BigDecimalParameterFormatter : IParameterFormatter
{
    internal static readonly BigDecimalParameterFormatter Instance = new();

    private BigDecimalParameterFormatter()
    {
    }

    /// <summary>Renders one parameter at its column's scale, or answers <see langword="null"/> for a value this package does not map.</summary>
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
    /// The interface declares <c>(value, name, type)</c> and the driver passes
    /// <c>(value, type, name)</c>, so the type is taken from whichever argument parses as one; a
    /// test pins the order observed. Falling back to the value's own scale would hand the server
    /// a value it silently truncates, so an unannotated parameter is refused instead.
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
