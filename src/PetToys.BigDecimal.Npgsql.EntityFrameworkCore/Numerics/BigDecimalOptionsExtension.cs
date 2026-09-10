using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Registers the mapping plugin on a context's own service collection.
/// </summary>
/// <remarks>
/// An options extension rather than an internal service provider. Reaching a plugin through
/// <c>AddEntityFrameworkNpgsql().UseInternalServiceProvider(...)</c> works and is not a thing to
/// hand a consumer: it turns off Entity Framework's own service provider caching and makes the
/// caller own a container, for a package that adds one singleton.
/// </remarks>
internal sealed class BigDecimalOptionsExtension : IDbContextOptionsExtension
{
    /// <inheritdoc/>
    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    /// <inheritdoc/>
    public void ApplyServices(IServiceCollection services) =>
        services.AddSingleton<IRelationalTypeMappingSourcePlugin, BigDecimalTypeMappingSourcePlugin>();

    /// <inheritdoc/>
    public void Validate(IDbContextOptions options)
    {
        // Nothing to validate: the extension carries no configuration, and the provider it needs
        // is the one that supplies the builder the registration hangs off.
    }

    /// <summary>
    /// What Entity Framework reads about this extension when it decides whether two contexts can
    /// share a service provider.
    /// </summary>
    /// <remarks>
    /// The hash is constant and the comparison is by type because the extension carries no
    /// configuration. Both move together the moment it gains an option: leaving them as they are
    /// would let a context be served a provider built for the other configuration, which fails
    /// nowhere near the option that changed.
    /// </remarks>
    private sealed class ExtensionInfo(IDbContextOptionsExtension extension)
        : DbContextOptionsExtensionInfo(extension)
    {
        /// <inheritdoc/>
        public override bool IsDatabaseProvider => false;

        /// <inheritdoc/>
        public override string LogFragment => "using BigDecimal ";

        /// <inheritdoc/>
        public override int GetServiceProviderHashCode() => 0;

        /// <inheritdoc/>
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) =>
            other is ExtensionInfo;

        /// <inheritdoc/>
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo) =>
            debugInfo["BigDecimal"] = "1";
    }
}
