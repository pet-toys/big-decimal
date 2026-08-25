using System;
using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

public sealed class JsonConverterTests
{
    private static readonly BigDecimalJsonConverter Converter = new();

    private static BigDecimal Read(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return Converter.Read(ref reader, typeof(BigDecimal), JsonSerializerOptions.Default);
    }

    private static BigDecimal ReadSegmented(string json, int chunkSize)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        Segment? first = null;
        Segment? last = null;
        for (var i = 0; i < bytes.Length; i += chunkSize)
        {
            var memory = new ReadOnlyMemory<byte>(bytes, i, Math.Min(chunkSize, bytes.Length - i));
            if (first is null)
            {
                first = new Segment(memory, 0);
                last = first;
            }
            else
            {
                last = last!.Append(memory);
            }
        }

        var sequence = new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
        var reader = new Utf8JsonReader(sequence, isFinalBlock: true, state: default);
        reader.Read();
        return Converter.Read(ref reader, typeof(BigDecimal), JsonSerializerOptions.Default);
    }

    private static string Write(BigDecimal value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        Converter.Write(writer, value, JsonSerializerOptions.Default);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    [Fact]
    public void Values_RoundTripAsStrings()
    {
        Write(BigDecimal.Parse("1.500", CultureInfo.InvariantCulture)).Should().Be("\"1.500\"");
        Read("\"1.500\"").Scale.Should().Be(3);
        Read("1.500").Scale.Should().Be(3);
        Read("\"" + BigDecimal.MaxValue.ToString(CultureInfo.InvariantCulture) + "\"")
            .Should().Be(BigDecimal.MaxValue);
    }

    [Fact]
    public void ALongValue_IsAcceptedWhateverTheTokenLooksLike()
    {
        var text = "0." + new string('1', 500);
        var expected = BigDecimal.Parse(text, CultureInfo.InvariantCulture);

        Read("\"" + text + "\"").Should().Be(expected);
        Read(text).Should().Be(expected);
        ReadSegmented("\"" + text + "\"", 64).Should().Be(expected);
        ReadSegmented(text, 64).Should().Be(expected);

        var escaped = "0." + new string('1', 400);
        Read("\"" + escaped + "\\u0030\"")
            .Should().Be(BigDecimal.Parse(escaped + "0", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Segmentation_DoesNotDecideWhetherAValueIsAccepted()
    {
        var text = "\"0." + new string('9', 300) + "\"";

        var whole = Read(text);

        foreach (var chunk in new[] { 1, 3, 17, 64, 256 })
        {
            ReadSegmented(text, chunk).Should().Be(whole);
        }
    }

    [Fact]
    public void AnEscapedValue_IsUnescapedBeforeParsing()
    {
        Read("\"1.5\\u0030\"").Should().Be(BigDecimal.Parse("1.50", CultureInfo.InvariantCulture));
        Read("\"1.5\\u0030\"").Scale.Should().Be(2);
    }

    [Theory]
    [InlineData("\"abc\"")]
    [InlineData("\"\"")]
    [InlineData("true")]
    [InlineData("null")]
    public void AnInvalidValue_ThrowsJsonException(string json)
    {
        var act = () => Read(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void AnInvalidLongValue_ThrowsJsonExceptionRatherThanArgumentException()
    {
        var act = () => Read("\"0." + new string('1', 400) + "\\u0030x\"");

        act.Should().Throw<JsonException>();
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }
}
