using System;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The <see cref="TypeDescriptor"/> route into and out of the type, and the interface that is
/// deliberately not on it.
/// </summary>
/// <remarks>
/// Configuration binding, model binding and designers reach a type they know nothing about through
/// <see cref="TypeDescriptor"/>. These cases are about that route being there, carrying the type's
/// own rules rather than a second set, and stopping where it should.
/// </remarks>
/// <remarks>
/// One case here changes the ambient culture, which is process state the runner's threads share, so
/// the class joins the collection that exists for exactly that and runs unparallelised.
/// </remarks>
[Collection(AmbientCulture.Name)]
public sealed class TypeConverterTests
{
    /// <summary>Values whose text has something to lose: scale, sign, size, and the three that are not numbers.</summary>
    public static TheoryData<string> Corpus =>
    [
        "0",
        "1",
        "-1",
        "1.50",
        "0.000",
        "-0.0001",
        "123456789012345678901234567890.123456789",
        "NaN",
        "Infinity",
        "-Infinity",
    ];

    /// <summary>Types the converter has no business converting, one per family.</summary>
    public static TheoryData<Type> NotOffered =>
    [
        typeof(int),
        typeof(long),
        typeof(decimal),
        typeof(double),
        typeof(object),
    ];

    [Fact]
    public void TheTypeDescriptor_AnswersThisConverter()
    {
        // What the attribute on the declaration buys, and the only assertion that proves a consumer
        // needs no registration: everything else here could pass with the converter unreachable.
        TypeDescriptor.GetConverter(typeof(BigDecimal)).Should().BeOfType<BigDecimalTypeConverter>();
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void TextSurvivesTheRoundTrip(string text)
    {
        var converter = new BigDecimalTypeConverter();

        var value = converter.ConvertFromInvariantString(text);
        var back = converter.ConvertToInvariantString(value);

        back.Should().Be(text, "the converter renders through the type's own formatting");
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void TheConverterAgreesWithTheTypesOwnParseAndFormat(string text)
    {
        var converter = new BigDecimalTypeConverter();
        var expected = BigDecimal.Parse(text, CultureInfo.InvariantCulture);

        // The routing rather than the results. If the converter ever grows rules of its own, this is
        // what says so, whatever those rules happen to answer.
        converter.ConvertFromInvariantString(text).Should().Be(expected);
        converter.ConvertToInvariantString(expected)
            .Should().Be(expected.ToString(null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ExponentNotation_IsAcceptedAndRendersExpanded()
    {
        var converter = new BigDecimalTypeConverter();

        var value = converter.ConvertFromInvariantString("1E+30");

        // Accepted on the way in, because the type's default parse styles allow an exponent, and
        // rendered expanded on the way out, because its formatting has no exponent form. Text does
        // not round-trip here and it is not supposed to: the value does.
        value.Should().Be(BigDecimal.Parse("1E+30", CultureInfo.InvariantCulture));
        converter.ConvertToInvariantString(value).Should().Be("1000000000000000000000000000000");
    }

    [Fact]
    public void TheCallersCulture_DecidesWhatTheTextMeans()
    {
        var converter = new BigDecimalTypeConverter();
        var german = CultureInfo.GetCultureInfo("de-DE");

        var value = converter.ConvertFrom(null, german, "1.234.567,89");

        value.Should().Be(BigDecimal.Parse("1234567.89", CultureInfo.InvariantCulture));

        // The same text under the invariant culture is not a smaller number, it is not a number:
        // two decimal points. Culture handling that quietly guessed would answer something here.
        var invariant = () => converter.ConvertFromInvariantString("1.234.567,89");

        invariant.Should().Throw<FormatException>();
    }

    [Fact]
    public void AMissingCulture_IsTheInvariantOneRatherThanTheCurrent()
    {
        var converter = new BigDecimalTypeConverter();

        // A culture whose decimal separator is a comma. If a missing culture meant the current one,
        // neither line below would answer what the invariant culture answers.
        using (CultureScope.For(CultureCase.CommaDecimal))
        {
            converter.ConvertFrom(null, null, "1.5")
                .Should().Be(BigDecimal.Parse("1.5", CultureInfo.InvariantCulture));

            converter.ConvertTo(null, null, BigDecimal.Parse("1.5", CultureInfo.InvariantCulture), typeof(string))
                .Should().Be("1.5");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("one")]
    [InlineData("1.2.3")]
    [InlineData("--1")]
    public void MalformedText_Raises(string text)
    {
        var converter = new BigDecimalTypeConverter();

        var converting = () => converter.ConvertFromInvariantString(text);

        // Not a default value. A converter that answered zero for rubbish would put a wrong number
        // into a configuration binding with nothing to report it.
        converting.Should().Throw<FormatException>();
    }

    [Theory]
    [MemberData(nameof(NotOffered))]
    public void ANumericConversion_IsNotOffered(Type type)
    {
        var converter = new BigDecimalTypeConverter();

        // Numbers convert to numbers through the cast operators and generic math, which the compiler
        // checks. A second route through here would have its own rules and no such check.
        converter.CanConvertFrom(type).Should().BeFalse();
        converter.CanConvertTo(type).Should().BeFalse();
    }

    [Fact]
    public void TextIsOffered_InBothDirections()
    {
        var converter = new BigDecimalTypeConverter();

        converter.CanConvertFrom(typeof(string)).Should().BeTrue();
        converter.CanConvertTo(typeof(string)).Should().BeTrue();
    }

    [Fact]
    public void AnInstanceDescriptor_IsNotOffered()
    {
        var converter = new BigDecimalTypeConverter();

        // The one conversion the base class offers that this does not, and it is on the reading
        // side: TypeConverter.CanConvertFrom answers true for InstanceDescriptor and false for
        // string, which is the opposite of what this converter is for. A designer would use it to
        // rebuild a value from a constructor call; text already carries every value exactly.
        converter.CanConvertFrom(typeof(InstanceDescriptor)).Should().BeFalse();

        // The writing side is where DecimalConverter adds it. This one does not.
        converter.CanConvertTo(typeof(InstanceDescriptor)).Should().BeFalse();
    }

    [Fact]
    public void TheType_ImplementsNoIConvertible()
    {
        // Deliberate, and two shipped packages rest on it. Without a universal conversion to fall
        // back to, a library that does not recognise this type refuses: PetToys.BigDecimal.ClickHouse
        // fails with InvalidCastException when no parameter formatter is installed, and
        // PetToys.BigDecimal.ClickHouse.Dapper is refused by Dapper when no handler is registered.
        // Implement IConvertible and both of those become a value narrowed through System.Decimal,
        // quietly, and nothing else in this repository would report the day it happened.
        typeof(IConvertible).IsAssignableFrom(typeof(BigDecimal))
            .Should().BeFalse("the ClickHouse packages' refusals are specified in terms of this absence");
    }
}
