namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The format strings every formatting test and the allocation inventory both run over.
/// </summary>
/// <remarks>
/// <para>
/// One list, two consumers, on purpose: a format string that is checked for correctness and left
/// unmeasured for allocation is exactly the hole that let grouped formatting allocate 64 bytes per
/// call through two changes and a full benchmark run.
/// </para>
/// <para>
/// <see cref="Custom"/> carries the corners rather than the cases that read naturally, because a
/// hand-written engine agrees with a reading of the documentation long before it agrees with the
/// framework. Each of these was a mismatch at some point while the engine was being written:
/// <c>##.##</c> renders zero as the empty string, an explicit negative section suppresses the sign,
/// an empty negative section falls back to the positive one plus a sign, an unterminated quote runs
/// to the end of the format, rounding that carries in a scientific section moves the exponent
/// rather than the mantissa, and a value that rounds to zero is rendered by the zero section.
/// </para>
/// </remarks>
public static class FormatCorpus
{
    /// <summary>Every standard specifier, in both cases, with and without a precision.</summary>
    public static string[] Standard { get; } =
    [
        "", "G", "g", "R", "r", "G1", "G3", "G6", "G12",
        "F", "F0", "F2", "F9", "f3",
        "N", "N0", "N2", "N7", "n1",
        "E", "E0", "E2", "E6", "e3",
        "C", "C0", "C2", "C5", "c1",
        "P", "P0", "P2", "P5", "p1",
    ];

    /// <summary>Custom numeric format strings, corners included.</summary>
    public static string[] Custom { get; } =
    [
        "#,##0.00", "0.0%", "0.00‰", "0,,", "#,##0,,", "0.###E+0", "0.###E-0", "00.##E+00",
        "##.##", "#", "0", "0;0;0", ";;", "'a'#'b'", "\\#0", "###,###", "0.00;(0.00);zero",
        "#.##;;0", "#,#", "#,#.#", "0.0##", "\"lit\"0.0", "0 'kg'", "%0.0", "0.0e+00", "#%",
        "‰0.0", "0.0;-0.0;+0.0", "ZZ", "abc", "0#0", "#0#0.0#0", "0,.#", ",0", "0.0,,%",
        "#,##0.## 'items'", "'''0", "0;;",

        // Each was a mismatch found in review with the corpus above already green: an integer part
        // rendered in full with no placeholder, a comma that scales only after one, a sub-digit
        // mantissa, a second exponent token as literal text, and a zero falling back to section
        // one.
        ".##", ".00", "'x'.00", ".0E+0", ",.00", "0.0E+0E+0", "E+0", "0.0E+0#",
        "0.00;(0.00)", "0.0000;(0.00)", "0.00;(0.0000)", "0.00;(0.00);0.0000", "0.0;-0.0",
    ];

    /// <summary>Everything a caller may pass and get text back for.</summary>
    public static string[] All { get; } = [.. Standard, .. Custom];

    /// <summary>
    /// Format strings that are rejected: a single letter that is not a standard specifier, with or
    /// without a precision.
    /// </summary>
    /// <remarks>
    /// A longer string of the same letters is a custom format and renders as literal text, which is
    /// the distinction the parity suite has to hold: it is not "recognised versus unrecognised".
    /// </remarks>
    public static string[] Rejected { get; } = ["D", "D5", "X", "X8", "Z", "B", "H", "S", "A"];
}
