using System;
using System.ComponentModel;
using System.Globalization;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Converts a <see cref="BigDecimal"/> to and from text for the reflection-driven half of the
/// platform.
/// </summary>
/// <remarks>
/// <see cref="TypeDescriptor"/> is how configuration binding, model binding and designers reach a
/// type they know nothing about, and the attribute on <see cref="BigDecimal"/> is what makes this
/// converter found. Text in both directions and nothing else: numbers convert to numbers through
/// the cast operators and generic math, which the compiler checks, and the parsing and formatting
/// rules are the type's own.
/// </remarks>
public sealed class BigDecimalTypeConverter : TypeConverter
{
    /// <summary>Whether a value of some type can be converted to <see cref="BigDecimal"/>.</summary>
    /// <param name="context">The format context, unused.</param>
    /// <param name="sourceType">The type converted from.</param>
    /// <returns><see langword="true"/> for <see cref="string"/> and nothing else.</returns>
    /// <remarks>
    /// The base answers <see langword="true"/> for the designer's <c>InstanceDescriptor</c>, which
    /// rebuilds a value from a constructor by reflection; that is declined, since text already
    /// carries every value of this type exactly.
    /// </remarks>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string);

    /// <summary>Whether a <see cref="BigDecimal"/> can be converted to some type.</summary>
    /// <param name="context">The format context, unused.</param>
    /// <param name="destinationType">The type converted to.</param>
    /// <returns><see langword="true"/> for <see cref="string"/> and nothing else.</returns>
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string);

    /// <summary>Converts text to a <see cref="BigDecimal"/>.</summary>
    /// <param name="context">The format context, unused.</param>
    /// <param name="culture">The culture to read the text in, or <see langword="null"/>.</param>
    /// <param name="value">The value to convert.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="NotSupportedException"><paramref name="value"/> is not a string.</exception>
    /// <exception cref="FormatException">The text is not a number this type reads.</exception>
    /// <exception cref="OverflowException">The value does not fit.</exception>
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
        value is string text
            ? BigDecimal.Parse(text, Provider(culture))
            : base.ConvertFrom(context, culture, value);

    /// <summary>Converts a <see cref="BigDecimal"/> to text.</summary>
    /// <param name="context">The format context, unused.</param>
    /// <param name="culture">The culture to render in, or <see langword="null"/>.</param>
    /// <param name="value">The value to convert.</param>
    /// <param name="destinationType">The type converted to.</param>
    /// <returns>The rendered value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="destinationType"/> is null.</exception>
    /// <exception cref="NotSupportedException">The conversion is not one this offers.</exception>
    public override object? ConvertTo(
        ITypeDescriptorContext? context,
        CultureInfo? culture,
        object? value,
        Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        return destinationType == typeof(string) && value is BigDecimal number
            ? number.ToString(null, Provider(culture))
            : base.ConvertTo(context, culture, value, destinationType);
    }

    // A missing culture is the invariant one, as the base class has it: a value reaching a
    // converter usually came out of a configuration file or a request.
    private static CultureInfo Provider(CultureInfo? culture) => culture ?? CultureInfo.InvariantCulture;
}
