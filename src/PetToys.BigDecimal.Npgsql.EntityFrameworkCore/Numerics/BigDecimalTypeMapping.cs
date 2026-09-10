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
/// <para>
/// Declaring <see cref="BigDecimal"/> as the CLR type is what makes this work: Entity Framework
/// answers <c>GetFieldValue&lt;BigDecimal&gt;</c> as the reader method and leaves
/// <c>Converter</c> null, so the value crosses through the handler
/// <c>PetToys.BigDecimal.Npgsql</c> installs on the data source and never through
/// <see cref="decimal"/>. A <c>ValueConverter&lt;BigDecimal, decimal&gt;</c> would displace both
/// without failing anything, which is the precision loss this package exists to prevent.
/// </para>
/// <para>
/// <see cref="StoreTypePostfix.PrecisionAndScale"/> rather than <see cref="StoreTypePostfix.None"/>
/// is load bearing and not cosmetic. With <c>None</c> the mapping still carries the facets the
/// model declared while the generated column has no limit at all, so every mapping-level
/// assertion passes and the schema quietly drops the constraint. A column with no constraint
/// then has nothing for <see cref="ConfigureParameter"/> to refuse against either.
/// </para>
/// </remarks>
internal sealed class BigDecimalTypeMapping : RelationalTypeMapping
{
    /// <summary>
    /// The change tracker's equality, which is not the type's. Numeric equality makes <c>1.0</c>
    /// equal <c>1.00</c>, so with the default comparer a property moved from <c>1.5</c> to
    /// <c>1.50</c> stays <c>Unchanged</c> and <c>SaveChanges</c> writes nothing - no error, no
    /// wrong value, simply no update. Comparing the scale beside the value is what makes a change
    /// of scale a change.
    /// </summary>
    /// <remarks>
    /// The hash mixes the same two components, because a hash that disagreed with this equality
    /// would corrupt the tracker's lookups rather than fail a test. The snapshot is the identity
    /// function: the type is a readonly struct and there is nothing to deep copy.
    /// </remarks>
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

    /// <summary>Clones the mapping, keeping this type rather than dropping to the base one.</summary>
    /// <remarks>
    /// Entity Framework clones a mapping every time it applies facets to it, so a
    /// <c>Clone</c> that returned a base <see cref="RelationalTypeMapping"/> would lose the
    /// comparer, the literal and the refusal at the first faceted property.
    /// </remarks>
    /// <param name="parameters">The parameters of the clone.</param>
    /// <returns>A mapping of this type over those parameters.</returns>
    protected override RelationalTypeMapping Clone(RelationalTypeMappingParameters parameters) =>
        new BigDecimalTypeMapping(parameters);

    /// <summary>Renders a constant as a <c>numeric</c> literal, cast to the store type.</summary>
    /// <remarks>
    /// The cast is what keeps a comparison against a constant on the server. Without it the
    /// literal is a bare decimal number whose type PostgreSQL infers, and a query that stops
    /// translating does not fail - Entity Framework evaluates on the client what it cannot
    /// translate, so the table would be pulled across the wire and every assertion about the
    /// result would still pass.
    /// <para>
    /// Quoted because three values of this type do not spell themselves as numbers.
    /// <c>NaN::numeric</c> is a reference to a column called <c>nan</c> and fails as
    /// <c>42703: column "nan" does not exist</c>, and the two infinities do the same;
    /// <c>'NaN'::numeric</c> is the value. The quotes cost nothing for a finite value, and the
    /// text of a <see cref="BigDecimal"/> never contains one.
    /// </para>
    /// </remarks>
    /// <param name="value">The value to render.</param>
    /// <returns>The SQL literal.</returns>
    protected override string GenerateNonNullSqlLiteral(object value) =>
        FormattableString.Invariant($"'{(BigDecimal)value}'::{this.StoreType}");

    /// <summary>
    /// Refuses, before the value is sent, anything the declared column cannot hold exactly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The message names the value and the store type and cannot name the property.
    /// <see cref="RelationalTypeMappingInfo"/> carries nothing identifying one, and the parameter
    /// this method is handed is named <c>@p2</c> rather than after the column, so a message that
    /// promised a property name would be promising something this layer cannot produce. The name
    /// is one level up: Entity Framework wraps the failure in a <c>DbUpdateException</c> whose
    /// <c>Entries</c> carry the entity, its state and the property with its current value. The
    /// README shows that code.
    /// </para>
    /// <para>
    /// The check runs only where the model declared facets. An unconstrained <c>numeric</c> has no
    /// limit to violate - PostgreSQL allows 131072 integer digits against this type's 78 - so a
    /// check there would refuse values the server accepts.
    /// </para>
    /// <para>
    /// It refuses two things, and the second is the difference between this package and the
    /// server. An integer part the column cannot hold is what PostgreSQL itself rejects as
    /// <c>22003</c>. A fraction the column cannot keep is what PostgreSQL silently rounds, and
    /// rounding a value the caller handed over whole is a conversion this package declines to take
    /// over. Dropping trailing zeros is not that: <c>1.500</c> into a <c>numeric(12,2)</c> is
    /// <c>1.50</c> and loses nothing, so it is accepted, and the round trip returns the column's
    /// scale as it does for every other value.
    /// </para>
    /// </remarks>
    /// <param name="parameter">The parameter carrying the value.</param>
    /// <exception cref="OverflowException">The value does not fit the declared column.</exception>
    protected override void ConfigureParameter(DbParameter parameter)
    {
        if (this.Precision is not { } precision
            || parameter.Value is not BigDecimal value
            || !BigDecimal.IsFinite(value))
        {
            // PostgreSQL stores NaN and the infinities in a constrained numeric as it does in an
            // unconstrained one, so there is nothing here to refuse them for.
            return;
        }

        // A precision with no scale is numeric(p), which is numeric(p, 0) to PostgreSQL.
        var scale = this.Scale ?? 0;
        var stored = value.Scale > scale ? BigDecimal.Round(value, scale) : value;

        if (!stored.Equals(value))
        {
            Refuse(value);
        }

        // Zero reports one significant digit at every scale, so counting its integer part the way
        // every other value's is counted would make it one digit wide and refuse it from a column
        // whose precision equals its scale - which PostgreSQL accepts.
        if (!stored.IsZero && stored.Precision - stored.Scale > precision - scale)
        {
            Refuse(value);
        }

        void Refuse(BigDecimal refused) =>
            throw new OverflowException(FormattableString.Invariant(
                $"{refused} does not fit {this.StoreType}."));
    }
}
