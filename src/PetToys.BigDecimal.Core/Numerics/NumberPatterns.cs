namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The sign, symbol and digit layouts that <see cref="System.Globalization.NumberFormatInfo"/>
/// selects between by pattern index.
/// </summary>
/// <remarks>
/// Every layout was read back from <see cref="decimal"/> under a culture whose sign, symbol and
/// separators were distinct markers, not transcribed from the documentation. The five number
/// layouts are why this class exists: <c>N</c> prefixed the negative sign under all five, so a
/// culture on pattern 0 was told <c>-1,234.5</c> where <see cref="decimal"/> says <c>(1,234.5)</c>.
/// <para>
/// Tokens: <c>n</c> is the digits, <c>-</c> the negative sign, <c>$</c> and <c>%</c> the currency
/// and percent symbols; anything else that is not a space or a parenthesis is treated as the
/// symbol, so the two symbol tokens need not be told apart.
/// </para>
/// </remarks>
internal static class NumberPatterns
{
    /// <summary>Returns the layout for a value formatted with <c>N</c> and a negative sign.</summary>
    /// <param name="pattern">The culture's <c>NumberNegativePattern</c>, 0 through 4.</param>
    /// <returns>The layout, or the layout of pattern 1 for an index the framework does not define.</returns>
    internal static string NumberNegative(int pattern) => pattern switch
    {
        0 => "(n)",
        2 => "- n",
        3 => "n-",
        4 => "n -",
        _ => "-n",
    };

    /// <summary>Returns the layout for a positive value formatted with <c>C</c>.</summary>
    /// <param name="pattern">The culture's <c>CurrencyPositivePattern</c>, 0 through 3.</param>
    /// <returns>The layout, or the layout of pattern 0 for an index the framework does not define.</returns>
    internal static string CurrencyPositive(int pattern) => pattern switch
    {
        1 => "n$",
        2 => "$ n",
        3 => "n $",
        _ => "$n",
    };

    /// <summary>Returns the layout for a negative value formatted with <c>C</c>.</summary>
    /// <param name="pattern">The culture's <c>CurrencyNegativePattern</c>, 0 through 15.</param>
    /// <returns>The layout, or the layout of pattern 0 for an index the framework does not define.</returns>
    internal static string CurrencyNegative(int pattern) => pattern switch
    {
        1 => "-$n",
        2 => "$-n",
        3 => "$n-",
        4 => "(n$)",
        5 => "-n$",
        6 => "n-$",
        7 => "n$-",
        8 => "-n $",
        9 => "-$ n",
        10 => "n $-",
        11 => "$ n-",
        12 => "$ -n",
        13 => "n- $",
        14 => "($ n)",
        15 => "(n $)",
        _ => "($n)",
    };

    /// <summary>Returns the layout for a positive value formatted with <c>P</c>.</summary>
    /// <param name="pattern">The culture's <c>PercentPositivePattern</c>, 0 through 3.</param>
    /// <returns>The layout, or the layout of pattern 0 for an index the framework does not define.</returns>
    internal static string PercentPositive(int pattern) => pattern switch
    {
        1 => "n%",
        2 => "%n",
        3 => "% n",
        _ => "n %",
    };

    /// <summary>Returns the layout for a negative value formatted with <c>P</c>.</summary>
    /// <param name="pattern">The culture's <c>PercentNegativePattern</c>, 0 through 11.</param>
    /// <returns>The layout, or the layout of pattern 0 for an index the framework does not define.</returns>
    internal static string PercentNegative(int pattern) => pattern switch
    {
        1 => "-n%",
        2 => "-%n",
        3 => "%-n",
        4 => "%n-",
        5 => "n-%",
        6 => "n%-",
        7 => "-% n",
        8 => "n %-",
        9 => "% n-",
        10 => "% -n",
        11 => "n- %",
        _ => "-n %",
    };
}
