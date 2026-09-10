using System;
using System.Collections.Generic;
using System.Globalization;
using AwesomeAssertions;
using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The write-side guard, asserted without a server: every case here is one where nothing is ever
/// sent.
/// </summary>
/// <remarks>
/// The driver consults a resolver only for a parameter the statement did not annotate, so being
/// asked at all means the column's type is unknown. These cases are about what the guard does with
/// that question - refuse for the types this package maps, and get out of the way for every other.
/// </remarks>
public sealed class BigDecimalParameterTypeResolverTests
{
    private static readonly BigDecimal[] OneValue = [BigDecimal.One];

    private static readonly string[] OneString = ["text"];

    /// <summary>The types this package maps, with the annotation each one's message shows.</summary>
    public static TheoryData<Type, object, string> Mapped =>
        new()
        {
            { typeof(BigDecimal), BigDecimal.One, "Decimal256(6)" },
            { typeof(BigDecimal?), BigDecimal.One, "Decimal256(6)" },
            { typeof(BigDecimal[]), OneValue, "Array(Decimal256(6))" },
        };

    /// <summary>Types this package has no opinion about, of the kinds a statement carries.</summary>
    public static TheoryData<Type, object> Foreign =>
        new()
        {
            { typeof(string), "text" },
            { typeof(int), 42 },
            { typeof(decimal), 1.5m },
            { typeof(DateTime), DateTime.UnixEpoch },
            { typeof(string[]), OneString },
        };

    [Theory]
    [MemberData(nameof(Mapped))]
    public void AMappedTypeWithNoAnnotation_IsRefusedByNameAndShowsTheAnnotation(
        Type type,
        object value,
        string annotation)
    {
        var resolver = ClickHouseBigDecimal.CreateParameterTypeResolver();

        var resolving = () => resolver.ResolveType(type, value, "total");

        resolving.Should().Throw<InvalidOperationException>()
            .WithMessage("*'total'*", "the caller has to be told which parameter it was")
            .WithMessage($"*{{total:{annotation}}}*", "and the shape that fixes it, with their own name in it")
            .WithMessage("*DynamicParameters.Add*", "which is the other half a Dapper caller needs");
    }

    [Theory]
    [MemberData(nameof(Foreign))]
    public void AForeignType_IsDeferredRatherThanAnswered(Type type, object value)
    {
        var resolver = ClickHouseBigDecimal.CreateParameterTypeResolver();

        // Nothing, rather than a type name. The driver reads that as deferral and infers the type
        // itself, which is what makes installing this guard invisible to every parameter but ours.
        // A resolver that answered here would declare unrelated parameters decimals.
        resolver.ResolveType(type, value, "other").Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Foreign))]
    public void AForeignType_ReachesAResolverTheCallerAlreadyInstalled(Type type, object value)
    {
        var inner = new RecordingResolver("UInt8");
        var resolver = ClickHouseBigDecimal.CreateParameterTypeResolver(inner);

        resolver.ResolveType(type, value, "other").Should().Be("UInt8");

        inner.Asked.Should().Equal(["other"], "the inner resolver decides its own types");
    }

    [Fact]
    public void AMappedType_IsRefusedWithoutConsultingTheCallersResolver()
    {
        var inner = new RecordingResolver("Decimal64(4)");
        var resolver = ClickHouseBigDecimal.CreateParameterTypeResolver(inner);

        var resolving = () => resolver.ResolveType(typeof(BigDecimal), BigDecimal.One, "total");

        resolving.Should().Throw<InvalidOperationException>();

        // The inner resolver would have answered, and its answer would have been a type derived
        // from something other than the column. Composition is for the types this package does not
        // map, not a way around its own refusal.
        inner.Asked.Should().BeEmpty();
    }

    [Fact]
    public void ComposingTheGuardWithItself_AddsNoLayer()
    {
        var first = ClickHouseBigDecimal.CreateParameterTypeResolver();

        // UseBigDecimal followed by UseBigDecimalForDapper is a composition the documentation
        // invites, and each call passes whatever is on the settings as the inner resolver. Wrapping
        // our own would add a layer per call that every parameter of every other type then walks.
        ClickHouseBigDecimal.CreateParameterTypeResolver(first).Should().BeSameAs(first);
    }

    [Fact]
    public void ComposingTheGuardWithSomebodyElses_KeepsIt()
    {
        var caller = new RecordingResolver("UInt8");

        var guard = ClickHouseBigDecimal.CreateParameterTypeResolver(caller);

        guard.Should().NotBeSameAs(caller);
        guard.ResolveType(typeof(string), "text", "other").Should().Be("UInt8");
    }

    [Fact]
    public void TheRefusalMessage_IsRenderedInvariantly()
    {
        var resolver = ClickHouseBigDecimal.CreateParameterTypeResolver();

        var resolving = () => resolver.ResolveType(typeof(BigDecimal), BigDecimal.One, "итог");

        // The parameter name is the caller's and is not a number, but the message is composed the
        // same way the codec's refusals are, so the culture it is built under is pinned here rather
        // than assumed.
        resolving.Should().Throw<InvalidOperationException>()
            .WithMessage("*'итог'*", CultureInfo.InvariantCulture.Name);
    }

    /// <summary>A resolver of the kind a caller installs for their own types.</summary>
    /// <param name="answer">The type it names for everything it is asked about.</param>
    private sealed class RecordingResolver(string answer) : IParameterTypeResolver
    {
        /// <summary>The parameter names it was asked about, in order.</summary>
        public List<string> Asked { get; } = [];

        /// <summary>Records the question and answers it.</summary>
        /// <param name="type">The parameter value's type.</param>
        /// <param name="value">The parameter's value.</param>
        /// <param name="name">The parameter's name.</param>
        /// <returns>The one answer this gives.</returns>
        public string ResolveType(Type type, object value, string name)
        {
            this.Asked.Add(name);

            return answer;
        }
    }
}
