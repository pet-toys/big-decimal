using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.ADO.Readers;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // See the remarks: the namespace is the driver's on purpose.

namespace ClickHouse.Driver;

/// <summary>
/// The <see cref="BigDecimal"/> mapping for ClickHouse decimal columns, as the driver's own hooks
/// take it.
/// </summary>
/// <remarks>
/// <para>
/// In the driver's namespace, so that a caller holding a <see cref="QueryOptions"/> reaches this
/// without a <c>using</c> they do not already have. The whole public surface of this package is
/// here, including the extensions on types that live in namespaces below this one: it is the
/// namespace a ClickHouse caller already imports, and one import is one thing to get wrong. It is
/// the only namespace this package declares and does not own, and IDE0130 is suppressed per file
/// for that reason.
/// </para>
/// <para>
/// <b>Prefer the per-query form.</b> The read hook is consulted once per value rather than once per
/// column, so installing the mapping on a connection makes every decimal column of every query pay
/// for the few that need it, and it changes what <c>GetValue</c> answers for all of them.
/// <see cref="CreateQueryOptions"/> is the narrow form; <c>UseBigDecimal</c> on
/// <see cref="ADO.ClickHouseClientSettings"/> is the wide one, and it exists because
/// <see cref="ADO.ClickHouseCommand"/> has no per-query hook at all.
/// </para>
/// <para>
/// Both forms need the driver's own arbitrary-precision decimals, its <c>UseCustomDecimals</c>
/// option. With that off, a value wider than <see cref="decimal"/> raises inside the driver before
/// any hook here is reached, which nothing in this package can rescue. The connection-wide
/// registration switches it on; the per-query one cannot, and the read hook says so by name when it
/// meets a column the driver decoded through <see cref="decimal"/>.
/// </para>
/// </remarks>
public static class ClickHouseBigDecimal
{
    /// <summary>
    /// The read hook: maps a decimal column to <see cref="BigDecimal"/> and an array of one to
    /// <c>BigDecimal[]</c>, and returns every other value untouched.
    /// </summary>
    /// <remarks>
    /// Exposed for the caller who is building a <see cref="QueryOptions"/> with other properties of
    /// their own, since the driver's options types are init-only and cannot be amended after
    /// construction. <see cref="CreateQueryOptions"/> covers the case where there is nothing else to
    /// set.
    /// </remarks>
    public static IReadValueConverter ReadValueConverter => BigDecimalReadValueConverter.Instance;

    /// <summary>
    /// The write hook: renders a <see cref="BigDecimal"/> parameter at its column's scale, and
    /// leaves every other value to the driver.
    /// </summary>
    /// <remarks>
    /// The parameter must be annotated with its type in the statement, as in
    /// <c>{total:Decimal256(6)}</c>. That is how the driver learns the type at all, and it is where
    /// this hook reads the scale it rescales to.
    /// </remarks>
    public static IParameterFormatter ParameterFormatter => BigDecimalParameterFormatter.Instance;

    /// <summary>Builds query options carrying the mapping, for one query.</summary>
    /// <returns>Fresh options with both hooks installed.</returns>
    /// <remarks>
    /// This is the form to reach for. A neighbouring query on the same client is unaffected, which
    /// is what makes the mapping something a query asks for rather than something a connection
    /// imposes. To set other options beside it, construct <see cref="QueryOptions"/> yourself and
    /// assign <see cref="ReadValueConverter"/> and <see cref="ParameterFormatter"/> in the
    /// initialiser; the driver's options are init-only, so they cannot be added afterwards.
    /// </remarks>
    public static QueryOptions CreateQueryOptions() => new()
    {
        ReadValueConverter = ReadValueConverter,
        ParameterFormatter = ParameterFormatter,
    };
}
