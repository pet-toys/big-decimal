using System;

namespace BigDecimalProbes;

/// <summary>
/// The shared shape of a probe, copied into each one rather than shared through a project.
/// A shared library would be a fourth assembly in the trimmed closure of all three probes, and
/// the closure is what is being measured; thirty lines are cheaper than that.
/// </summary>
internal static class Probe
{
    private static int _ran;

    /// <summary>
    /// Compares one result against a value captured from the jitted build. Exits the process on
    /// the first mismatch: a probe that carried on would report the last failure rather than the
    /// first, and the first is the one with a cause.
    /// </summary>
    internal static void Check(string name, string expected, string actual)
    {
        _ran++;

        if (string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Console.WriteLine("ok   " + name + ": " + actual);
            return;
        }

        Console.Error.WriteLine("FAIL " + name + ": expected <" + expected + ">, actual <" + actual + ">");
        Environment.Exit(1);
    }

    /// <summary>
    /// Reports what ran, and fails when that is not what the probe declared. A trimmed binary
    /// whose checks were themselves removed, or a probe that returned early, exits non-zero here
    /// rather than looking like a fast green run.
    /// </summary>
    internal static int Done(string probe, int declared)
    {
        if (_ran == declared)
        {
            Console.WriteLine(probe + ": " + _ran.ToString(System.Globalization.CultureInfo.InvariantCulture) + " checks passed");
            return 0;
        }

        Console.Error.WriteLine(
            "FAIL " + probe + ": declared " + declared.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            " checks, ran " + _ran.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return 1;
    }
}
