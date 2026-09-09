using System;
using Npgsql.TypeMapping;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // See the remarks: the namespace is the driver's on purpose.

namespace Npgsql;

/// <summary>
/// Registers the <see cref="BigDecimal"/> mapping for PostgreSQL <c>numeric</c>.
/// </summary>
/// <remarks>
/// <para>
/// In the driver's own namespace, which is where every Npgsql plugin puts its registration call, so
/// that <c>builder.UseBigDecimal()</c> needs no <c>using</c> a caller holding an
/// <see cref="NpgsqlDataSourceBuilder"/> does not already have. It is the one place this package
/// declares a namespace it does not own, and IDE0130 is suppressed here rather than for the project
/// for that reason.
/// </para>
/// </remarks>
public static class NpgsqlBigDecimalExtensions
{
    /// <summary>
    /// Maps PostgreSQL <c>numeric</c> to <see cref="BigDecimal"/>, and <c>numeric[]</c> to
    /// <c>BigDecimal[]</c>, on the data source being built.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This does not change what the driver answers by default. A <c>numeric</c> column read
    /// through <c>GetValue</c> is still a <see cref="decimal"/>, <c>GetFieldType</c> still reports
    /// <see cref="decimal"/>, and code written before this package was referenced reads exactly
    /// what it read before. <see cref="BigDecimal"/> is reached by asking for it, with
    /// <c>GetFieldValue&lt;BigDecimal&gt;</c>, with one of the accessors in
    /// <see cref="NpgsqlBigDecimalReaderExtensions"/>, or by giving a parameter a value of that
    /// type.
    /// </para>
    /// <para>
    /// This overload is the implementation, and it is on <see cref="INpgsqlTypeMapper"/> so that a
    /// caller configuring a data source through a callback reaches it whatever kind of builder is
    /// in hand. The two builder overloads beside it exist only to return the builder, so that
    /// <c>new NpgsqlDataSourceBuilder(...).UseBigDecimal().Build()</c> reads the way every other
    /// Npgsql plugin reads; they carry no logic of their own and cannot drift from this one.
    /// </para>
    /// </remarks>
    /// <param name="mapper">The type mapper, which both data source builders implement.</param>
    /// <returns>The same mapper, so the call chains.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mapper"/> is
    /// <see langword="null"/>.</exception>
    public static INpgsqlTypeMapper UseBigDecimal(this INpgsqlTypeMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

#pragma warning disable NPG9001 // See BigDecimalConverter: this is the driver's only extension point.
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
