using System;
using System.Globalization;
using AwesomeAssertions;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Compares what an operation produced against what the oracle requires, and reports a failure in
/// terms a reader can act on: the seed, the case within it, both operands and both results.
/// </summary>
/// <remarks>
/// An oracle that throws <see cref="OverflowException"/> is a prediction like any other — the
/// operation is required to throw as well, and quietly returning a value is a failure.
/// </remarks>
public static class OracleAssert
{
    /// <summary>Asserts that an operation agrees with the oracle, exceptions included.</summary>
    /// <param name="context">Where the case came from, for the failure message.</param>
    /// <param name="operation">How the operands were combined, for the failure message.</param>
    /// <param name="actual">The operation under test.</param>
    /// <param name="expected">The oracle's prediction.</param>
    public static void Matches(
        in FuzzContext context,
        string operation,
        Func<BigDecimal> actual,
        Func<OracleValue> expected)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);

        OracleValue required;
        try
        {
            required = expected();
        }
        catch (OverflowException)
        {
            actual.Should().Throw<OverflowException>("{0} {1} overflows", context, operation);

            return;
        }
        catch (DivideByZeroException)
        {
            actual.Should().Throw<DivideByZeroException>("{0} {1} divides by zero", context, operation);

            return;
        }

        BigDecimal produced;
        try
        {
            produced = actual();
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{context} {operation} threw {error.GetType().Name} where the oracle requires {required}."),
                error);
        }

        OracleValue.Observe(produced).Should().Be(required, "{0} {1}", context, operation);
    }
}
