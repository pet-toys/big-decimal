using System;
using System.Globalization;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Builds the culture matrix by setting <see cref="NumberFormatInfo"/> explicitly rather than by
/// naming a locale.
/// </summary>
/// <remarks>
/// A test that hinges on what `de-DE` happens to mean this year is a test that fails on one leg of
/// the operating-system matrix for a reason that has nothing to do with this package: ICU data
/// differs between platforms and between ICU versions. These cultures are built from the invariant
/// one and carry exactly the separators and group sizes they claim, on every machine.
/// </remarks>
public static class CultureMatrix
{
    /// <summary>Every case in the matrix.</summary>
    public static CultureCase[] All { get; } = Enum.GetValues<CultureCase>();

    // Built once. The randomised suites ask for a culture per case, and a fresh CultureInfo clone
    // with its NumberFormatInfo rewritten is not something to allocate three hundred thousand
    // times for four values that never change.
    private static readonly CultureInfo[] Cultures =
    [
        Build(".", ",", [3]),
        Build(",", ".", [3]),
        Build(".", ",", [3, 2]),
        Build(",", "\u00A0", [3]),
    ];

    /// <summary>Returns the culture for a case.</summary>
    /// <param name="culture">The case to build.</param>
    /// <returns>A culture carrying exactly the separators and group sizes the case names.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="culture"/> is not a defined case.</exception>
    public static CultureInfo Get(CultureCase culture) => culture switch
    {
        CultureCase.Invariant or CultureCase.CommaDecimal or CultureCase.NonUniformGroups or CultureCase.SpaceGroups =>
            Cultures[(int)culture],
        _ => throw new ArgumentOutOfRangeException(nameof(culture)),
    };

    private static CultureInfo Build(string decimalSeparator, string groupSeparator, int[] groupSizes)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        var numbers = culture.NumberFormat;

        numbers.NumberDecimalSeparator = decimalSeparator;
        numbers.NumberGroupSeparator = groupSeparator;
        numbers.NumberGroupSizes = groupSizes;
        numbers.CurrencyDecimalSeparator = decimalSeparator;
        numbers.CurrencyGroupSeparator = groupSeparator;
        numbers.CurrencyGroupSizes = groupSizes;
        numbers.PercentDecimalSeparator = decimalSeparator;
        numbers.PercentGroupSeparator = groupSeparator;
        numbers.PercentGroupSizes = groupSizes;
        numbers.NegativeSign = "-";
        numbers.PositiveSign = "+";

        // Shared from here on, so it is made read-only rather than trusted not to be written to.
        return CultureInfo.ReadOnly(culture);
    }
}
