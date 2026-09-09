using System;
using System.Globalization;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// A ClickHouse decimal column type: its declared precision and scale, and the payload width they
/// imply.
/// </summary>
/// <remarks>
/// <para>
/// Two spellings reach this package and both have to parse, because the two paths are handed
/// different ones. A read hook is given the normalised form, <c>Decimal(38, 10)</c>, while a query
/// annotates a parameter with the width-named form, <c>Decimal128(10)</c>. They describe the same
/// column.
/// </para>
/// <para>
/// The width follows from the precision rather than being stated: 9, 18, 38 and 76 digits are held
/// by 4, 8, 16 and 32 bytes. Both bounds matter and neither subsumes the other, which is why this
/// type carries both. A <c>Decimal64(2)</c> has eight bytes, holding a magnitude up to about
/// 9.22e18, and eighteen digits of precision, holding one up to 1e18: a mantissa between the two
/// fits the payload and is refused by the server.
/// </para>
/// </remarks>
internal readonly struct ClickHouseColumnType
{
    /// <summary>The largest precision ClickHouse allows, which is <c>Decimal256</c>.</summary>
    internal const int MaxPrecision = 76;

    private ClickHouseColumnType(int precision, int scale)
    {
        this.Precision = precision;
        this.Scale = scale;
    }

    /// <summary>The digits the declared type allows, 1 to <see cref="MaxPrecision"/>.</summary>
    internal int Precision { get; }

    /// <summary>The scale of the column, 0 to <see cref="Precision"/>.</summary>
    internal int Scale { get; }

    /// <summary>The payload width in bytes, which the precision determines.</summary>
    internal int Width => this.Precision switch
    {
        <= 9 => ClickHouseDecimal.Decimal32Size,
        <= 18 => ClickHouseDecimal.Decimal64Size,
        <= 38 => ClickHouseDecimal.Decimal128Size,
        _ => ClickHouseDecimal.Decimal256Size,
    };

    /// <summary>How the type is written where a person has to read it.</summary>
    internal string Declared =>
        string.Create(CultureInfo.InvariantCulture, $"Decimal({this.Precision}, {this.Scale})");

    /// <summary>Reads a declared ClickHouse type, if it is a decimal one.</summary>
    /// <param name="declared">
    /// The type as the driver or the server spells it, in either the normalised
    /// <c>Decimal(p, s)</c> form or the width-named <c>Decimal128(s)</c> one, optionally wrapped in
    /// <c>Nullable(...)</c>.
    /// </param>
    /// <param name="type">The parsed type, when this returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="declared"/> is a decimal column type.
    /// </returns>
    /// <remarks>
    /// A type this cannot read is not an error here. It is the answer "not a decimal column", which
    /// every caller of this method has to handle anyway, since the read hook sees every column of
    /// every row and a parameter can carry anything.
    /// </remarks>
    internal static bool TryParse(string? declared, out ClickHouseColumnType type)
    {
        type = default;

        if (string.IsNullOrEmpty(declared))
        {
            return false;
        }

        var text = declared.AsSpan().Trim();
        text = Unwrap(text, "Nullable");

        var open = text.IndexOf('(');
        if (open < 0 || text[^1] != ')')
        {
            return false;
        }

        var name = text[..open].TrimEnd();
        var arguments = text[(open + 1)..^1];

        // Decimal(p, s) states both; the width-named forms state the scale and imply the precision.
        var declaredPrecision = name switch
        {
            "Decimal" => 0,
            "Decimal32" => 9,
            "Decimal64" => 18,
            "Decimal128" => 38,
            "Decimal256" => MaxPrecision,
            _ => -1,
        };

        if (declaredPrecision < 0)
        {
            return false;
        }

        int precision;
        int scale;
        var comma = arguments.IndexOf(',');
        if (declaredPrecision == 0)
        {
            if (comma < 0
                || !TryReadNumber(arguments[..comma], out precision)
                || !TryReadNumber(arguments[(comma + 1)..], out scale))
            {
                return false;
            }
        }
        else
        {
            if (comma >= 0 || !TryReadNumber(arguments, out scale))
            {
                return false;
            }

            precision = declaredPrecision;
        }

        if (precision is < 1 or > MaxPrecision || scale < 0 || scale > precision)
        {
            return false;
        }

        type = new ClickHouseColumnType(precision, scale);

        return true;
    }

    /// <summary>Removes one <c>Wrapper(...)</c> layer, when the text carries it.</summary>
    /// <param name="text">The declared type.</param>
    /// <param name="wrapper">The wrapper to remove.</param>
    /// <returns>What the wrapper held, or the text unchanged.</returns>
    private static ReadOnlySpan<char> Unwrap(ReadOnlySpan<char> text, ReadOnlySpan<char> wrapper)
    {
        if (!text.StartsWith(wrapper, StringComparison.Ordinal)
            || text.Length <= wrapper.Length + 2
            || text[wrapper.Length] != '('
            || text[^1] != ')')
        {
            return text;
        }

        return text[(wrapper.Length + 1)..^1].Trim();
    }

    /// <summary>Reads one decimal argument of a type declaration.</summary>
    /// <param name="text">The argument, with any surrounding space.</param>
    /// <param name="value">The number, when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the argument is a number.</returns>
    private static bool TryReadNumber(ReadOnlySpan<char> text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
