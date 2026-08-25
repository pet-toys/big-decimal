using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Checks that a value survives every way out of the type and back in — text, UTF-8 and JSON —
/// carrying both its number and its scale.
/// </summary>
public sealed class RoundTripFuzzTests
{
    private static readonly BigDecimalJsonConverter Converter = new();

    [Theory]
    [FuzzData]
    public void ToStringAndParse_RoundTrip(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var context = FuzzContext.Of(seed, index, drawn);
            var text = drawn.Value.ToString(CultureInfo.InvariantCulture);

            OracleValue.Observe(BigDecimal.Parse(text, CultureInfo.InvariantCulture))
                .Should().Be(OracleValue.From(drawn), "{0} round trips through \"{1}\"", context, text);
        }
    }

    [Theory]
    [FuzzData]
    public void TryFormat_AgreesWithToString(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));
        Span<char> destination = stackalloc char[512];

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var context = FuzzContext.Of(seed, index, drawn);
            var text = drawn.Value.ToString(CultureInfo.InvariantCulture);

            drawn.Value.TryFormat(destination, out var written, default, CultureInfo.InvariantCulture)
                .Should().BeTrue("{0} formats into 512 chars", context);
            destination[..written].ToString().Should().Be(text, "{0} TryFormat agrees with ToString", context);
        }
    }

    [Theory]
    [FuzzData]
    public void Utf8TryFormat_AgreesWithToString(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));
        Span<byte> destination = stackalloc byte[512];

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var context = FuzzContext.Of(seed, index, drawn);
            var text = drawn.Value.ToString(CultureInfo.InvariantCulture);

            drawn.Value.TryFormat(destination, out var written, default, CultureInfo.InvariantCulture)
                .Should().BeTrue("{0} formats into 512 bytes", context);
            Encoding.UTF8.GetString(destination[..written])
                .Should().Be(text, "{0} the UTF-8 overload agrees with ToString", context);
        }
    }

    [Theory]
    [FuzzData]
    public void Utf8Parse_RoundTrips(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var context = FuzzContext.Of(seed, index, drawn);
            var utf8 = Encoding.UTF8.GetBytes(drawn.Value.ToString(CultureInfo.InvariantCulture));

            OracleValue.Observe(BigDecimal.Parse(utf8, CultureInfo.InvariantCulture))
                .Should().Be(OracleValue.From(drawn), "{0} round trips through UTF-8", context);
        }
    }

    [Theory]
    [FuzzData]
    public void Json_RoundTrips(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var context = FuzzContext.Of(seed, index, drawn);
            var json = JsonSerializer.Serialize(drawn.Value);

            OracleValue.Observe(JsonSerializer.Deserialize<BigDecimal>(json))
                .Should().Be(OracleValue.From(drawn), "{0} round trips through JSON \"{1}\"", context, json);
        }
    }

    [Theory]
    [FuzzData]
    public void JsonConverter_ReadsWhatItWrote(int seed, int cases)
    {
        var generator = new ValueGenerator(new Random(seed));

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var context = FuzzContext.Of(seed, index, drawn);

            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                Converter.Write(writer, drawn.Value, JsonSerializerOptions.Default);
            }

            var reader = new Utf8JsonReader(buffer.WrittenSpan);
            reader.Read();

            OracleValue.Observe(Converter.Read(ref reader, typeof(BigDecimal), JsonSerializerOptions.Default))
                .Should().Be(OracleValue.From(drawn), "{0} round trips through the converter", context);
        }
    }
}
