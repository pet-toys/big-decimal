using BenchmarkDotNet.Running;
using PetToys.BigDecimal.Numerics;

namespace PetToys.BigDecimal;

/// <summary>The entry point of the benchmark runner.</summary>
public static class Program
{
    /// <summary>Runs the benchmarks the command line selects.</summary>
    /// <param name="args">
    /// BenchmarkDotNet's own switches — <c>--filter</c>, <c>--runtimes</c>, <c>--job</c> and the
    /// rest. They are passed through untouched, which is why the configuration lives in one place
    /// instead of on every benchmark class.
    /// </param>
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, BenchmarkConfig.Create());
}
