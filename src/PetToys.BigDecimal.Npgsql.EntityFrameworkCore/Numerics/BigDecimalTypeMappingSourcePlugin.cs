using Microsoft.EntityFrameworkCore.Storage;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Answers Entity Framework's mapping source for <see cref="BigDecimal"/> and its collection
/// forms, and for nothing else.
/// </summary>
/// <remarks>
/// Declines on the CLR type before anything else and never matches on a store type alone: a
/// plugin answering for a bare <c>numeric</c> would make <see cref="BigDecimal"/> the default CLR
/// type of that column for the whole provider.
/// </remarks>
internal sealed class BigDecimalTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    /// <summary>Finds the mapping for a property, or declines.</summary>
    public RelationalTypeMapping? FindMapping(in RelationalTypeMappingInfo mappingInfo)
    {
        if (mappingInfo.ClrType is not { } clrType)
        {
            return null;
        }

        if (clrType == typeof(BigDecimal))
        {
            return new BigDecimalTypeMapping(
                mappingInfo.StoreTypeName ?? "numeric",
                mappingInfo.Precision,
                mappingInfo.Scale);
        }

        return BigDecimalCollectionTypeMapping.For(clrType, mappingInfo.StoreTypeName ?? "numeric[]");
    }
}
