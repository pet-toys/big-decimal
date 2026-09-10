using Xunit;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The collection every test that measures allocation belongs to.
/// </summary>
/// <remarks>
/// <see cref="Allocations"/> measures a per-thread counter, so another thread's allocations cannot
/// be counted into the window. What another thread can do is trigger a gen2 collection inside it,
/// which trims <see cref="System.Buffers.ArrayPool{T}.Shared"/> - and the next rent then allocates
/// a fresh buffer that the measurement reports as bytes the operation caused. That failure has no
/// stable case: it lands on whichever measurement was running, and it looks like a real regression
/// in a member that did not change.
/// <para>
/// Measured on 2026-09-10, on the change that added the type converter's cases to this assembly:
/// zero failures in eighteen Debug runs before those cases existed and two in ten after, on two
/// different measurements. Turning parallelisation off for this collection is what takes the other
/// threads out of the window; it costs the few seconds these tests take, and it is the difference
/// between a required check that reports the code and one that reports the scheduler.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialMeasurement
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Serial measurement";
}
