using System;
using System.ComponentModel;
using System.Globalization;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Converts a <see cref="BigDecimal"/> to and from text for the reflection-driven half of the
/// platform.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TypeDescriptor"/> is how configuration binding, model binding and designers reach a
/// type they know nothing about, and without a converter they reach nothing: the base
/// <see cref="TypeConverter"/> converts neither direction. The attribute on
/// <see cref="BigDecimal"/> is what makes this one found, so a consumer writes no registration.
/// </para>
/// <para>
/// Text in both directions and nothing else. Numbers convert to numbers through the cast operators
/// and generic math, which the compiler checks; a second route through here would have its own
/// rules and no such check, and the two would eventually disagree. Nor does this carry parsing or
/// formatting rules of its own - it calls the type's, so it cannot drift from them.
/// </para>
/// </remarks>
public sealed class BigDecimalTypeConverter : TypeConverter
{
    /// <summary>Whether a value of some type can be converted to <see cref="BigDecimal"/>.</summary>
    /// <param name="context">The format context, unused.</param>
    /// <param name="sourceType">The type converted from.</param>
    /// <returns><see langword="true"/> for <see cref="string"/> and nothing else.</returns>
    /// <remarks>
    /// This narrows the base class rather than widening it: measured, the base answers
    /// <see langword="false"/> for <see cref="string"/> and <see langword="true"/> for
    /// <c>System.ComponentModel.Design.Serialization.InstanceDescriptor</c>, which lets a designer
    /// rebuild a value from a constructor call. That is declined - it needs reflection over a
    /// constructor, and text already carries every value of this type exactly.
    /// </remarks>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string);

    /// <summary>Whether a <see cref="BigDecimal"/> can be converted to some type.</summary>
    /// <param name="context">The format context, unused.</param>
    /// <param name="destinationType">The type converted to.</param>
    /// <returns><see langword="true"/> for <see cref="string"/> and nothing else.</returns>
    /// <remarks>
    /// The parameter is nullable here and not on <see cref="CanConvertFrom"/> because that is how
    /// the base declares each of them: measured, <c>destinationType</c> carries
    /// <c>NotNullWhen(true)</c> and <c>sourceType</c> carries no nullable annotation at all.
    /// Otherwise this restates what the base already answers, and is here so that the pair
    /// reads as one statement about what the converter carries. What it does not do is what
    /// <c>DecimalConverter</c> does, which is to add the designer's <c>InstanceDescriptor</c> on
    /// this side; that is declined for the reason on <see cref="CanConvertFrom"/>.
    /// </remarks>
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

    /// <summary>Reads the culture a caller passed, or the one a missing culture means here.</summary>
    /// <param name="culture">The culture the caller passed.</param>
    /// <returns>The culture to parse or format with.</returns>
    /// <remarks>
    /// A missing culture is the invariant one rather than the current one. That is the base class's
    /// own convention, and it is the safer half of the choice: a value that reaches a converter
    /// usually came out of a configuration file or a request, where the machine's culture has no
    /// business deciding what the separator was.
    /// </remarks>
    private static CultureInfo Provider(CultureInfo? culture) => culture ?? CultureInfo.InvariantCulture;
}
