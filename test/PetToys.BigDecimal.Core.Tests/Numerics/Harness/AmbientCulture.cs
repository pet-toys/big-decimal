using Xunit;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The collection every test that changes <see cref="System.Globalization.CultureInfo.CurrentCulture"/>
/// belongs to.
/// </summary>
/// <remarks>
/// The current culture is process state that the test runner's threads share, and
/// <see cref="CultureScope"/> restores it rather than isolating it. Turning parallelisation off for
/// this collection is what makes that safe unconditionally, instead of safe because the tests
/// happen to be synchronous and stay on one thread. Everything else keeps running in parallel:
/// only the handful of tests about the ambient culture live here, and every other test passes an
/// <see cref="System.IFormatProvider"/> explicitly.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AmbientCulture
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Ambient culture";
}
