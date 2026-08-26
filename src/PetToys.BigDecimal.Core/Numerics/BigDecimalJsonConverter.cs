using System;
using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Reads and writes <see cref="BigDecimal"/> values as JSON.
/// </summary>
/// <remarks>
/// Values are written as JSON strings, which keeps every digit intact: a JSON number would be
/// re-read by many parsers as an IEEE double and lose precision long before the type's own
/// limits. Reading accepts both a string and a JSON number, and preserves the scale the text
/// carries. The converter is applied automatically through
/// <see cref="System.Text.Json.Serialization.JsonConverterAttribute"/> on the type.
/// </remarks>
public sealed class BigDecimalJsonConverter : JsonConverter<BigDecimal>
{
    private const string InvalidValue = "The JSON value is not a valid BigDecimal.";

    private const int StackCopyBytes = BigDecimal.MaxCharsPlain + 32;

    /// <summary>Reads a value from JSON.</summary>
    /// <returns>The value the token carries.</returns>
    /// <exception cref="System.Text.Json.JsonException">The token is not a string or number holding a value this type accepts.</exception>
    public override BigDecimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
            case JsonTokenType.Number:
                return ReadValue(ref reader);
            case JsonTokenType.Null:
                throw new JsonException("Cannot convert null to BigDecimal.");
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} when reading a BigDecimal.");
        }
    }

    /// <summary>Writes a value as a JSON string.</summary>
    public override void Write(Utf8JsonWriter writer, BigDecimal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<char> buffer = stackalloc char[BigDecimal.MaxCharsPlain];
        if (value.TryFormatInvariant(buffer, out var written))
        {
            writer.WriteStringValue(buffer[..written]);
            return;
        }

        writer.WriteStringValue(value.ToString(null, CultureInfo.InvariantCulture));
    }

    /// <summary>Reads a value used as a JSON property name.</summary>
    /// <returns>The value the property name carries.</returns>
    /// <exception cref="System.Text.Json.JsonException">The property name does not hold a value this type accepts.</exception>
    public override BigDecimal ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadValue(ref reader);

    /// <summary>Writes a value as a JSON property name.</summary>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, BigDecimal value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<char> buffer = stackalloc char[BigDecimal.MaxCharsPlain];
        if (value.TryFormatInvariant(buffer, out var written))
        {
            writer.WritePropertyName(buffer[..written]);
            return;
        }

        writer.WritePropertyName(value.ToString(null, CultureInfo.InvariantCulture));
    }

    private static BigDecimal ReadValue(ref Utf8JsonReader reader)
    {
        if (!reader.HasValueSequence && !reader.ValueIsEscaped)
        {
            return FromUtf8(reader.ValueSpan);
        }

        var length = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
        if (length > int.MaxValue)
        {
            throw new JsonException(InvalidValue);
        }

        byte[]? rented = null;
        try
        {
            var copy = length <= StackCopyBytes
                ? stackalloc byte[StackCopyBytes]
                : (rented = ArrayPool<byte>.Shared.Rent((int)length));

            var written = reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName
                ? reader.CopyString(copy)
                : CopySequence(reader.ValueSequence, copy);

            if (written < 0)
            {
                throw new JsonException(InvalidValue);
            }

            return FromUtf8(copy[..written]);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static BigDecimal FromUtf8(ReadOnlySpan<byte> utf8) =>
        BigDecimal.TryParse(utf8, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new JsonException(InvalidValue);

    private static int CopySequence(ReadOnlySequence<byte> sequence, Span<byte> destination)
    {
        if (sequence.Length > destination.Length)
        {
            return -1;
        }

        sequence.CopyTo(destination);
        return (int)sequence.Length;
    }
}
