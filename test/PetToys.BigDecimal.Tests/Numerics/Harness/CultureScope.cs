using System;
using System.Globalization;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// Sets the current culture for the duration of a block and restores it afterwards, whether the
/// block returns or throws.
/// </summary>
/// <remarks>
/// Only the tests that are about the ambient culture should reach for this; everything else passes
/// an <see cref="IFormatProvider"/> explicitly, which is both clearer and safe to run in parallel.
/// </remarks>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo previousCulture;
    private readonly CultureInfo previousUiCulture;
    private bool restored;

    /// <summary>Switches the current culture.</summary>
    /// <param name="culture">The culture to run under.</param>
    public CultureScope(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        previousCulture = CultureInfo.CurrentCulture;
        previousUiCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>Switches to one of the matrix cultures.</summary>
    /// <param name="culture">The case to run under.</param>
    /// <returns>A scope that restores the previous culture.</returns>
    public static CultureScope For(CultureCase culture) => new(CultureMatrix.Get(culture));

    /// <summary>Restores the culture that was in effect before the scope began.</summary>
    public void Dispose()
    {
        if (restored)
        {
            return;
        }

        restored = true;
        CultureInfo.CurrentCulture = previousCulture;
        CultureInfo.CurrentUICulture = previousUiCulture;
    }
}
