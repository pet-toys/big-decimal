using System;
using Npgsql.Internal;
using Npgsql.Internal.Postgres;

namespace PetToys.BigDecimal.Numerics;

#pragma warning disable NPG9001 // The driver's only extension point for a new type.

/// <summary>
/// Tells Npgsql that <see cref="BigDecimal"/> maps to <c>numeric</c>, and that
/// <c>BigDecimal[]</c> maps to <c>numeric[]</c>.
/// </summary>
/// <remarks>
/// <c>MatchRequirement.All</c> is load bearing: the other two values make <c>GetValue</c> over a
/// <c>numeric</c> column answer <see cref="BigDecimal"/> instead of <see cref="decimal"/>, which
/// would change every untyped read in the application the moment this package is installed.
/// </remarks>
internal sealed class BigDecimalTypeInfoResolverFactory : PgTypeInfoResolverFactory
{
    /// <inheritdoc/>
    public override IPgTypeInfoResolver CreateResolver() => new Resolver();

    /// <inheritdoc/>
    public override IPgTypeInfoResolver CreateArrayResolver() => new ArrayResolver();

    // Spelled out: Npgsql's DataTypeNames is not accessible outside the driver.
    private const string Numeric = "pg_catalog.numeric";

    // AddStructType registers the nullable form beside it, so BigDecimal? comes along.
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

    // The array mapping is derived from the element mapping it finds beside it.
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
