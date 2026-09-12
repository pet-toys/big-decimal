using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // The driver's namespace, the one a ClickHouse caller already imports.

namespace ClickHouse.Driver;

/// <summary>
/// The <see cref="BigDecimal"/> mapping for ClickHouse decimal columns, as the driver's own hooks
/// take it.
/// </summary>
/// <remarks>
/// <para>
/// The whole public surface of this package is in the driver's namespace, the one a ClickHouse
/// caller already imports; it is the only namespace this package declares and does not own.
/// </para>
/// <para>
/// Prefer the per-query form, <see cref="CreateQueryOptions"/>. The read hook is consulted once
/// per value, so <c>UseBigDecimal</c> on <see cref="ADO.ClickHouseClientSettings"/> makes every
/// decimal column of every query pay for the few that need it and changes what <c>GetValue</c>
/// answers for all of them; it exists because <see cref="ADO.ClickHouseCommand"/> has no per-query
/// hook. Both forms need the driver's <c>UseCustomDecimals</c> option: with it off, a value wider
/// than <see cref="decimal"/> raises inside the driver before any hook here is reached.
/// </para>
/// </remarks>
public static class ClickHouseBigDecimal
{
    /// <summary>
    /// The read hook: maps a decimal column to <see cref="BigDecimal"/> and an array of one to
    /// <c>BigDecimal[]</c>, and returns every other value untouched.
    /// </summary>
    /// <remarks>
    /// For a caller building a <see cref="QueryOptions"/> with other properties of their own; the
    /// driver's options are init-only.
    /// </remarks>
    public static IReadValueConverter ReadValueConverter => BigDecimalReadValueConverter.Instance;

    /// <summary>
    /// The write hook: renders a <see cref="BigDecimal"/> parameter at its column's scale, and
    /// leaves every other value to the driver.
    /// </summary>
    /// <remarks>
    /// The parameter must be annotated with its type in the statement, as in
    /// <c>{total:Decimal256(6)}</c>; that is where this hook reads the scale it rescales to.
    /// </remarks>
    public static IParameterFormatter ParameterFormatter => BigDecimalParameterFormatter.Instance;

    /// <summary>
    /// Builds the write-side guard: it refuses a <see cref="BigDecimal"/> parameter the statement
    /// did not annotate with its ClickHouse type, and leaves every other type alone.
    /// </summary>
    /// <param name="inner">
    /// A resolver already on the settings, consulted for every type this package does not map, or
    /// <see langword="null"/> to leave those types to the driver.
    /// </param>
    /// <returns>A resolver to assign to the settings.</returns>
    /// <remarks>
    /// Without it the driver answers such a parameter with a bare <c>Unknown type</c>. Handed a
    /// guard this package already built, it returns that one rather than wrapping it again.
    /// </remarks>
    public static IParameterTypeResolver CreateParameterTypeResolver(IParameterTypeResolver? inner = null) =>
        inner as BigDecimalParameterTypeResolver ?? new BigDecimalParameterTypeResolver(inner);

    /// <summary>Builds query options carrying the mapping, for one query.</summary>
    /// <returns>Fresh options with both hooks installed.</returns>
    /// <remarks>
    /// A neighbouring query on the same client is unaffected. To set other options beside it,
    /// construct <see cref="QueryOptions"/> yourself and assign <see cref="ReadValueConverter"/>
    /// and <see cref="ParameterFormatter"/> in the initialiser. These options carry no parameter
    /// type resolver; <c>UseBigDecimal</c> is where the write-side refusal is installed.
    /// </remarks>
    public static QueryOptions CreateQueryOptions() => new()
    {
        ReadValueConverter = ReadValueConverter,
        ParameterFormatter = ParameterFormatter,
    };
}
