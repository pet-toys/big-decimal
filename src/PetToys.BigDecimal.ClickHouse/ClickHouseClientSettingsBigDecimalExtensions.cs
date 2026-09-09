using System;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // The namespace is the one the extended type lives in, on purpose.

namespace ClickHouse.Driver.ADO;

/// <summary>
/// Installs the <see cref="BigDecimal"/> mapping on a whole connection.
/// </summary>
/// <remarks>
/// In the namespace of the type it extends, so that a caller holding
/// <see cref="ClickHouseClientSettings"/> has the call in scope.
/// </remarks>
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
    /// This is the wide form and it is not equivalent to the narrow one. Every decimal column read
    /// on a connection built from these settings maps, so <c>GetValue</c> answers
    /// <see cref="BigDecimal"/> for all of them, including in code written before this package was
    /// referenced. The reader does not agree with itself while it does: <c>GetFieldType</c> keeps
    /// reporting the driver's own decimal type, which nothing outside the driver can change, so
    /// anything that builds a schema from the reader and then fills it sees a column typed for one
    /// type receiving another.
    /// </para>
    /// <para>
    /// It exists because <see cref="ClickHouseCommand"/> has no per-query hook: a caller working
    /// through the ADO surface, or through anything layered on it, has no narrower option. Where
    /// <see cref="ClickHouseClient"/> is in reach, prefer
    /// <see cref="ClickHouseBigDecimal.CreateQueryOptions"/>.
    /// </para>
    /// <para>
    /// <c>UseCustomDecimals</c> is switched on because this call is constructing the settings and
    /// the mapping cannot work without it. A read hook or a parameter formatter already on the
    /// settings is replaced rather than composed with; the driver holds one of each.
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
        };
    }
}
