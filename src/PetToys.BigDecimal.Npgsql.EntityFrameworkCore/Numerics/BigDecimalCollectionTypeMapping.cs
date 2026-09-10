using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Maps a PostgreSQL <c>numeric[]</c> column to one of the four collection forms of
/// <see cref="BigDecimal"/>: an array or a list, of the value type or of its nullable form.
/// </summary>
/// <remarks>
/// <para>
/// This mapping exists because leaving the collection forms to the provider is not the cheap
/// option it looks like. Left alone, the provider builds an array mapping over the element mapping
/// and everything visible is right: the model builds, the generated schema declares
/// <c>numeric[]</c>, and the element comparer even reaches the collection comparer, so an edit
/// inside an array is still seen. The first write then throws
/// <c>InvalidCastException: Writing values of 'BigDecimal[]' is not supported for parameters
/// having NpgsqlDbType '-2147483608'</c>, because that mapping names an explicit provider
/// parameter type, and resolving a parameter by that name reaches the driver's default
/// <c>numeric[]</c> mapping rather than the one the adapter registered, which matches on the CLR
/// type as well. Declining the collections would have cost the same mapping, since that failure is
/// what declining actually produces.
/// </para>
/// <para>
/// So this mapping names no provider parameter type, which leaves the driver to resolve the
/// parameter by its CLR type and find the adapter's registration. One mapping serves all four
/// forms because they differ only in the CLR type Entity Framework matches on and in the comparer;
/// the nullable element form <see cref="BigDecimal"/>? needs nothing here at all, since Entity
/// Framework reaches the element mapping for it and wraps the comparer itself.
/// </para>
/// </remarks>
internal sealed class BigDecimalCollectionTypeMapping : RelationalTypeMapping
{
    private static readonly ValueComparer<BigDecimal[]> ArrayComparer = new(
        (left, right) => ElementsEqual(left, right),
        value => Hash(value),
        value => (BigDecimal[])value.Clone());

    private static readonly ValueComparer<List<BigDecimal>> ListComparer = new(
        (left, right) => ElementsEqual(left, right),
        value => Hash(value),
        value => new List<BigDecimal>(value));

    private static readonly ValueComparer<BigDecimal?[]> NullableArrayComparer = new(
        (left, right) => ElementsEqual(left, right),
        value => Hash(value),
        value => (BigDecimal?[])value.Clone());

    private static readonly ValueComparer<List<BigDecimal?>> NullableListComparer = new(
        (left, right) => ElementsEqual(left, right),
        value => Hash(value),
        value => new List<BigDecimal?>(value));

    private BigDecimalCollectionTypeMapping(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type clrType,
        string storeType,
        ValueComparer comparer)
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(clrType, converter: null, comparer: comparer),
            storeType,
            StoreTypePostfix.None,
            System.Data.DbType.Object))
    {
    }

    private BigDecimalCollectionTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    /// <summary>
    /// The mapping for a collection form, or <see langword="null"/> for anything else.
    /// </summary>
    /// <remarks>
    /// The four forms are spelled out here as literals rather than passed through as a
    /// <see cref="Type"/> because the trim analyzer is right to object to the latter: the base
    /// constructor wants a type whose public methods survive trimming, and a
    /// <see cref="Type"/> arriving from Entity Framework's mapping information carries no such
    /// promise. A literal <c>typeof</c> does, so the closed set is written out and the gate stays
    /// a gate rather than a suppression.
    /// </remarks>
    /// <param name="clrType">The CLR type the model declared.</param>
    /// <param name="storeType">The store type name, <c>numeric[]</c> unless the model named one.</param>
    /// <returns>The mapping, or <see langword="null"/>.</returns>
    public static BigDecimalCollectionTypeMapping? For(Type clrType, string storeType)
    {
        if (clrType == typeof(BigDecimal[]))
        {
            return new BigDecimalCollectionTypeMapping(typeof(BigDecimal[]), storeType, ArrayComparer);
        }

        if (clrType == typeof(List<BigDecimal>))
        {
            return new BigDecimalCollectionTypeMapping(typeof(List<BigDecimal>), storeType, ListComparer);
        }

        if (clrType == typeof(BigDecimal?[]))
        {
            return new BigDecimalCollectionTypeMapping(
                typeof(BigDecimal?[]), storeType, NullableArrayComparer);
        }

        if (clrType == typeof(List<BigDecimal?>))
        {
            return new BigDecimalCollectionTypeMapping(
                typeof(List<BigDecimal?>), storeType, NullableListComparer);
        }

        return null;
    }

    /// <inheritdoc/>
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BigDecimalCollectionTypeMapping(parameters);

    private static bool ElementsEqual(IReadOnlyList<BigDecimal>? left, IReadOnlyList<BigDecimal>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].Scale != right[i].Scale || !left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ElementsEqual(IReadOnlyList<BigDecimal?>? left, IReadOnlyList<BigDecimal?>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] is not { } one)
            {
                if (right[i] is not null)
                {
                    return false;
                }

                continue;
            }

            if (right[i] is not { } other || one.Scale != other.Scale || !one.Equals(other))
            {
                return false;
            }
        }

        return true;
    }

    private static int Hash(IReadOnlyList<BigDecimal> value)
    {
        var hash = default(HashCode);

        for (var i = 0; i < value.Count; i++)
        {
            hash.Add(value[i].GetHashCode());
            hash.Add(value[i].Scale);
        }

        return hash.ToHashCode();
    }

    private static int Hash(IReadOnlyList<BigDecimal?> value)
    {
        var hash = default(HashCode);

        for (var i = 0; i < value.Count; i++)
        {
            hash.Add(value[i]?.GetHashCode() ?? 0);
            hash.Add(value[i]?.Scale ?? -1);
        }

        return hash.ToHashCode();
    }
}
