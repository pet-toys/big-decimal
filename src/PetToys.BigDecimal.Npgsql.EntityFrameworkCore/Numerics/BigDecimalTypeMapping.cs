using System;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Maps a PostgreSQL <c>numeric</c> column to a <see cref="BigDecimal"/> property, with no value
/// converter anywhere in the path.
/// </summary>
/// <remarks>
/// Declaring <see cref="BigDecimal"/> as the CLR type with a null <c>Converter</c> is what routes
/// the value through <c>GetFieldValue&lt;BigDecimal&gt;</c> and the handler on the data source,
/// never through <see cref="decimal"/>. <see cref="StoreTypePostfix.PrecisionAndScale"/> is load
/// bearing: with <c>None</c> the generated column silently drops the declared facets.
/// </remarks>
internal sealed class BigDecimalTypeMapping : RelationalTypeMapping
{
    // Numeric equality makes 1.0 equal 1.00, so with the default comparer a property moved from
    // 1.5 to 1.50 stays Unchanged and SaveChanges writes nothing. The hash mixes the same two.
    private static readonly ValueComparer<BigDecimal> ScaleAwareComparer = new(
        (left, right) => left.Scale == right.Scale && left.Equals(right),
        value => HashCode.Combine(value.GetHashCode(), value.Scale),
        value => value);

    /// <summary>Creates a mapping for a column, carrying the facets the model declared.</summary>
    /// <param name="storeType">The store type name, <c>numeric</c> unless the model named one.</param>
    /// <param name="precision">The declared precision, or <see langword="null"/>.</param>
    /// <param name="scale">The declared scale, or <see langword="null"/>.</param>
    public BigDecimalTypeMapping(string storeType, int? precision, int? scale)
        : base(new RelationalTypeMappingParameters(
            new CoreTypeMappingParameters(typeof(BigDecimal), converter: null, comparer: ScaleAwareComparer),
            storeType,
            StoreTypePostfix.PrecisionAndScale,
            System.Data.DbType.Object,
            unicode: false,
            size: null,
            fixedLength: false,
            precision: precision,
            scale: scale))
    {
    }

    private BigDecimalTypeMapping(RelationalTypeMappingParameters parameters)
        : base(parameters)
    {
    }

    // Entity Framework clones a mapping every time it applies facets, so a Clone returning the
    // base type would lose the comparer, the literal and the refusal at the first faceted property.
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BigDecimalTypeMapping(parameters);

    /// <summary>Renders a constant as a <c>numeric</c> literal, cast to the store type.</summary>
    /// <remarks>
    /// The cast keeps a comparison against a constant on the server; without it Entity Framework
    /// evaluates on the client and pulls the table across the wire. Quoted because
    /// <c>NaN::numeric</c> is a reference to a column called <c>nan</c>.
    /// </remarks>
    protected override string GenerateNonNullSqlLiteral(object value) =>
        FormattableString.Invariant($"'{(BigDecimal)value}'::{this.StoreType}");

    /// <summary>
    /// Refuses, before the value is sent, anything the declared column cannot hold exactly.
    /// </summary>
    /// <remarks>
    /// Only where the model declared facets: an unconstrained <c>numeric</c> has no limit to
    /// violate. Two things are refused - an integer part the column cannot hold, which PostgreSQL
    /// rejects itself, and a fraction the column cannot keep, which PostgreSQL silently rounds.
    /// Dropping trailing zeros is not rounding, so <c>1.500</c> into a <c>numeric(12,2)</c> is
    /// accepted. The message cannot name the property; Entity Framework's <c>DbUpdateException</c>
    /// carries it one level up.
    /// </remarks>
    /// <exception cref="OverflowException">The value does not fit the declared column.</exception>
    protected override void ConfigureParameter(DbParameter parameter)
    {
        if (this.Precision is not { } precision
            || parameter.Value is not BigDecimal value
            || !BigDecimal.IsFinite(value))
        {
            // A constrained numeric stores NaN and the infinities as an unconstrained one does.
            return;
        }

        // numeric(p) is numeric(p, 0) to PostgreSQL.
        var scale = this.Scale ?? 0;
        var stored = value.Scale > scale ? BigDecimal.Round(value, scale) : value;

        if (!stored.Equals(value))
        {
            Refuse(value);
        }

        // Zero reports one significant digit at every scale, and PostgreSQL accepts it in a
        // column whose precision equals its scale.
        if (!stored.IsZero && stored.Precision - stored.Scale > precision - scale)
        {
            Refuse(value);
        }

        void Refuse(BigDecimal refused) =>
            throw new OverflowException(FormattableString.Invariant(
                $"{refused} does not fit {this.StoreType}."));
    }
}
