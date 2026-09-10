using System;
using Npgsql.Internal;
using Npgsql.Internal.Postgres;

namespace PetToys.BigDecimal.Numerics;

#pragma warning disable NPG9001 // See BigDecimalConverter: this is the driver's only extension point.

/// <summary>
/// Tells Npgsql that <see cref="BigDecimal"/> maps to <c>numeric</c>, and that
/// <c>BigDecimal[]</c> maps to <c>numeric[]</c>.
/// </summary>
/// <remarks>
/// <c>MatchRequirement.All</c> is the load-bearing decision here, not a detail. Measured against
/// PostgreSQL 18: the other two values make <c>GetValue</c> over a <c>numeric</c> column answer
/// <see cref="BigDecimal"/> instead of <see cref="decimal"/>, and <c>DataTypeName</c> additionally
/// refuses a parameter carrying only a value. The mapping is registered on a data source the whole
/// application shares, so a default that moved would change every existing untyped read from the
/// moment this package is installed, and would surface at a cast far from the registration.
/// </remarks>
internal sealed class BigDecimalTypeInfoResolverFactory : PgTypeInfoResolverFactory
{
    /// <inheritdoc/>
    public override IPgTypeInfoResolver CreateResolver() => new Resolver();

    /// <inheritdoc/>
    public override IPgTypeInfoResolver CreateArrayResolver() => new ArrayResolver();

    /// <summary>
    /// The <c>numeric</c> type, fully qualified. Spelled out because Npgsql's own
    /// <c>DataTypeNames</c> is not accessible outside the driver.
    /// </summary>
    private const string Numeric = "pg_catalog.numeric";

    /// <summary>
    /// Adds the element mapping. <c>AddStructType</c> registers the nullable form beside it, so
    /// <c>BigDecimal?</c> comes along and a NULL column does not need a mapping of its own.
    /// </summary>
    /// <param name="mappings">The collection being built.</param>
    private static void AddElement(TypeInfoMappingCollection mappings) =>
        mappings.AddStructType<BigDecimal>(
            Numeric,
            static (options, mapping, _) => mapping.CreateInfo(options, new BigDecimalConverter()),
            MatchRequirement.All);

    private sealed class Resolver : IPgTypeInfoResolver
    {
        private readonly TypeInfoMappingCollection mappings = new();

        public Resolver() => AddElement(this.mappings);

        public PgTypeInfo? GetTypeInfo(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options) =>
            this.mappings.Find(type, dataTypeName, options);
    }

    /// <summary>
    /// The array side. The element mapping is added to this collection too, because the array
    /// mapping is derived from the element mapping it finds beside it.
    /// </summary>
    private sealed class ArrayResolver : IPgTypeInfoResolver
    {
        private readonly TypeInfoMappingCollection mappings = new();

        public ArrayResolver()
        {
            AddElement(this.mappings);
            this.mappings.AddStructArrayType<BigDecimal>(Numeric);
        }

        public PgTypeInfo? GetTypeInfo(Type? type, DataTypeName? dataTypeName, PgSerializerOptions options) =>
            this.mappings.Find(type, dataTypeName, options);
    }
}
