using System;
using ClickHouse.Driver.ADO;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // See the remarks: the namespace is the driver's root on purpose.

namespace ClickHouse.Driver;

/// <summary>
/// Configures a connection for a Dapper caller who maps <see cref="BigDecimal"/>.
/// </summary>
/// <remarks>
/// In <c>ClickHouse.Driver</c>, beside the adapter's own surface and for the same reason: it is
/// the namespace a ClickHouse caller already has. The method name differs from the adapter's
/// <c>UseBigDecimal</c> because both extend <see cref="ClickHouseClientSettings"/> from this
/// namespace, and two extension methods of one name over one type are ambiguous at a call site
/// that imports both packages - which every consumer of this one does, since it depends on the
/// adapter.
/// </remarks>
public static class ClickHouseClientSettingsBigDecimalDapperExtensions
{
    /// <summary>
    /// Returns settings a Dapper caller can map <see cref="BigDecimal"/> over: the driver's
    /// arbitrary-precision decimals switched on, and this package's parameter formatter installed.
    /// </summary>
    /// <param name="settings">The settings to build on.</param>
    /// <returns>New settings; <paramref name="settings"/> is not modified.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="settings"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This is the narrow form and the read hook is deliberately not part of it. The Dapper
    /// handlers widen a column when a caller asks for the type by naming it on a member, so
    /// nothing here has to move what an untyped read produces. <c>UseBigDecimal</c> on these same
    /// settings is the wide form, for a caller working through the ADO surface; a caller who has
    /// already used it needs nothing from here, because it installs this formatter too.
    /// </para>
    /// <para>
    /// The formatter is what makes a write exact and it is not optional. Without one the driver
    /// converts the parameter itself, by reaching for <see cref="IConvertible"/>, which
    /// <see cref="BigDecimal"/> does not implement: the write fails with an
    /// <see cref="InvalidCastException"/> before the statement is sent, naming neither the column
    /// nor the value. With it, the value is rescaled at the column's declared scale, half to even,
    /// and a value beyond the column's width or precision is refused by name.
    /// </para>
    /// <para>
    /// The parameter type resolver this installs refuses a <see cref="BigDecimal"/> parameter the
    /// statement did not annotate, naming the parameter and the shape that works, where the driver
    /// would answer <c>Unknown type</c>. A parameter Dapper removes before any of this reaches it -
    /// an anonymous object, or <c>DynamicParameters</c> built from a template - is still the
    /// server's to refuse, and it still answers that the substitution is not set.
    /// </para>
    /// <para>
    /// A read hook or a parameter formatter already on the settings is replaced rather than
    /// composed with; the driver holds one of each. A parameter type resolver is kept instead, and
    /// asked about every type this package does not map.
    /// </para>
    /// </remarks>
    public static ClickHouseClientSettings UseBigDecimalForDapper(this ClickHouseClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ClickHouseClientSettings(settings)
        {
            UseCustomDecimals = true,
            ParameterFormatter = ClickHouseBigDecimal.ParameterFormatter,
            ParameterTypeResolver = ClickHouseBigDecimal.CreateParameterTypeResolver(settings.ParameterTypeResolver),
        };
    }
}
