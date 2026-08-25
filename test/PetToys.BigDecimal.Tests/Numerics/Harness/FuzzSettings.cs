using System;
using System.Globalization;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The two knobs the randomised tests answer to, read once from the environment.
/// </summary>
/// <remarks>
/// Both have fixed defaults, so an unconfigured run — a developer's, or a continuous integration
/// leg's — executes exactly the same cases everywhere. Raising <see cref="CasesVariable"/> turns
/// the same tests into a soak run; changing <see cref="SeedVariable"/> sends that soak over ground
/// the default run never visits. A value that cannot be parsed is an error rather than a silent
/// fallback: a misconfigured soak that quietly runs the default explores nothing and says nothing.
/// </remarks>
public static class FuzzSettings
{
    /// <summary>The environment variable that overrides <see cref="Cases"/>.</summary>
    public const string CasesVariable = "BIGDECIMAL_FUZZ_CASES";

    /// <summary>The environment variable that overrides <see cref="BaseSeed"/>.</summary>
    public const string SeedVariable = "BIGDECIMAL_FUZZ_SEED";

    /// <summary>The number of cases each randomised test runs when nothing overrides it.</summary>
    public const int DefaultCases = 2_000;

    /// <summary>The constant every derived seed is mixed with when nothing overrides it.</summary>
    public const int DefaultBaseSeed = 0x5F3D_1A27;

    /// <summary>
    /// How many rows a randomised test's cases are split across. Each row is one reported test
    /// with a seed of its own, so this trades the runner's per-result overhead against how
    /// precisely a failure is located.
    /// </summary>
    public const int BatchCount = 20;

    /// <summary>The number of cases each randomised test runs.</summary>
    public static int Cases { get; } = ReadInt32(CasesVariable, DefaultCases, minimum: 1);

    /// <summary>The constant every derived seed is mixed with.</summary>
    public static int BaseSeed { get; } = ReadInt32(SeedVariable, DefaultBaseSeed, minimum: int.MinValue);

    private static int ReadInt32(string variable, int fallback, int minimum)
    {
        var text = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < minimum)
        {
            throw new InvalidOperationException(
                $"{variable} is set to '{text}', which is not an integer of at least {minimum}.");
        }

        return value;
    }
}
