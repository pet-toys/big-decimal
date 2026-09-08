using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Reads culture data that the framework hands out as a defensive copy.
/// </summary>
/// <remarks>
/// <see cref="NumberFormatInfo.NumberGroupSizes"/> clones its array on every read, measured on
/// .NET 10 at 32 bytes and 48.7 ns against 0.22 ns for the field it clones. Grouped formatting
/// needs the list on every call, so that copy was both the only allocation left in the type and
/// most of what the <c>N</c> specifier cost over <c>F</c>. <see cref="NumberFormatInfo.CurrencyGroupSizes"/>
/// and <see cref="NumberFormatInfo.PercentGroupSizes"/> clone in exactly the same way, so <c>C</c>
/// and <c>P</c> would have reopened that allocation the day they were accepted. Caching the arrays
/// instead is not an option: a <see cref="NumberFormatInfo"/> that is not read-only can change
/// between two calls, and a cache would go on serving the old list.
/// </remarks>
internal static class CultureData
{
    // Probed once per property, not once for the class. A runtime that renames one field must not
    // be hidden by the two that still resolve, and the worst case per property is the cost above
    // rather than a MissingFieldException at the first format call. Field initialisers rather than
    // a static constructor, so the type keeps beforefieldinit and the branches stay inlineable.
    private static readonly bool NumberFieldIsReachable = ProbeNumberGroupSizes();

    private static readonly bool CurrencyFieldIsReachable = ProbeCurrencyGroupSizes();

    private static readonly bool PercentFieldIsReachable = ProbePercentGroupSizes();

    /// <summary>Whether the number group sizes are read from the field rather than the property.</summary>
    /// <remarks>
    /// Exposed for the suite to assert. Nothing else can detect a renamed field: the fallback
    /// returns exactly what the property returns, so every value this class hands out stays
    /// correct while the cost and the allocation come back.
    /// </remarks>
    internal static bool ReadsTheNumberFieldDirectly => NumberFieldIsReachable;

    /// <summary>Whether the currency group sizes are read from the field rather than the property.</summary>
    internal static bool ReadsTheCurrencyFieldDirectly => CurrencyFieldIsReachable;

    /// <summary>Whether the percent group sizes are read from the field rather than the property.</summary>
    internal static bool ReadsThePercentFieldDirectly => PercentFieldIsReachable;

    /// <summary>Returns a culture's number group sizes without the copy the property makes.</summary>
    /// <param name="info">The format info to read.</param>
    /// <returns>
    /// The sizes as the framework stores them: the first entry sizes the rightmost group, the last
    /// entry repeats for every group beyond it, an entry of zero stops grouping, and an empty span
    /// means no grouping at all.
    /// </returns>
    internal static ReadOnlySpan<int> NumberGroupSizes(NumberFormatInfo info) =>
        NumberFieldIsReachable ? NumberGroupSizesField(info) : info.NumberGroupSizes;

    /// <summary>Returns a culture's currency group sizes without the copy the property makes.</summary>
    /// <param name="info">The format info to read.</param>
    /// <returns>The sizes as the framework stores them, read the same way as the number sizes.</returns>
    internal static ReadOnlySpan<int> CurrencyGroupSizes(NumberFormatInfo info) =>
        CurrencyFieldIsReachable ? CurrencyGroupSizesField(info) : info.CurrencyGroupSizes;

    /// <summary>Returns a culture's percent group sizes without the copy the property makes.</summary>
    /// <param name="info">The format info to read.</param>
    /// <returns>The sizes as the framework stores them, read the same way as the number sizes.</returns>
    internal static ReadOnlySpan<int> PercentGroupSizes(NumberFormatInfo info) =>
        PercentFieldIsReachable ? PercentGroupSizesField(info) : info.PercentGroupSizes;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_numberGroupSizes")]
    private static extern ref int[] NumberGroupSizesField(NumberFormatInfo info);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_currencyGroupSizes")]
    private static extern ref int[] CurrencyGroupSizesField(NumberFormatInfo info);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_percentGroupSizes")]
    private static extern ref int[] PercentGroupSizesField(NumberFormatInfo info);

    // Three probes rather than one taking a delegate: a delegate would be the only allocation this
    // class makes, and it would sit in the type initialiser of the type that exists to avoid one.
    private static bool ProbeNumberGroupSizes()
    {
        try
        {
            return NumberGroupSizesField(NumberFormatInfo.InvariantInfo) is not null;
        }
        catch (MissingFieldException)
        {
            return false;
        }
    }

    private static bool ProbeCurrencyGroupSizes()
    {
        try
        {
            return CurrencyGroupSizesField(NumberFormatInfo.InvariantInfo) is not null;
        }
        catch (MissingFieldException)
        {
            return false;
        }
    }

    private static bool ProbePercentGroupSizes()
    {
        try
        {
            return PercentGroupSizesField(NumberFormatInfo.InvariantInfo) is not null;
        }
        catch (MissingFieldException)
        {
            return false;
        }
    }
}
