using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using PetToys.BigDecimal.Numerics;

namespace BigDecimalProbes;

/// <summary>
/// Publishes trimmed and Native AOT, and answers whether the core package still works when
/// reflection is gone. Every expectation below was captured from the jitted build; nothing here
/// computes both sides of a comparison with the code under test.
/// </summary>
internal static class Program
{
    private const int DeclaredChecks = 9;

    private static int Main()
    {
        var invariant = CultureInfo.InvariantCulture;
        var german = CultureInfo.GetCultureInfo("de-DE");

        // The ends of the range, where a formatting or parsing path that lost a word would show it.
        var max = BigDecimal.MaxValue;
        Probe.Check("max.format", ExpectedMax, max.ToString(invariant));
        Probe.Check("max.roundtrip", ExpectedMax, BigDecimal.Parse(ExpectedMax, invariant).ToString(invariant));

        // A value carrying a long fraction: 77 significant digits do not survive a double, and the
        // trailing zero is there because the scale carries it and formatting must not drop it.
        var scaled = BigDecimal.FromScaled(BigInteger.Parse(UnscaledDigits, invariant), 20);
        Probe.Check("scaled.format", ExpectedScaled, scaled.ToString(invariant));

        // Culture data is the part of the formatting path a trimmer could remove: these two fail
        // on an invariant-globalization build, which is why the probe does not make one.
        var grouped = BigDecimal.Parse("1234567.890", invariant);
        Probe.Check("culture.format", ExpectedGerman, grouped.ToString("N", german));
        Probe.Check("culture.parse", ExpectedGermanParsed, BigDecimal.Parse(ExpectedGerman, german).ToString(invariant));

        Span<byte> utf8 = stackalloc byte[64];
        var utf8Text = grouped.TryFormat(utf8, out var utf8Written, default, invariant)
            ? Encoding.UTF8.GetString(utf8[..utf8Written])
            : "TryFormat returned false";
        Probe.Check("utf8.format", ExpectedUtf8, utf8Text);

        // The JSON converter, reached through [JsonConverter] on the type. Two of the three forms
        // go through reflection-based serialization, which Native AOT disables outright: there the
        // expectation is the refusal, and the source-generated form is the one that must work.
        var value = BigDecimal.Parse("123.4500", invariant);
        var reflection = JsonSerializer.IsReflectionEnabledByDefault;

        Probe.Check("json.direct", reflection ? ExpectedJsonValue : ReflectionRefused, SerializeValue(value));

        Probe.Check(
            "json.property",
            reflection ? ExpectedJsonHolder : ReflectionRefused,
            SerializeHolder(new Holder { Amount = value }));

        var generated = JsonSerializer.Serialize(new Holder { Amount = value }, ProbeJsonContext.Default.Holder);
        var read = JsonSerializer.Deserialize(generated, ProbeJsonContext.Default.Holder);
        Probe.Check(
            "json.sourcegen",
            ExpectedJsonHolder + "|" + ExpectedJsonRead,
            generated + "|" + (read is null ? "null" : read.Amount.ToString(invariant)));

        return Probe.Done("core", DeclaredChecks);
    }

    /// <summary>
    /// Serializes through the reflection-based entry point on purpose, and reports the exception
    /// type rather than the text when the runtime refuses, so that a refusal is a comparable
    /// result instead of a crashed probe.
    /// </summary>
    /// <remarks>
    /// The suppressions are the point rather than a workaround: the analyzers are right that this
    /// call cannot be proved safe, and what the probe measures is what the runtime does with it
    /// once trimming or Native AOT has removed the machinery behind it. Nothing this method
    /// returns is used except as a compared string.
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Deliberate: the probe exists to observe what this call does in a trimmed binary.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Deliberate: the probe exists to observe what this call does in a Native AOT binary.")]
    private static string SerializeValue(BigDecimal value)
    {
        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch (NotSupportedException e)
        {
            return e.GetType().Name;
        }
        catch (InvalidOperationException e)
        {
            return e.GetType().Name;
        }
    }

    /// <summary>
    /// The same, for a value reached as a property of an enclosing object.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Deliberate: the probe exists to observe what this call does in a trimmed binary.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Deliberate: the probe exists to observe what this call does in a Native AOT binary.")]
    private static string SerializeHolder(Holder holder)
    {
        try
        {
            return JsonSerializer.Serialize(holder);
        }
        catch (NotSupportedException e)
        {
            return e.GetType().Name;
        }
        catch (InvalidOperationException e)
        {
            return e.GetType().Name;
        }
    }

    private const string ExpectedMax = "115792089237316195423570985008687907853269984665640564039457584007913129639935";

    private const string UnscaledDigits = "12345678901234567890123456789012345678901234567890";

    private const string ExpectedScaled = "123456789012345678901234567890.12345678901234567890";

    private const string ExpectedGerman = "1.234.567,890";

    private const string ExpectedGermanParsed = "1234567.890";

    private const string ExpectedUtf8 = "1234567.890";

    private const string ExpectedJsonValue = "\"123.4500\"";

    private const string ExpectedJsonHolder = "{\"Amount\":\"123.4500\"}";

    private const string ExpectedJsonRead = "123.4500";

    private const string ReflectionRefused = "InvalidOperationException";
}
