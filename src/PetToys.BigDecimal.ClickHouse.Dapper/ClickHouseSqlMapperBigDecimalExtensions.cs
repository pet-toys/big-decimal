using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // Dapper's namespace, where a caller registering a handler already is.

namespace Dapper;

/// <summary>
/// Registers the ClickHouse <see cref="BigDecimal"/> handlers with Dapper.
/// </summary>
/// <remarks>
/// Two calls make this package work: this one registers the type handlers with Dapper, and
/// <c>UseBigDecimalForDapper()</c> on <c>ClickHouseClientSettings</c> puts the driver's
/// arbitrary-precision decimals and the parameter formatter on the connection. Registering the
/// handlers does not change what a decimal column produces for code that never asks for
/// <see cref="BigDecimal"/>, unlike the adapter's connection-wide mapping.
/// </remarks>
public static class ClickHouseSqlMapperBigDecimalExtensions
{
    /// <summary>
    /// Registers the <see cref="ClickHouseBigDecimalTypeHandler"/> and the
    /// <see cref="ClickHouseBigDecimalArrayTypeHandler"/> with Dapper.
    /// </summary>
    /// <remarks>
    /// Dapper's registry is static and process-wide, so this is a start-up call. It is idempotent,
    /// and it silently replaces any handler already registered for <see cref="BigDecimal"/>.
    /// </remarks>
    public static void UseBigDecimal()
    {
        SqlMapper.AddTypeHandler(new ClickHouseBigDecimalTypeHandler());
        SqlMapper.AddTypeHandler(new ClickHouseBigDecimalArrayTypeHandler());
    }
}
