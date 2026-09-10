using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Tells Entity Framework that this suite's model depends on the table name the context was given.
/// </summary>
/// <remarks>
/// Without this the model is cached per context type, and the first context built in the process
/// decides the table name for every later one - so the model-level cases, which map
/// <c>Items</c> and never open a connection, hand their model to the server-backed cases, which
/// then drop a table they did not create and create one that already exists. EF Core 9 happened
/// not to reuse the model across these two configurations and EF Core 10 does, which is the whole
/// argument for running the suite against both majors: the defect was in this harness the entire
/// time and only the second run said so.
/// </remarks>
public sealed class NumericModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc/>
    public object Create(DbContext context, bool designTime) =>
        context is NumericContext numeric
            ? (context.GetType(), numeric.Table, designTime)
            : (context.GetType(), designTime);
}
