using System;
using System.Globalization;
using ClickHouse.Driver.ADO.Parameters;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Refuses a <see cref="BigDecimal"/> parameter the statement did not annotate, and defers on every
/// other type.
/// </summary>
/// <remarks>
/// <para>
/// The driver consults this only for a parameter whose type the statement does not name; an
/// annotated one never reaches it. So being asked at all means the column's type is unknown, and
/// the only type this could answer with is one derived from the value. That answer becomes the
/// declared type the parameter formatter is handed, after which the server applies the column's own
/// scale by truncating, where this package rounds half to even. A refusal is the answer; a guess
/// would be a different number.
/// </para>
/// <para>
/// Every other type is deferred, which the driver reads from <see langword="null"/> exactly as it
/// reads no resolver at all - the interface is consulted for every parameter on the connection, so
/// a resolver that answered for all of them would declare unrelated parameters decimals. An inner
/// resolver a caller installed is asked instead of returning <see langword="null"/>, so this can be
/// composed into settings that already carry one.
/// </para>
/// <para>
/// Unlike <see cref="IParameterFormatter.Format"/>, this interface's arguments are in the order it
/// declares them: the third really is the parameter's name. The formatter's defensive parse is for
/// that other interface and is deliberately not repeated here.
/// </para>
/// </remarks>
/// <param name="inner">
/// A resolver to consult for types this one does not own, or <see langword="null"/> to defer to the
/// driver.
/// </param>
internal sealed class BigDecimalParameterTypeResolver(IParameterTypeResolver? inner) : IParameterTypeResolver
{
    /// <summary>Answers for one parameter, or refuses it.</summary>
    /// <param name="type">The parameter value's type.</param>
    /// <param name="value">The parameter's value.</param>
    /// <param name="name">The parameter's name.</param>
    /// <returns>
    /// The inner resolver's answer, or <see langword="null"/> to leave the type to the driver.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="type"/> is a type this package maps, which means the statement did not
    /// annotate the parameter.
    /// </exception>
    public string ResolveType(Type type, object value, string name)
    {
        if (type == typeof(BigDecimal) || type == typeof(BigDecimal?))
        {
            throw Unannotated(name, "Decimal256(6)");
        }

        if (type == typeof(BigDecimal[]))
        {
            throw Unannotated(name, "Array(Decimal256(6))");
        }

        return inner?.ResolveType(type, value, name)!;
    }

    /// <summary>Builds the refusal for a parameter the statement left untyped.</summary>
    /// <param name="name">The parameter's name.</param>
    /// <param name="example">A ClickHouse type to show the annotation with.</param>
    /// <returns>The exception to throw.</returns>
    /// <remarks>
    /// The driver's own refusal for the same parameter names the element type and nothing else, and
    /// it names it identically for one value and for an array, so a caller reading it cannot tell
    /// whether the annotation they are missing is <c>Decimal256(6)</c> or
    /// <c>Array(Decimal256(6))</c>. That is what <paramref name="example"/> carries.
    /// </remarks>
    private static InvalidOperationException Unannotated(string name, string example) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"The parameter '{name}' carries a BigDecimal and the statement does not name its ClickHouse type, so the column's scale is unknown. Annotate it in the statement, as in {{{name}:{example}}}. Through Dapper, pass it with DynamicParameters.Add as well - an anonymous object is stripped before the parameter reaches this package."));
}
