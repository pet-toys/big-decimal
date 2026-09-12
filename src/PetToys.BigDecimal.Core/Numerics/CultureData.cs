using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Reads culture data that the framework hands out as a defensive copy.
/// </summary>
/// <remarks>
/// The group size properties clone their array on every read - 32 bytes and 48.7 ns against
/// 0.22 ns for the field - and that copy was the last allocation in the type. Caching is not an
/// option: a writable <see cref="NumberFormatInfo"/> can change between calls.
/// </remarks>
internal static class CultureData
{
    // Probed per property, so a renamed field is not hidden by the two that still resolve.
    private static readonly bool NumberFieldIsReachable = ProbeNumberGroupSizes();

    private static readonly bool CurrencyFieldIsReachable = ProbeCurrencyGroupSizes();

    private static readonly bool PercentFieldIsReachable = ProbePercentGroupSizes();

    /// <summary>Whether the number group sizes are read from the field rather than the property.</summary>
    /// <remarks>For the suite: the fallback is correct, so nothing else would notice a renamed field.</remarks>
    internal static bool ReadsTheNumberFieldDirectly => NumberFieldIsReachable;

    /// <summary>Whether the currency group sizes are read from the field rather than the property.</summary>
    internal static bool ReadsTheCurrencyFieldDirectly => CurrencyFieldIsReachable;

    /// <summary>Whether the percent group sizes are read from the field rather than the property.</summary>
    internal static bool ReadsThePercentFieldDirectly => PercentFieldIsReachable;

    /// <summary>Returns a culture's number group sizes without the copy the property makes.</summary>
    /// <returns>
    /// The sizes as the framework stores them: the first entry sizes the rightmost group, the last
    /// repeats, a zero stops grouping, and an empty span means no grouping at all.
    /// </returns>
    internal static ReadOnlySpan<int> NumberGroupSizes(NumberFormatInfo info) =>
        NumberFieldIsReachable ? NumberGroupSizesField(info) : info.NumberGroupSizes;

    /// <summary>Returns a culture's currency group sizes without the copy the property makes.</summary>
    internal static ReadOnlySpan<int> CurrencyGroupSizes(NumberFormatInfo info) =>
        CurrencyFieldIsReachable ? CurrencyGroupSizesField(info) : info.CurrencyGroupSizes;

    /// <summary>Returns a culture's percent group sizes without the copy the property makes.</summary>
    internal static ReadOnlySpan<int> PercentGroupSizes(NumberFormatInfo info) =>
        PercentFieldIsReachable ? PercentGroupSizesField(info) : info.PercentGroupSizes;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_numberGroupSizes")]
    private static extern ref int[] NumberGroupSizesField(NumberFormatInfo info);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_currencyGroupSizes")]
    private static extern ref int[] CurrencyGroupSizesField(NumberFormatInfo info);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_percentGroupSizes")]
    private static extern ref int[] PercentGroupSizesField(NumberFormatInfo info);

    // Three probes rather than one taking a delegate, which would be this class's one allocation.
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
