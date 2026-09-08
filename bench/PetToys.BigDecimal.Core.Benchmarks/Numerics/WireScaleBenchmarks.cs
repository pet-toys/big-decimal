using System.Numerics;
using BenchmarkDotNet.Attributes;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// One magnitude encoded to PostgreSQL at both ends of the scale, which is what makes the
/// zero-group stripping measurable.
/// </summary>
/// <remarks>
/// <para>
/// The guard is a budget rather than a test because the failure it catches passes every
/// correctness test there is. A writer that emitted one base-10000 group per scale position
/// instead of stripping the zero groups and carrying the display scale in <c>dscale</c> would
/// produce bytes a server accepts and a reader returns the right value from; it would simply cost
/// some sixty times more on a value of high scale. Nothing but a ratio sees that.
/// </para>
/// <para>
/// The pair is PostgreSQL's alone. A ClickHouse payload is a fixed width whatever the scale, so
/// there is no stripping for a row pair to be about.
/// </para>
/// <para>
/// The two rows carry the same mantissa and write into the same buffer, so the ratio measures the
/// scale and nothing else. This class declares its own baseline rather than borrowing one, since
/// the quantity wanted is the ratio between its two rows.
/// </para>
/// </remarks>
[BenchmarkCategory(BenchmarkCategories.Budget)]
public class WireScaleBenchmarks
{
    /// <summary>
    /// The mantissa both rows carry: nineteen digits, a single 64-bit word, five base-10000 groups.
    /// </summary>
    private static readonly BigInteger Mantissa = new(1234567890123456789L);

    private BigDecimal _atZero;
    private BigDecimal _atMaximum;
    private byte[] _destination = [];

    /// <summary>Builds the two values and the destination they share.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _atZero = BigDecimal.FromScaled(Mantissa, 0);
        _atMaximum = BigDecimal.FromScaled(Mantissa, BigDecimal.MaxScale);
        _destination = new byte[PostgresNumeric.MaxByteCount];
    }

    /// <summary>Encoding the magnitude as an integer, which the 1.5x budget is stated against.</summary>
    /// <returns>The number of bytes written, returned so that the call is not elided.</returns>
    [Benchmark(Baseline = true)]
    public int AtScaleZero()
    {
        _ = PostgresNumeric.TryWrite(_atZero, _destination, out var written);

        return written;
    }

    /// <summary>Encoding the same magnitude at the widest scale the type carries.</summary>
    /// <returns>The number of bytes written, returned so that the call is not elided.</returns>
    [Benchmark]
    public int AtMaximumScale()
    {
        _ = PostgresNumeric.TryWrite(_atMaximum, _destination, out var written);

        return written;
    }
}
