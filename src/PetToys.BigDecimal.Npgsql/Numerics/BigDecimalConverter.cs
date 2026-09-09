using System;
using System.Buffers;
using System.Globalization;
using Npgsql.Internal;

namespace PetToys.BigDecimal.Numerics;

#pragma warning disable NPG9001 // See the remarks: this is the driver's only extension point.

/// <summary>
/// Maps a PostgreSQL <c>numeric</c> to <see cref="BigDecimal"/> and back, in the binary format and
/// through no other type.
/// </summary>
/// <remarks>
/// <para>
/// Buffered rather than streaming. A value of this type spans at most 21 base-10000 groups, which
/// is 50 bytes with the header, so there is nothing to stream; and <c>PgReader.ReadBytes</c>
/// and <c>PgWriter.WriteBytes</c> take the spans <see cref="PostgresNumeric"/> already
/// works in, so the payload crosses without a copy in the ordinary case.
/// </para>
/// <para>
/// The base classes live in <c>Npgsql.Internal</c> and are published as
/// <c>[Experimental("NPG9001")]</c>. That is suppressed here, in the one file that reaches into the
/// driver's internals, rather than for the project: a later file that wants the same access should
/// have to say so. There is no alternative extension point. The <c>INpgsqlTypeHandler</c> family
/// this package was originally scoped against was removed in Npgsql 8 and nothing outside that
/// namespace replaced it, so the package's floor on the driver is a floor on a shape that may move.
/// </para>
/// </remarks>
internal sealed class BigDecimalConverter : PgBufferedConverter<BigDecimal>
{
    /// <summary>Answers the binary format and refuses the text one.</summary>
    /// <remarks>
    /// There is no text encoder here on purpose. PostgreSQL's text rendering of a
    /// <c>numeric</c> is a decimal string that the type's own formatter could produce, but writing
    /// that path would be a second encoder with a second set of edges to get wrong, and the driver
    /// negotiates binary by default. A caller who forces text gets a resolution failure, which
    /// names the problem, rather than a value that took a different route to get here.
    /// </remarks>
    /// <param name="format">The format being negotiated.</param>
    /// <param name="bufferRequirements">Receives what the converter needs buffered.</param>
    /// <returns><see langword="true"/> for <see cref="DataFormat.Binary"/>.</returns>
    public override bool CanConvert(DataFormat format, out BufferRequirements bufferRequirements)
    {
        bufferRequirements = BufferRequirements.Value;

        return format is DataFormat.Binary;
    }

    /// <summary>Answers the exact size of the payload before it is written.</summary>
    /// <remarks>
    /// This costs the same decomposition the write costs, so the value is decomposed twice on the
    /// way out. The codec says that is the driver's shape rather than a choice, and this is where
    /// that turns out to be literally true: Npgsql asks a length, writes it as the field header,
    /// and only then hands over a buffer.
    /// </remarks>
    /// <param name="context">The size context, unused.</param>
    /// <param name="value">The value about to be written.</param>
    /// <param name="writeState">The write state, unused.</param>
    /// <returns>The number of bytes the payload occupies.</returns>
    public override Size GetSize(SizeContext context, BigDecimal value, ref object? writeState) =>
        PostgresNumeric.GetByteCount(value);

    /// <summary>Decodes a <c>numeric</c> payload the server sent.</summary>
    /// <remarks>
    /// The payload is not bounded by this type's range. PostgreSQL allows 131072 integer digits and
    /// 16383 fractional ones, so a field can be tens of kilobytes and the buffer cannot be a stack
    /// one sized from <see cref="PostgresNumeric.MaxByteCount"/>. In the ordinary case there is no
    /// buffer at all: a fully buffered field usually arrives as a single segment and the codec
    /// reads the driver's own memory.
    /// </remarks>
    /// <param name="reader">The reader, positioned at the field.</param>
    /// <returns>The value, rounded half to even where the fraction is longer than this type holds.</returns>
    protected override BigDecimal ReadCore(PgReader reader)
    {
        var length = reader.CurrentRemaining;
        var payload = reader.ReadBytes(length);

        if (payload.IsSingleSegment)
        {
            return PostgresNumeric.Read(payload.FirstSpan);
        }

        // The read buffer wrapped, so the field is split across segments and has to be made
        // contiguous before the codec sees it. Rented rather than stack-allocated because the
        // length is the server's to choose.
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
    /// Every value of this type fits: PostgreSQL <c>numeric</c> holds far more than the type does
    /// in both directions, and <c>weight</c> and <c>dscale</c> are 16-bit fields a scale of at most
    /// 255 cannot reach. So neither branch below is reachable by a value, and each is a statement
    /// about this file rather than about the caller.
    /// </remarks>
    /// <param name="writer">The writer, positioned at the field.</param>
    /// <param name="value">The value to write.</param>
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

        // The driver has already written the field length from what GetSize answered, so a
        // disagreement between the two would desynchronise the protocol and be reported by the
        // server as something else entirely, several messages later. The size it is about to use is
        // on the writer, so checking costs nothing.
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
