using System;
using ClickHouse.Driver.ADO;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // The driver's namespace, the one a ClickHouse caller already imports.

namespace ClickHouse.Driver;

/// <summary>
/// Configures a connection for a Dapper caller who maps <see cref="BigDecimal"/>.
/// </summary>
/// <remarks>
/// The method name differs from the adapter's <c>UseBigDecimal</c> because both extend
/// <see cref="ClickHouseClientSettings"/> from this namespace, and every consumer of this package
/// imports both.
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
    /// The narrow form: the read hook is not part of it, because the Dapper handlers widen a
    /// column when a member names the type. A caller who has already used the adapter's
    /// <c>UseBigDecimal</c> needs nothing from here, since it installs this formatter too.
    /// </para>
    /// <para>
    /// The formatter is what makes a write exact: without it the driver converts the parameter
    /// through <see cref="IConvertible"/>, which <see cref="BigDecimal"/> does not implement, and
    /// fails naming neither the column nor the value. The parameter type resolver refuses a
    /// parameter the statement did not annotate, by name; a parameter Dapper removes before any
    /// of this reaches it is still the server's to refuse.
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
