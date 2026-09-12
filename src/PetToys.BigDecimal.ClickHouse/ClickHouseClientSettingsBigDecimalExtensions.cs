using System;
using ClickHouse.Driver.ADO;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // The driver's namespace, the one a ClickHouse caller already imports.

namespace ClickHouse.Driver;

/// <summary>
/// Installs the <see cref="BigDecimal"/> mapping on a whole connection.
/// </summary>
public static class ClickHouseClientSettingsBigDecimalExtensions
{
    /// <summary>
    /// Returns settings that map ClickHouse decimal columns to <see cref="BigDecimal"/>, with the
    /// driver's arbitrary-precision decimals switched on.
    /// </summary>
    /// <param name="settings">The settings to build on.</param>
    /// <returns>New settings; <paramref name="settings"/> is not modified.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="settings"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The wide form: every decimal column read on a connection built from these settings maps,
    /// so <c>GetValue</c> answers <see cref="BigDecimal"/> for all of them, including in code
    /// written before this package was referenced, while <c>GetFieldType</c> keeps reporting the
    /// driver's own decimal type. It exists because <see cref="ClickHouseCommand"/> has no
    /// per-query hook; where <see cref="ClickHouseClient"/> is in reach, prefer
    /// <see cref="ClickHouseBigDecimal.CreateQueryOptions"/>.
    /// </para>
    /// <para>
    /// A read hook or a parameter formatter already on the settings is replaced, since neither
    /// interface lets an implementation say a value is not its own. A parameter type resolver is
    /// kept and asked about every type this package does not map; what this package's adds is a
    /// refusal, by name, for a <see cref="BigDecimal"/> parameter the statement did not annotate.
    /// </para>
    /// </remarks>
    public static ClickHouseClientSettings UseBigDecimal(this ClickHouseClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new ClickHouseClientSettings(settings)
        {
            UseCustomDecimals = true,
            ReadValueConverter = ClickHouseBigDecimal.ReadValueConverter,
            ParameterFormatter = ClickHouseBigDecimal.ParameterFormatter,
            ParameterTypeResolver = ClickHouseBigDecimal.CreateParameterTypeResolver(settings.ParameterTypeResolver),
        };
    }
}
