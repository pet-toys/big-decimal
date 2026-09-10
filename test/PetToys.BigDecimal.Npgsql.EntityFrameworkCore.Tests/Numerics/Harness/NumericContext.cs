using System;
using Microsoft.EntityFrameworkCore;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The context the suite maps <see cref="NumericItem"/> through, with the table name supplied so
/// that test classes running in parallel do not share one.
/// </summary>
/// <param name="options">The options, built by <see cref="NumericModel"/>.</param>
/// <param name="table">The table this context maps the entity to.</param>
public sealed class NumericContext(DbContextOptions<NumericContext> options, string table)
    : DbContext(options)
{
    /// <summary>The entities.</summary>
    public DbSet<NumericItem> Items => this.Set<NumericItem>();

    /// <summary>The table this context maps to.</summary>
    public string Table { get; } = table;

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var item = modelBuilder.Entity<NumericItem>();
        item.ToTable(this.Table);
        item.HasKey(x => x.Id);
        item.Property(x => x.Id).ValueGeneratedNever();
        item.Property(x => x.Faceted).HasPrecision(12, 4);
        item.Property(x => x.Tight).HasPrecision(4, 4);
    }
}
