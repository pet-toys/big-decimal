using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // See ClickHouseBigDecimalTypeHandler: the namespace is Dapper's.

namespace Dapper;

/// <summary>
/// Registers the ClickHouse <see cref="BigDecimal"/> handlers with Dapper.
/// </summary>
/// <remarks>
/// <para>
/// Two calls make this package work and they are not interchangeable. This one registers the type
/// handlers with Dapper, which is the whole read side and what carries a parameter of the type.
/// <c>UseBigDecimalForDapper()</c> on <c>ClickHouseClientSettings</c> is what puts the driver's
/// arbitrary-precision decimals and this repository's parameter formatter on the connection.
/// </para>
/// <para>
/// Registering the handlers does not change what a decimal column produces for code that never
/// asks for <see cref="BigDecimal"/>. An untyped read still answers the driver's own decimal,
/// <c>GetFieldType</c> still reports it, and a dynamic query still answers it. That is the
/// difference between this scope and the adapter's connection-wide mapping, which moves
/// <c>GetValue</c> for every decimal column on the connection.
/// </para>
/// </remarks>
public static class ClickHouseSqlMapperBigDecimalExtensions
{
    /// <summary>
    /// Registers the <see cref="ClickHouseBigDecimalTypeHandler"/> and the
    /// <see cref="ClickHouseBigDecimalArrayTypeHandler"/> with Dapper.
    /// </summary>
    /// <remarks>
    /// Dapper's handler registry is static and process-wide - it takes no scope argument - so this
    /// is a process-level act, and calling it from application start-up is the shape that matches
    /// what it does. It is idempotent. It also replaces any handler already registered for
    /// <see cref="BigDecimal"/>: Dapper accepts a second registration silently and publishes no
    /// way to read the registry back, so a caller with a handler of their own registers theirs
    /// after this one, or not at all.
    /// </remarks>
    public static void UseBigDecimal()
    {
        SqlMapper.AddTypeHandler(new ClickHouseBigDecimalTypeHandler());
        SqlMapper.AddTypeHandler(new ClickHouseBigDecimalArrayTypeHandler());
    }
}
