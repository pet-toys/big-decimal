using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The one configuration every benchmark in this assembly runs under.
/// </summary>
/// <remarks>
/// <para>
/// It is built on top of <see cref="DefaultConfig"/> rather than from nothing, so the default
/// loggers, columns, analysers and validators stay in place, and it is applied through
/// <c>BenchmarkSwitcher</c> rather than through an attribute on every class, so the command line
/// keeps its say over the job and the runtimes.
/// </para>
/// <para>
/// The memory diagnoser is the only addition. It is what puts the allocated-bytes column in the
/// report, which makes a regression in the zero-allocation guarantee visible in the table a
/// performance change is already reading. Enforcement of that guarantee stays in the test suite;
/// nothing here asserts on it. The GitHub-flavoured markdown export that the recorded baseline is
/// a copy of needs no addition — the default configuration already emits it.
/// </para>
/// <para>
/// The job is left at its default. A shorter one is available from the command line for a quick
/// pass, but the baseline is taken with the default: a reference measured over fewer iterations
/// than the runs compared against it is not a reference.
/// </para>
/// </remarks>
public static class BenchmarkConfig
{
    /// <summary>Builds the configuration.</summary>
    /// <returns>The configuration to hand to the switcher.</returns>
    public static IConfig Create() =>
        ManualConfig.Create(DefaultConfig.Instance)
            .AddDiagnoser(MemoryDiagnoser.Default);
}
