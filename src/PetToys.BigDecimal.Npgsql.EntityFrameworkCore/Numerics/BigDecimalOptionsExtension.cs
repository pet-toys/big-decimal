using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Registers the mapping plugin on a context's own service collection.
/// </summary>
/// <remarks>
/// An options extension rather than <c>UseInternalServiceProvider</c>, which would make the caller
/// own a container for a package that adds one singleton.
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
        // Nothing to validate: the extension carries no configuration.
    }

    // The hash is constant and the comparison is by type because the extension carries no
    // configuration; both have to move the moment it gains an option.
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
