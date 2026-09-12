using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using PetToys.BigDecimal.Numerics;

#pragma warning disable IDE0130 // Entity Framework's namespace, where every provider plugin puts its registration.

namespace Microsoft.EntityFrameworkCore;

/// <summary>
/// The registration: <c>UseNpgsql(dataSource, o =&gt; o.UseBigDecimal())</c>.
/// </summary>
/// <remarks>
/// The receiver is the provider's builder rather than the bare
/// <see cref="DbContextOptionsBuilder"/>, which would compile against a SQL Server context too,
/// where <c>numeric</c> exists and this mapping is wrong. It is the only type this package needs
/// from the provider.
/// </remarks>
public static class NpgsqlBigDecimalDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Maps <c>numeric</c> columns to <see cref="BigDecimal"/> properties on this context.
    /// </summary>
    /// <remarks>
    /// One of two registrations, and neither works alone: this one tells Entity Framework which
    /// store type a <see cref="BigDecimal"/> property has, and <c>PetToys.BigDecimal.Npgsql</c>
    /// installs the codec on the data source the caller hands to <c>UseNpgsql</c>.
    /// </remarks>
    /// <param name="builder">The provider's options builder.</param>
    /// <returns>The same builder, so the call chains.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    public static NpgsqlDbContextOptionsBuilder UseBigDecimal(this NpgsqlDbContextOptionsBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // EF Core's own public route from a provider's builder back to the outer one.
        var options = ((IRelationalDbContextOptionsBuilderInfrastructure)builder).OptionsBuilder;

        ((IDbContextOptionsBuilderInfrastructure)options)
            .AddOrUpdateExtension(new BigDecimalOptionsExtension());

        return builder;
    }
}
