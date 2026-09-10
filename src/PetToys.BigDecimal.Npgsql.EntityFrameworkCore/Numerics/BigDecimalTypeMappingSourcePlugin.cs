using Microsoft.EntityFrameworkCore.Storage;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Answers Entity Framework's mapping source for <see cref="BigDecimal"/> and its collection
/// forms, and for nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The rule is to decline on the CLR type before examining anything else, and never to match on a
/// store type alone. Both halves are load bearing. Declining first is the common path: building
/// one two-property model called this twenty-seven times, twenty-three of them for types it does
/// not map, four of those carrying a store type and no CLR type at all - <c>text</c>,
/// <c>jsonb</c>, <c>tsvector</c> and <c>lquery</c>. And a plugin that answered for a bare
/// <c>numeric</c> would make <see cref="BigDecimal"/> the default CLR type of that column for the
/// whole provider, which is the one thing this package must not do.
/// </para>
/// <para>
/// That guarantee is structural rather than a flag: Entity Framework keys its mapping source by
/// CLR type as well as by store type, so a plugin that answers only for its own CLR types cannot
/// take a store type over. It is worth stating as a decision because the sibling Dapper package
/// for the same server cannot make it without giving up exact reads.
/// </para>
/// </remarks>
internal sealed class BigDecimalTypeMappingSourcePlugin : IRelationalTypeMappingSourcePlugin
{
    /// <summary>Finds the mapping for a property, or declines.</summary>
    /// <param name="mappingInfo">What Entity Framework knows about the property and column.</param>
    /// <returns>The mapping, or <see langword="null"/> to leave the type to somebody else.</returns>
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
