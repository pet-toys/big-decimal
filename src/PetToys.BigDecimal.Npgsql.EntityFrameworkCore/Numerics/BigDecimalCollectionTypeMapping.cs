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
/// Left to the provider, the array mapping names an explicit provider parameter type, and the
/// first write throws <c>InvalidCastException: Writing values of 'BigDecimal[]' is not
/// supported</c> because that name reaches the driver's default <c>numeric[]</c> mapping rather
/// than the adapter's. This mapping names none, so the driver resolves the parameter by its CLR
/// type. <c>BigDecimal?</c> needs nothing here: Entity Framework wraps the element mapping itself.
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
    /// Literal <c>typeof</c>s rather than the <see cref="Type"/> passed in: the base constructor
    /// wants a type whose public methods survive trimming, which the literal promises.
    /// </remarks>
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
