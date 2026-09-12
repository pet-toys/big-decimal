using System;
using System.Globalization;
using ClickHouse.Driver.ADO.Parameters;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Refuses a <see cref="BigDecimal"/> parameter the statement did not annotate, and defers on every
/// other type.
/// </summary>
/// <remarks>
/// The driver consults this only for a parameter the statement does not annotate, so being asked
/// means the column's scale is unknown, and a type guessed from the value would let the server
/// truncate at the column's scale where this package rounds. Every other type is deferred to the
/// inner resolver, or to the driver through <see langword="null"/>. Unlike
/// <see cref="IParameterFormatter.Format"/>, this interface's arguments arrive in declared order.
/// </remarks>
internal sealed class BigDecimalParameterTypeResolver(IParameterTypeResolver? inner) : IParameterTypeResolver
{
    /// <summary>Answers for one parameter, or refuses it.</summary>
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

    // The example says whether the missing annotation is Decimal256(6) or Array(Decimal256(6)),
    // which the driver's own refusal does not.
    private static InvalidOperationException Unannotated(string name, string example) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"The parameter '{name}' carries a BigDecimal and the statement does not name its ClickHouse type, so the column's scale is unknown. Annotate it in the statement, as in {{{name}:{example}}}. Through Dapper, pass it with DynamicParameters.Add as well - an anonymous object is stripped before the parameter reaches this package."));
}
