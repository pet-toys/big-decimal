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
/// <para>
/// The mapping matches on the CLR type and the data type name together, and that is the load
/// bearing decision in this package rather than a detail. Measured against PostgreSQL 18, the three
/// values of <see cref="MatchRequirement"/> do not differ by degree:
/// </para>
/// <list type="bullet">
/// <item>
/// <c>Single</c>: <c>GetFieldValue&lt;BigDecimal&gt;</c> works, a parameter carrying only a value
/// works, and <c>GetValue</c> over a <c>numeric</c> column starts answering <see cref="BigDecimal"/>
/// instead of <see cref="decimal"/>.
/// </item>
/// <item>
/// <c>DataTypeName</c>: the same hijack of the default, and a parameter carrying only a value fails
/// with <c>Writing values of 'BigDecimal' is not supported for parameters having no NpgsqlDbType or
/// DataTypeName</c>.
/// </item>
/// <item>
/// <c>All</c>: reads and writes both work, and <see cref="decimal"/> stays the default the driver
/// answers for the type.
/// </item>
/// </list>
/// <para>
/// Only the last is admissible. A type mapping is registered on a data source the whole application
/// shares, so a default that moved would change what every existing untyped read produces from the
/// moment this package is installed, and it would surface at a cast far away from the registration.
/// <see cref="BigDecimal"/> is for the columns that need it, asked for by name.
/// </para>
/// </remarks>
internal sealed class BigDecimalTypeInfoResolverFactory : PgTypeInfoResolverFactory
{
    /// <inheritdoc/>
    public override IPgTypeInfoResolver CreateResolver() => new Resolver();

    /// <inheritdoc/>
    public override IPgTypeInfoResolver CreateArrayResolver() => new ArrayResolver();

    /// <summary>
    /// The <c>numeric</c> type, fully qualified. Npgsql's own <c>DataTypeNames</c> holds this
    /// constant but the class is not accessible outside the driver, so the name is spelled out
    /// rather than referenced.
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
