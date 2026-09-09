using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The copy envelope on its own, with no server involved.
/// </summary>
/// <remarks>
/// <para>
/// The envelope is not the <c>numeric</c> format, and a mistake in it would show up as a codec
/// failure in every test that crosses it. It is established here first, against payloads the oracle
/// composes rather than payloads the codec writes, and each direction is checked against the
/// documented layout rather than against the other: the two are different shapes, since a row going
/// in carries an ordinal beside the payload and a row coming out is the payload alone.
/// </para>
/// <para>
/// These cases are also what keeps this project out of the zero-tests report on the legs that
/// exclude the integration category.
/// </para>
/// </remarks>
public sealed class CopyBinaryFrameTests
{
    private static byte[] Payload(long unscaled, int scale) =>
        WireFormatOracle.Postgres(new OracleValue(new BigInteger(unscaled), scale));

    /// <summary>Builds what the server sends back: rows of one field, with no ordinal.</summary>
    /// <remarks>
    /// Written out here rather than taken from <see cref="CopyBinaryFrame.IndexedRows"/>, so that
    /// the reader is checked against the documented layout and not against our own writer.
    /// </remarks>
    private static byte[] Export(params byte[][] payloads)
    {
        var stream = new List<byte>(CopyBinaryFrame.Signature.ToArray());
        stream.AddRange([0, 0, 0, 0, 0, 0, 0, 0]);

        foreach (var payload in payloads)
        {
            stream.AddRange([0, 1]);
            stream.AddRange([(byte)(payload.Length >> 24), (byte)(payload.Length >> 16), (byte)(payload.Length >> 8), (byte)payload.Length]);
            stream.AddRange(payload);
        }

        stream.AddRange([0xFF, 0xFF]);

        return [.. stream];
    }

    [Fact]
    public void AnImportedRow_IsLaidOutAsTheFormatDocumentsIt()
    {
        var payload = Payload(12345678, 4);
        var stream = CopyBinaryFrame.IndexedRows([payload]);

        // Signature, flags, header extension length; then the row: two fields, an int of four bytes
        // carrying the ordinal, and the payload with its own length.
        stream.AsSpan(0, 11).ToArray().Should().Equal(CopyBinaryFrame.Signature.ToArray());
        BinaryPrimitives.ReadInt32BigEndian(stream.AsSpan(11)).Should().Be(0, "no flag is defined");
        BinaryPrimitives.ReadInt32BigEndian(stream.AsSpan(15)).Should().Be(0, "nobody writes a header extension");
        BinaryPrimitives.ReadInt16BigEndian(stream.AsSpan(19)).Should().Be(2, "the ordinal travels beside the value");
        BinaryPrimitives.ReadInt32BigEndian(stream.AsSpan(21)).Should().Be(sizeof(int));
        BinaryPrimitives.ReadInt32BigEndian(stream.AsSpan(25)).Should().Be(0, "the first row is ordinal zero");
        BinaryPrimitives.ReadInt32BigEndian(stream.AsSpan(29)).Should().Be(payload.Length);
        stream.AsSpan(33, payload.Length).ToArray().Should().Equal(payload);
        BinaryPrimitives.ReadInt16BigEndian(stream.AsSpan(33 + payload.Length)).Should().Be(-1, "the trailer closes the stream");
        stream.Length.Should().Be(35 + payload.Length);
    }

    [Fact]
    public void EachImportedRow_CarriesItsOwnOrdinal()
    {
        var payload = Payload(1, 0);
        var stream = CopyBinaryFrame.IndexedRows([payload, payload, payload]);

        var row = 33 + payload.Length;
        BinaryPrimitives.ReadInt32BigEndian(stream.AsSpan(row + 6)).Should().Be(1);
        BinaryPrimitives.ReadInt32BigEndian(stream.AsSpan((2 * row) - 19 + 6)).Should().Be(2);
    }

    [Fact]
    public void AnExportedRow_ReadsBackAsItsPayload()
    {
        var payload = Payload(12345678, 4);

        CopyBinaryFrame.ReadRows(Export(payload)).Should().ContainSingle().Which.Should().Equal(payload);
    }

    [Fact]
    public void ExportedRows_KeepTheirOrderAndTheirLengths()
    {
        byte[][] payloads = [Payload(0, 0), Payload(-1, 0), Payload(5, 1), Payload(1, 2)];

        var read = CopyBinaryFrame.ReadRows(Export(payloads));

        read.Should().HaveCount(payloads.Length);
        for (var index = 0; index < payloads.Length; index++)
        {
            read[index].Should().Equal(payloads[index], "row {0} carries its own payload", index);
        }
    }

    [Fact]
    public void AStreamWithoutTheSignature_IsRefusedRatherThanRead()
    {
        var mangled = Export(Payload(1, 0));
        mangled[7] = 0x0A;

        var read = () => CopyBinaryFrame.ReadRows(mangled);

        read.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void AHeaderExtensionTheStreamCannotHold_IsRefusedRatherThanRead(int extension)
    {
        // The length comes off the wire, so a reader that trusted it would slice from a negative
        // index or overflow the addition, and either way the span would throw where this file says
        // a malformed envelope is a FormatException.
        var mangled = Export(Payload(1, 0));
        BinaryPrimitives.WriteInt32BigEndian(mangled.AsSpan(15), extension);

        var read = () => CopyBinaryFrame.ReadRows(mangled);

        read.Should().Throw<FormatException>();
    }

    [Fact]
    public void AStreamCutShort_IsRefusedRatherThanRead()
    {
        var truncated = Export(Payload(12345678, 4))[..^3];

        var read = () => CopyBinaryFrame.ReadRows(truncated);

        read.Should().Throw<FormatException>("a stream that ends mid-row is an envelope fault");
    }

    [Fact]
    public void AStreamOfTheWrongShape_IsRefusedRatherThanRead()
    {
        var read = () => CopyBinaryFrame.ReadRows(CopyBinaryFrame.IndexedRows([Payload(1, 0)]));

        read.Should().Throw<FormatException>("a row going in is not shaped like a row coming out");
    }
}
