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
/// most of what the <c>N</c> specifier cost over <c>F</c>. Caching the array instead is not an
/// option: a <see cref="NumberFormatInfo"/> that is not read-only can change between two calls,
/// and a cache would go on serving the old list.
/// </remarks>
internal static class CultureData
{
    // Probed once. A runtime that renames the field leaves this false and the property path runs,
    // so the worst case is the cost above rather than a MissingFieldException at the first format
    // call. A field initialiser rather than a static constructor, so the type keeps beforefieldinit
    // and the branch below stays inlineable.
    private static readonly bool FieldIsReachable = ProbeNumberGroupSizes();

    /// <summary>Whether the field is being read directly rather than through the property.</summary>
    /// <remarks>
    /// Exposed for the suite to assert. Nothing else can detect a renamed field: the fallback
    /// returns exactly what the property returns, so every value this class hands out stays
    /// correct while the cost and the allocation come back.
    /// </remarks>
    internal static bool ReadsTheFieldDirectly => FieldIsReachable;

    /// <summary>Returns a culture's group sizes without the copy the property makes.</summary>
    /// <param name="info">The format info to read.</param>
    /// <returns>
    /// The sizes as the framework stores them: the first entry sizes the rightmost group, the last
    /// entry repeats for every group beyond it, an entry of zero stops grouping, and an empty span
    /// means no grouping at all.
    /// </returns>
    internal static ReadOnlySpan<int> NumberGroupSizes(NumberFormatInfo info) =>
        FieldIsReachable ? NumberGroupSizesField(info) : info.NumberGroupSizes;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_numberGroupSizes")]
    private static extern ref int[] NumberGroupSizesField(NumberFormatInfo info);

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
}
