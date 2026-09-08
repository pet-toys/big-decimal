using System;
using System.Collections.Generic;
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
    /// <summary>Every shape case in the matrix.</summary>
    public static CultureCase[] All { get; } = Enum.GetValues<CultureCase>();

    // Built once. The randomised suites ask for a culture per case, and a fresh CultureInfo clone
    // with its NumberFormatInfo rewritten is not something to allocate three hundred thousand
    // times for a handful of values that never change.
    private static readonly CultureInfo[] Shapes =
    [
        Build(".", ",", [3]),
        Build(",", ".", [3]),
        Build(".", ",", [3, 2]),
        Build(",", "\u00A0", [3]),
        Build(".", ",", [3, 0]),
        Build(".", ",", []),
    ];

    /// <summary>Returns the culture for a case.</summary>
    /// <param name="culture">The case to build.</param>
    /// <returns>A culture carrying exactly the separators and group sizes the case names.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="culture"/> is not a defined case.</exception>
    public static CultureInfo Get(CultureCase culture) => culture switch
    {
        CultureCase.Invariant or CultureCase.CommaDecimal or CultureCase.NonUniformGroups
            or CultureCase.SpaceGroups or CultureCase.StoppedGroups or CultureCase.NoGroups =>
            Shapes[(int)culture],
        _ => throw new ArgumentOutOfRangeException(nameof(culture)),
    };

    /// <summary>
    /// The cultures that differ from the invariant one only in a pattern index.
    /// </summary>
    /// <remarks>
    /// One culture per index of each of the five patterns a specifier reads: five for
    /// <c>NumberNegativePattern</c>, sixteen and four for currency, twelve and four for percent.
    /// They are separate from the shape cases because a shape and a pattern are independent, and
    /// because a matrix in which every culture carries pattern 1 cannot see a formatter that
    /// ignores the pattern - which is exactly how an <c>N</c> that always prefixed the sign passed
    /// the suite through two changes.
    /// </remarks>
    public static CultureInfo[] Patterns { get; } = BuildPatterns();

    /// <summary>Every culture the matrix holds: the shapes and the patterns.</summary>
    public static CultureInfo[] Every { get; } = [.. Shapes, .. Patterns];

    private static CultureInfo[] BuildPatterns()
    {
        var patterns = new List<CultureInfo>(41);

        for (var index = 0; index < 5; index++)
        {
            patterns.Add(WithPattern(index, static (info, value) => info.NumberNegativePattern = value));
        }

        for (var index = 0; index < 16; index++)
        {
            patterns.Add(WithPattern(index, static (info, value) => info.CurrencyNegativePattern = value));
        }

        for (var index = 0; index < 4; index++)
        {
            patterns.Add(WithPattern(index, static (info, value) => info.CurrencyPositivePattern = value));
        }

        for (var index = 0; index < 12; index++)
        {
            patterns.Add(WithPattern(index, static (info, value) => info.PercentNegativePattern = value));
        }

        for (var index = 0; index < 4; index++)
        {
            patterns.Add(WithPattern(index, static (info, value) => info.PercentPositivePattern = value));
        }

        return [.. patterns];
    }

    private static CultureInfo WithPattern(int index, Action<NumberFormatInfo, int> apply)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        apply(culture.NumberFormat, index);
        return CultureInfo.ReadOnly(culture);
    }

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
