namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// The sign, symbol and digit layouts that <see cref="System.Globalization.NumberFormatInfo"/>
/// selects between by pattern index.
/// </summary>
/// <remarks>
/// Every layout was read back from <see cref="decimal"/> under a culture whose sign, symbol and
/// separators were distinct markers. Tokens: <c>n</c> is the digits, <c>-</c> the negative sign,
/// and anything but a space or a parenthesis is the symbol.
/// </remarks>
internal static class NumberPatterns
{
    /// <summary>Returns the layout for a value formatted with <c>N</c> and a negative sign.</summary>
    internal static string NumberNegative(int pattern) => pattern switch
    {
        0 => "(n)",
        2 => "- n",
        3 => "n-",
        4 => "n -",
        _ => "-n",
    };

    /// <summary>Returns the layout for a positive value formatted with <c>C</c>.</summary>
    internal static string CurrencyPositive(int pattern) => pattern switch
    {
        1 => "n$",
        2 => "$ n",
        3 => "n $",
        _ => "$n",
    };

    /// <summary>Returns the layout for a negative value formatted with <c>C</c>.</summary>
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
    internal static string PercentPositive(int pattern) => pattern switch
    {
        1 => "n%",
        2 => "%n",
        3 => "% n",
        _ => "n %",
    };

    /// <summary>Returns the layout for a negative value formatted with <c>P</c>.</summary>
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
