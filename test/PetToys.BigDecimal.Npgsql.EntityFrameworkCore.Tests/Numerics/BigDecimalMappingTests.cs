using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// What the mapping is, before anything reaches a server.
/// </summary>
/// <remarks>
/// These cases need no container and do not carry the integration category. They are the ones that
/// stay meaningful where Docker is not, and they cover the two guarantees that are invisible at
/// run time: that the default CLR type of <c>numeric</c> did not move, and that no value converter
/// stands in the path.
/// </remarks>
public sealed class BigDecimalMappingTests
{
    [Fact]
    public void RegisteringTheMapping_LeavesDecimalAsTheDefaultForTheStoreType()
    {
        using var context = NumericModel.Offline();
        var source = context.GetService<IRelationalTypeMappingSource>();

        // The assertion this package is built around. The plugin is installed on a context the
        // rest of an application shares, so if these move, code written before the package existed
        // reads a different type out of the same column, at a cast far from here.
        source.FindMapping("numeric")!.ClrType.Should().Be<decimal>(
            "registering the mapping must not change the CLR type the store type answers for");
        source.FindMapping("numeric(12,4)")!.ClrType.Should().Be<decimal>(
            "a declared column is the same store type with facets, not a different one");
        // Asserted on the element rather than on the collection: asked for by store type alone,
        // the provider answers the list form rather than the array one, and which of the two it
        // picks is its business and may differ between EF Core majors. What must not move is that
        // the element is still decimal.
        ElementOf(source.FindMapping("numeric[]")!.ClrType).Should().Be<decimal>(
            "the array default moves with the element default");
    }

    [Fact]
    public void AskingForTheClrType_ReachesThisPackagesMapping()
    {
        using var context = NumericModel.Offline();
        var source = context.GetService<IRelationalTypeMappingSource>();

        var mapping = source.FindMapping(typeof(BigDecimal));

        mapping.Should().BeOfType<BigDecimalTypeMapping>();
        mapping!.StoreType.Should().Be("numeric");
    }

    [Fact]
    public void ThePlugin_DeclinesWhenThereIsNoClrType()
    {
        var plugin = new BigDecimalTypeMappingSourcePlugin();

        // The shapes a model build actually produces: a store type and no CLR type at all. Four of
        // them arrived in one two-property model, and the plugin has to decline them without
        // looking at the store type, since matching one would move the default by a second route.
        foreach (var store in new[] { "text", "jsonb", "tsvector", "lquery", "numeric", "numeric[]" })
        {
            plugin.FindMapping(new RelationalTypeMappingInfo(storeTypeName: store))
                .Should().BeNull($"'{store}' arrives with no CLR type and is not ours to answer for");
        }
    }

    [Fact]
    public void ThePlugin_DeclinesEveryClrTypeButItsOwn()
    {
        var plugin = new BigDecimalTypeMappingSourcePlugin();

        foreach (var clrType in new[] { typeof(decimal), typeof(double), typeof(string), typeof(decimal[]) })
        {
            plugin.FindMapping(new RelationalTypeMappingInfo(clrType)).Should().BeNull();
        }
    }

    [Fact]
    public void TheMapping_CarriesNoConverterAndReadsThroughTheHandler()
    {
        using var context = NumericModel.Offline();
        var mapping = Mapping(context, nameof(NumericItem.Amount));

        // Converter null is what makes the reader method GetFieldValue<BigDecimal>, which is what
        // puts the adapter's handler in the path. A ValueConverter to decimal would displace both
        // and fail nothing.
        mapping.Converter.Should().BeNull();
        mapping.GetDataReaderMethod().Name.Should().Be("GetFieldValue");
        mapping.GetDataReaderMethod().GetGenericArguments().Should().ContainSingle()
            .Which.Should().Be<BigDecimal>();
    }

    [Fact]
    public void ThePublicSurface_OffersNoValueConverter()
    {
        var assembly = typeof(NpgsqlBigDecimalDbContextOptionsBuilderExtensions).Assembly;

        assembly.GetTypes()
            .Where(type => typeof(ValueConverter).IsAssignableFrom(type))
            .Should().BeEmpty("the converter route is the precision loss this package replaces");
    }

    [Fact]
    public void TheComparer_SeparatesWhatTheTypeEquates()
    {
        using var context = NumericModel.Offline();
        var comparer = (ValueComparer<BigDecimal>)Comparer(context, nameof(NumericItem.Amount));

        var one = BigDecimal.Parse("1.5", null);
        var two = BigDecimal.Parse("1.50", null);

        // The type is right and the tracker needs the opposite: numeric equality would leave a
        // scale-only edit Unchanged, and SaveChanges would write nothing at all.
        one.Equals(two).Should().BeTrue("numeric equality is the type's contract");
        comparer.Equals(one, two).Should().BeFalse("the tracker has to see a change of scale");
        comparer.Equals(one, BigDecimal.Parse("1.5", null)).Should().BeTrue();
    }

    [Fact]
    public void TheComparersHash_AgreesWithItsEquality()
    {
        using var context = NumericModel.Offline();
        var comparer = (ValueComparer<BigDecimal>)Comparer(context, nameof(NumericItem.Amount));

        var left = BigDecimal.Parse("1.50", null);
        var right = BigDecimal.Parse("1.50", null);

        comparer.Equals(left, right).Should().BeTrue();
        comparer.GetHashCode(left).Should().Be(comparer.GetHashCode(right),
            "a hash that disagreed with the equality would corrupt the tracker's lookups rather than fail");
    }

    [Fact]
    public void TheNullableForm_ReachesTheElementMappingAndKeepsItsComparer()
    {
        using var context = NumericModel.Offline();

        Mapping(context, nameof(NumericItem.Maybe)).Should().BeOfType<BigDecimalTypeMapping>();

        var comparer = Comparer(context, nameof(NumericItem.Maybe));
        comparer.Equals(BigDecimal.Parse("1.5", null), BigDecimal.Parse("1.50", null))
            .Should().BeFalse("EF wraps our comparer for the nullable form rather than replacing it");
    }

    [Fact]
    public void TheFacets_ReachTheMapping()
    {
        using var context = NumericModel.Offline();
        var mapping = Mapping(context, nameof(NumericItem.Faceted));

        mapping.Precision.Should().Be(12);
        mapping.Scale.Should().Be(4);
    }

    [Fact]
    public void TheSchema_CarriesTheFacetsAndTheArrayTypes()
    {
        using var context = NumericModel.Offline();

        var script = context.Database.GenerateCreateScript();

        // The mapping carrying the facets and the schema declaring them are not the same thing:
        // with StoreTypePostfix.None the first still passes while the column has no limit at all,
        // and the overflow and rescaling behaviour then has nothing to violate.
        script.Should().Contain("\"Faceted\" numeric(12,4)");
        script.Should().Contain("\"Amount\" numeric ");
        script.Should().Contain("\"Amounts\" numeric[]");
        script.Should().Contain("\"More\" numeric[]");
        script.Should().Contain("\"Sparse\" numeric[]");
        script.Should().Contain("\"SparseList\" numeric[]");
    }

    [Fact]
    public void TheCollectionForms_AreAnsweredByThePluginRatherThanTheProvider()
    {
        using var context = NumericModel.Offline();

        // Left to the provider these are NpgsqlArrayTypeMapping, the model is correct in every
        // visible way, and the first write throws InvalidCastException over a parameter type code.
        foreach (var property in new[]
                 {
                     nameof(NumericItem.Amounts), nameof(NumericItem.More),
                     nameof(NumericItem.Sparse), nameof(NumericItem.SparseList),
                 })
        {
            Mapping(context, property).Should().BeOfType<BigDecimalCollectionTypeMapping>(
                $"{property} must not be left to the provider's array mapping");
        }
    }

    [Fact]
    public void TheCollectionComparer_SeesAChangeOfScaleInsideACollection()
    {
        using var context = NumericModel.Offline();

        var one = BigDecimal.Parse("1.5", null);
        var two = BigDecimal.Parse("1.50", null);

        Comparer(context, nameof(NumericItem.Amounts))
            .Equals(new[] { one }, new[] { two }).Should().BeFalse();
        Comparer(context, nameof(NumericItem.More))
            .Equals(new List<BigDecimal> { one }, new List<BigDecimal> { two }).Should().BeFalse();
        Comparer(context, nameof(NumericItem.Sparse))
            .Equals(new BigDecimal?[] { one }, new BigDecimal?[] { two }).Should().BeFalse();
        Comparer(context, nameof(NumericItem.Sparse))
            .Equals(new BigDecimal?[] { null }, new BigDecimal?[] { one }).Should().BeFalse();
        Comparer(context, nameof(NumericItem.Sparse))
            .Equals(new BigDecimal?[] { null }, new BigDecimal?[] { null }).Should().BeTrue();
    }

    [Fact]
    public void AContextWithoutTheRegistration_FailsAtModelBuildingRatherThanSilently()
    {
        using var context = NumericModel.Unregistered();

        // Pinned rather than asserted from a design: the message is Entity Framework's, and the
        // README quotes it so that a consumer who forgot the call recognises what they are looking
        // at. If EF changes the wording, this test is where that shows up.
        var act = () => context.Model.FindEntityType(typeof(NumericItem));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*BigDecimal*");
    }

    private static Type ElementOf(Type collection) =>
        collection.IsArray
            ? collection.GetElementType()!
            : collection.GenericTypeArguments.Single();

    private static RelationalTypeMapping Mapping(NumericContext context, string property) =>
        context.Model.FindEntityType(typeof(NumericItem))!
            .FindProperty(property)!
            .GetRelationalTypeMapping();

    private static ValueComparer Comparer(NumericContext context, string property) =>
        context.Model.FindEntityType(typeof(NumericItem))!
            .FindProperty(property)!
            .GetValueComparer();
}
