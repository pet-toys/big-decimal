using System;
using System.Buffers;
using System.Globalization;
using Npgsql.Internal;

namespace PetToys.BigDecimal.Numerics;

#pragma warning disable NPG9001 // The driver's only extension point for a new type.

/// <summary>
/// Maps a PostgreSQL <c>numeric</c> to <see cref="BigDecimal"/> and back, in the binary format and
/// through no other type.
/// </summary>
/// <remarks>
/// Buffered rather than streaming: a value spans at most 50 bytes, and the payload crosses to
/// <see cref="PostgresNumeric"/> without a copy. The base classes are <c>Npgsql.Internal</c>'s,
/// published as <c>[Experimental("NPG9001")]</c>; nothing public replaced the handler family
/// Npgsql 8 removed, which is why the driver's version range is closed at the major.
/// </remarks>
internal sealed class BigDecimalConverter : PgBufferedConverter<BigDecimal>
{
    /// <summary>Answers the binary format and refuses the text one.</summary>
    /// <remarks>
    /// A text encoder would be a second encoder with a second set of edges, and the driver
    /// negotiates binary by default; a caller who forces text gets a resolution failure.
    /// </remarks>
    public override bool CanConvert(DataFormat format, out BufferRequirements bufferRequirements)
    {
        bufferRequirements = BufferRequirements.Value;

        return format is DataFormat.Binary;
    }

    /// <summary>Answers the exact size of the payload before it is written.</summary>
    public override Size GetSize(SizeContext context, BigDecimal value, ref object? writeState) =>
        PostgresNumeric.GetByteCount(value);

    /// <summary>Decodes a <c>numeric</c> payload the server sent.</summary>
    /// <remarks>
    /// The payload is not bounded by this type's range - PostgreSQL allows 131072 integer digits -
    /// so a split field is rented rather than stack-allocated. Usually there is no buffer at all.
    /// </remarks>
    protected override BigDecimal ReadCore(PgReader reader)
    {
        var length = reader.CurrentRemaining;
        var payload = reader.ReadBytes(length);

        if (payload.IsSingleSegment)
        {
            return PostgresNumeric.Read(payload.FirstSpan);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            payload.CopyTo(buffer);

            return PostgresNumeric.Read(buffer.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Encodes a value into a <c>numeric</c> payload.</summary>
    /// <remarks>
    /// Every value of this type fits, so neither refusal below is reachable by a value; each is a
    /// statement about this file rather than about the caller.
    /// </remarks>
    protected override void WriteCore(PgWriter writer, BigDecimal value)
    {
        Span<byte> payload = stackalloc byte[PostgresNumeric.MaxByteCount];

        if (!PostgresNumeric.TryWrite(value, payload, out var written))
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"A value of this type needs more than the {PostgresNumeric.MaxByteCount} bytes the format's own bound allows."));
        }

        // The field length GetSize answered is already on the wire; a disagreement desynchronises
        // the protocol and surfaces several messages later.
        var declared = writer.Current.Size;
        if (declared.Kind is SizeKind.Exact && declared.Value != written)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The field was framed as {declared.Value} bytes and the value encodes to {written}."));
        }

        writer.WriteBytes(payload[..written]);
    }
}
