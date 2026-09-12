using System;
using System.Globalization;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// A ClickHouse decimal column type: its declared precision and scale, and the payload width they
/// imply.
/// </summary>
/// <remarks>
/// Both spellings parse: a read hook is given the normalised <c>Decimal(38, 10)</c> and a query
/// annotates a parameter with the width-named <c>Decimal128(10)</c>. The width follows from the
/// precision, and both bounds are carried because neither subsumes the other.
/// </remarks>
internal readonly struct ClickHouseColumnType
{
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

    /// <summary>
    /// Reads a declared ClickHouse type in either spelling, optionally wrapped in
    /// <c>Nullable(...)</c>, answering <see langword="false"/> for anything that is not a decimal.
    /// </summary>
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

    private static bool TryReadNumber(ReadOnlySpan<char> text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
