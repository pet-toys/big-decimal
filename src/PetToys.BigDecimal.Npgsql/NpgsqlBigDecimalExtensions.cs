using System;
using Npgsql.TypeMapping;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // The driver's namespace, where every Npgsql plugin puts its registration.

namespace Npgsql;

/// <summary>
/// Registers the <see cref="BigDecimal"/> mapping for PostgreSQL <c>numeric</c>.
/// </summary>
public static class NpgsqlBigDecimalExtensions
{
    /// <summary>
    /// Maps PostgreSQL <c>numeric</c> to <see cref="BigDecimal"/>, and <c>numeric[]</c> to
    /// <c>BigDecimal[]</c>, on the data source being built.
    /// </summary>
    /// <remarks>
    /// This does not change what the driver answers by default: a <c>numeric</c> column read
    /// through <c>GetValue</c> is still a <see cref="decimal"/>, and <c>GetFieldType</c> still
    /// reports it. <see cref="BigDecimal"/> is reached by asking for it, with
    /// <c>GetFieldValue&lt;BigDecimal&gt;</c>, an accessor in
    /// <see cref="NpgsqlBigDecimalReaderExtensions"/>, or a parameter value of that type. This
    /// overload is the implementation; the two builder overloads exist to return the builder.
    /// </remarks>
    /// <param name="mapper">The type mapper, which both data source builders implement.</param>
    /// <returns>The same mapper, so the call chains.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mapper"/> is
    /// <see langword="null"/>.</exception>
    public static INpgsqlTypeMapper UseBigDecimal(this INpgsqlTypeMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

#pragma warning disable NPG9001 // The driver's only extension point for a new type.
        mapper.AddTypeInfoResolverFactory(new BigDecimalTypeInfoResolverFactory());
#pragma warning restore NPG9001

        return mapper;
    }

    /// <summary>
    /// Maps PostgreSQL <c>numeric</c> to <see cref="BigDecimal"/> on the data source being built,
    /// and returns the builder so the call chains.
    /// </summary>
    /// <param name="builder">The data source builder.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is
    /// <see langword="null"/>.</exception>
    public static NpgsqlDataSourceBuilder UseBigDecimal(this NpgsqlDataSourceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        UseBigDecimal((INpgsqlTypeMapper)builder);

        return builder;
    }

    /// <summary>
    /// Maps PostgreSQL <c>numeric</c> to <see cref="BigDecimal"/> on the slim data source being
    /// built, and returns the builder so the call chains.
    /// </summary>
    /// <param name="builder">The slim data source builder.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is
    /// <see langword="null"/>.</exception>
    public static NpgsqlSlimDataSourceBuilder UseBigDecimal(this NpgsqlSlimDataSourceBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        UseBigDecimal((INpgsqlTypeMapper)builder);

        return builder;
    }
}
