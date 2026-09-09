using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The envelope a binary <c>COPY</c> stream puts around a field, written and read here rather than
/// by the driver.
/// </summary>
/// <remarks>
/// <para>
/// None of this is part of the <c>numeric</c> format. A signature, a flags word, a header extension
/// length, a field count per row, a length per field and a trailer carry the payload and nothing
/// else, which is exactly why they are worth isolating: a mistake in the envelope would surface as
/// a codec failure and be read as one. It is established against a payload the oracle composes
/// before any byte of the codec is judged by it.
/// </para>
/// <para>
/// The layout is the one PostgreSQL documents for <c>COPY ... WITH (FORMAT BINARY)</c>.
/// </para>
/// </remarks>
public static class CopyBinaryFrame
{
    /// <summary>The eleven byte signature every binary <c>COPY</c> stream opens with.</summary>
    /// <remarks>
    /// <c>PGCOPY\n\377\r\n\0</c>. The high bit and the two line endings are there so that a stream
    /// mangled by a text-mode transfer fails to parse rather than being read as data.
    /// </remarks>
    public static ReadOnlySpan<byte> Signature =>
        [0x50, 0x47, 0x43, 0x4F, 0x50, 0x59, 0x0A, 0xFF, 0x0D, 0x0A, 0x00];

    /// <summary>The signature, the flags word and the header extension length.</summary>
    private const int HeaderSize = 11 + 4 + 4;

    /// <summary>
    /// Frames payloads as rows of an ordinal and the payload itself, so that what comes back out of
    /// the table can be put in the order it went in.
    /// </summary>
    /// <param name="payloads">One field's bytes per row, in order.</param>
    /// <returns>A complete stream, header and trailer included.</returns>
    public static byte[] IndexedRows(IReadOnlyList<byte[]> payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);

        var size = HeaderSize + 2;
        foreach (var payload in payloads)
        {
            size += 2 + 4 + sizeof(int) + 4 + payload.Length;
        }

        var stream = new byte[size];
        var cursor = stream.AsSpan();

        Signature.CopyTo(cursor);
        cursor = cursor[Signature.Length..];

        // Flags: no OIDs, and nothing else is defined. Then a header extension nobody writes.
        BinaryPrimitives.WriteInt32BigEndian(cursor, 0);
        BinaryPrimitives.WriteInt32BigEndian(cursor[4..], 0);
        cursor = cursor[8..];

        for (var index = 0; index < payloads.Count; index++)
        {
            var payload = payloads[index];

            BinaryPrimitives.WriteInt16BigEndian(cursor, 2);
            BinaryPrimitives.WriteInt32BigEndian(cursor[2..], sizeof(int));
            BinaryPrimitives.WriteInt32BigEndian(cursor[6..], index);
            BinaryPrimitives.WriteInt32BigEndian(cursor[10..], payload.Length);
            payload.CopyTo(cursor[14..]);

            cursor = cursor[(14 + payload.Length)..];
        }

        // The trailer is a field count of -1, which no row can have.
        BinaryPrimitives.WriteInt16BigEndian(cursor, -1);

        return stream;
    }

    /// <summary>Reads back the single field of every row of a stream, checking the envelope as it
    /// goes.</summary>
    /// <param name="stream">The bytes the server produced.</param>
    /// <returns>One field's bytes per row, in the order the server sent them.</returns>
    /// <exception cref="FormatException">The envelope is not the documented one, which is a
    /// statement about this file rather than about the codec.</exception>
    public static IReadOnlyList<byte[]> ReadRows(ReadOnlySpan<byte> stream)
    {
        if (stream.Length < Signature.Length || !stream[..Signature.Length].SequenceEqual(Signature))
        {
            throw new FormatException("The stream does not open with the binary COPY signature.");
        }

        Needs(stream, HeaderSize, "a header");

        var extension = BinaryPrimitives.ReadInt32BigEndian(stream[15..]);
        Needs(stream, HeaderSize + extension, "the header extension it declares");

        var cursor = stream[(HeaderSize + extension)..];

        var payloads = new List<byte[]>();
        while (true)
        {
            // Every read is bounded first. A stream cut short is a statement about the envelope, and
            // it says so as a FormatException rather than as whatever BinaryPrimitives throws when
            // it runs off the end of a span.
            Needs(cursor, 2, "a field count");

            var fields = BinaryPrimitives.ReadInt16BigEndian(cursor);
            if (fields == -1)
            {
                return payloads;
            }

            if (fields != 1)
            {
                throw new FormatException($"Expected rows of one field, and a row declares {fields}.");
            }

            Needs(cursor, 6, "a field length");

            var length = BinaryPrimitives.ReadInt32BigEndian(cursor[2..]);
            if (length < 0)
            {
                throw new FormatException("The field is null, which no case here writes.");
            }

            Needs(cursor, 6 + length, "a field");

            payloads.Add(cursor.Slice(6, length).ToArray());
            cursor = cursor[(6 + length)..];
        }
    }

    private static void Needs(ReadOnlySpan<byte> span, int length, string what)
    {
        if (span.Length < length)
        {
            throw new FormatException($"The stream ends before {what}: {span.Length} bytes are left and {length} are needed.");
        }
    }
}
