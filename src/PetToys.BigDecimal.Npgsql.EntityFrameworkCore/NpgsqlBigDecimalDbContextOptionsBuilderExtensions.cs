using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using PetToys.BigDecimal.Numerics;

// Entity Framework's namespace on purpose: it is the one a consumer already has open where they
// configure a context, and it is where every provider plugin puts its registration call.
#pragma warning disable IDE0130

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// The registration: <c>UseNpgsql(dataSource, o =&gt; o.UseBigDecimal())</c>.
/// </summary>
/// <remarks>
/// <para>
/// In Entity Framework's own namespace, which is where every provider plugin puts its registration
/// call and the namespace a consumer already has open where they configure a context. It declares
/// a namespace it does not own, which is what IDE0130 is about, so the rule is suppressed here
/// rather than for the project.
/// </para>
/// <para>
/// The receiver is the provider's builder rather than the bare
/// <see cref="DbContextOptionsBuilder"/>. An extension on the bare builder is a lighter dependency
/// graph and the wrong shape: it compiles against a SQL Server context too, where <c>numeric</c>
/// also exists, where this mapping is wrong, and where the failure lands far from the call. The
/// narrower receiver is the one that cannot lie, which is the argument that put the adapter's
/// reader accessors on <c>NpgsqlDbDataReader</c> rather than on <c>DbDataReader</c>. It is also
/// the only type this package needs from the provider; everything else here compiles against
/// <c>Microsoft.EntityFrameworkCore.Relational</c>.
/// </para>
/// </remarks>
public static class NpgsqlBigDecimalDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Maps <c>numeric</c> columns to <see cref="BigDecimal"/> properties on this context.
    /// </summary>
    /// <remarks>
    /// This is one of two registrations and neither works alone. This one tells Entity Framework
    /// which store type a <see cref="BigDecimal"/> property has; the codec that carries the value
    /// is installed by <c>PetToys.BigDecimal.Npgsql</c> on the data source, which the caller
    /// builds and hands to <c>UseNpgsql</c>. A context registered here over a data source without
    /// that call has no handler to read or write through.
    /// </remarks>
    /// <param name="builder">The provider's options builder.</param>
    /// <returns>The same builder, so the call chains.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static NpgsqlDbContextOptionsBuilder UseBigDecimal(this NpgsqlDbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // IRelationalDbContextOptionsBuilderInfrastructure is EF Core relational's own public
        // route from a provider's builder back to the outer one, which is where an extension has
        // to be added. Nothing here reaches into the provider.
        var options = ((IRelationalDbContextOptionsBuilderInfrastructure)builder).OptionsBuilder;

        ((IDbContextOptionsBuilderInfrastructure)options)
            .AddOrUpdateExtension(new BigDecimalOptionsExtension());

        return builder;
    }
}
