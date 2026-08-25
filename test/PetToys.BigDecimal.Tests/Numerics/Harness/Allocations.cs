using System;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Measures what an operation allocates on the managed heap, under the rules a naive measurement
/// gets wrong.
/// </summary>
/// <remarks>
/// <para>
/// Never collect inside or before the window. <see cref="GC.GetAllocatedBytesForCurrentThread"/>
/// is a monotonic per-thread total that a collection does not reset, and a gen2 collection
/// <em>trims</em> <see cref="System.Buffers.ArrayPool{T}.Shared"/> — so the next rent inside the
/// window allocates a fresh buffer and the measurement reports bytes the operation did not cause.
/// That is what made a UTF-8 parse measurement report exactly 1 048 bytes, the size of a
/// <c>char[512]</c>, on macOS arm64 and nowhere else.
/// </para>
/// <para>
/// The operation runs once before the window so that just-in-time compilation and static
/// initialisation are not counted, results go through <see cref="Sink"/> rather than an
/// <see cref="object"/> so that nothing boxes, and the assertion is left to the caller — an
/// assertion-library call inside the window measures the assertion library.
/// </para>
/// </remarks>
public static class Allocations
{
    /// <summary>
    /// Where a measured operation leaves its result. Strongly typed on purpose: returning through
    /// <see cref="object"/> would box the value and measure the box.
    /// </summary>
    public static BigDecimal Sink { get; set; }

    /// <summary>Where a measured operation leaves a result that is not a <see cref="BigDecimal"/>.</summary>
    public static long OtherSink { get; set; }

    /// <summary>Runs an operation twice and reports what the second run allocated.</summary>
    /// <param name="operation">The operation to measure. It must not allocate the delegate itself.</param>
    /// <returns>The managed bytes allocated by one run.</returns>
    public static long Measure(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        // Outside the window: the first call pays for tiered compilation and for whatever static
        // state the path touches for the first time.
        operation();

        var before = GC.GetAllocatedBytesForCurrentThread();
        operation();
        var after = GC.GetAllocatedBytesForCurrentThread();

        return after - before;
    }

    /// <summary>Runs an operation repeatedly and reports what the whole run allocated.</summary>
    /// <param name="operation">The operation to measure.</param>
    /// <param name="iterations">How many times to run it inside the window.</param>
    /// <returns>The managed bytes allocated by all the iterations together.</returns>
    public static long Measure(Action operation, int iterations)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);

        operation();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < iterations; index++)
        {
            operation();
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        return after - before;
    }
}
