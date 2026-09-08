using System;
using System.Globalization;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Pins how a value is rendered to text, against <see cref="decimal"/> wherever the specified
/// behaviour is parity with it.
/// </summary>
/// <remarks>
/// The cultures here are built by setting <see cref="NumberFormatInfo"/> explicitly rather than by
/// naming a locale, so that nothing depends on the ICU version of the machine. They go beyond the
/// six in the culture matrix because grouping has more shapes than the randomised suites need.
/// </remarks>
public sealed class FormattingTests
{
    // Every shape a group size list can take. The framework's own setter rejects a zero anywhere
    // but last and rejects anything above nine, so this is the whole domain.
    private static readonly int[][] GroupSizeShapes =
    [
        [3],
        [3, 2],
        [3, 0],
        [2, 3],
        [4],
        [1],
        [],
        [9],
        [3, 2, 4],
        [0],
        [1, 0],
        [9, 9],
    ];

    [Fact]
    public void EveryGroupSizeShape_MatchesDecimal()
    {
        foreach (var groupSizes in GroupSizeShapes)
        {
            var culture = WithGroupSizes(groupSizes);
            var shape = string.Join(",", groupSizes);

            // Up to 28 digits, which is where decimal stops being able to hold the operand and so
            // stops being an oracle.
            for (var digits = 1; digits <= 28; digits++)
            {
                var text = new string('7', digits);
                var reference = decimal.Parse(text, CultureInfo.InvariantCulture);
                var value = BigDecimal.Parse(text, CultureInfo.InvariantCulture);

                value.ToString("N0", culture).Should().Be(
                    reference.ToString("N0", culture),
                    "{0} digits under [{1}]",
                    digits,
                    shape);

                (-value).ToString("N0", culture).Should().Be(
                    (-reference).ToString("N0", culture),
                    "{0} negative digits under [{1}]",
                    digits,
                    shape);
            }
        }
    }

    [Fact]
    public void ASeparator_IsNeverWrittenBeforeTheFirstDigitOrNextToTheSign()
    {
        var culture = WithGroupSizes(3, 2);

        foreach (var text in new[] { "123", "1234", "12345" })
        {
            var value = BigDecimal.Parse(text, CultureInfo.InvariantCulture);

            value.ToString("N0", culture).Should().NotStartWith(",", "a value never opens with a separator");
            (-value).ToString("N0", culture).Should().NotStartWith("-,", "a separator never follows the sign");
        }
    }

    [Fact]
    public void OnlyTheNumberSpecifier_Groups()
    {
        var culture = WithGroupSizes(3, 2);
        var value = BigDecimal.Parse("1234567.89", CultureInfo.InvariantCulture);

        foreach (var specifier in new[] { "F2", "G", "E4" })
        {
            value.ToString(specifier, culture).Should().NotContain(
                ",",
                "{0} does not group, whatever the culture's group sizes are",
                specifier);
        }

        value.ToString("N2", culture).Should().Contain(",", "N does group");
    }

    [Fact]
    public void TheDestinationLengthCheck_CountsTheSeparatorsActuallyWritten()
    {
        var culture = WithGroupSizes(3, 2);
        var value = BigDecimal.Parse("1234567890123", CultureInfo.InvariantCulture);
        var expected = ((decimal)value).ToString("N0", culture);

        var exact = new char[expected.Length];
        value.TryFormat(exact, out var written, "N0", culture).Should().BeTrue("the destination is exactly long enough");
        written.Should().Be(expected.Length);
        new string(exact, 0, written).Should().Be(expected);

        var oneShort = new char[expected.Length - 1];
        value.TryFormat(oneShort, out var none, "N0", culture).Should().BeFalse("the destination is one character short");
        none.Should().Be(0, "a refused format writes nothing and reports nothing");
    }

    [Fact]
    public void BothOverloads_ProduceTheSameText()
    {
        var culture = WithGroupSizes(3, 2);
        var value = BigDecimal.Parse("1234567890123.45", CultureInfo.InvariantCulture);

        var characters = new char[128];
        var bytes = new byte[128];

        value.TryFormat(characters, out var charsWritten, "N2", culture).Should().BeTrue();
        value.TryFormat(bytes, out var bytesWritten, "N2", culture).Should().BeTrue();

        Encoding.UTF8.GetString(bytes, 0, bytesWritten).Should().Be(new string(characters, 0, charsWritten));
    }

    [Fact]
    public void GroupedFormatting_AllocatesNothingWhateverTheProviderIs()
    {
        var value = BigDecimal.Parse("1234567890123.45", CultureInfo.InvariantCulture);
        var buffer = new char[128];
        var readOnly = WithGroupSizes(3, 2);
        var mutable = new NumberFormatInfo { NumberGroupSeparator = ",", NumberGroupSizes = [3, 2] };

        Allocations.Measure(() => Allocations.OtherSink =
                value.TryFormat(buffer, out var written, "N2", readOnly) ? written : -1)
            .Should().Be(0, "grouped formatting under a read-only culture allocates nothing");

        Allocations.Measure(() => Allocations.OtherSink =
                value.TryFormat(buffer, out var written, "N2", mutable) ? written : -1)
            .Should().Be(0, "the guarantee is not conditional on the format info being read-only");
    }

    [Fact]
    public void AChangeToTheCulture_IsPickedUpOnTheNextCall()
    {
        var info = new NumberFormatInfo { NumberGroupSeparator = ",", NumberGroupSizes = [3] };
        var value = BigDecimal.Parse("1234567890123", CultureInfo.InvariantCulture);

        value.ToString("N0", info).Should().Be("1,234,567,890,123");

        info.NumberGroupSizes = [3, 2];

        value.ToString("N0", info).Should().Be(
            "12,34,56,78,90,123",
            "nothing caches the group sizes, so a mutable format info is read fresh");
    }

    [Fact]
    public void AMultiCharacterSeparator_IsCountedAndWrittenInFull()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();

        culture.NumberFormat.NumberGroupSeparator = " - ";
        culture.NumberFormat.NumberGroupSizes = [3, 2];

        var readOnly = CultureInfo.ReadOnly(culture);
        var value = BigDecimal.Parse("1234567890123", CultureInfo.InvariantCulture);
        var expected = ((decimal)value).ToString("N0", readOnly);

        value.ToString("N0", readOnly).Should().Be(expected);

        var exact = new char[expected.Length];
        value.TryFormat(exact, out var written, "N0", readOnly).Should().BeTrue(
            "the length check counts each separator's own length rather than one character apiece");
        written.Should().Be(expected.Length);
    }

    [Fact]
    public void Percent_ScalesTheRenderingRatherThanTheValue()
    {
        // A hundred times MaxValue does not fit the mantissa, and there is nothing to fit: the
        // point moves two places and the rendering grows by two digits. decimal renders its own
        // maximum the same way, which is what settled this as a rendering rather than an operation.
        var rendered = BigDecimal.MaxValue.ToString("P0", CultureInfo.InvariantCulture);

        new string([.. rendered.Where(char.IsAsciiDigit)]).Should().Be(
            BigDecimal.MaxValue.ToString("F0", CultureInfo.InvariantCulture) + "00",
            "scaling by a hundred appends two zeros to an integer rendering and changes nothing else; "
            + "the separators differ because P groups and F does not");
        rendered.Count(char.IsAsciiDigit).Should().Be(80, "the maximum is 78 digits and the point moved two places");

        decimal.MaxValue.ToString("P0", CultureInfo.InvariantCulture).Should().Be(
            ((BigDecimal)decimal.MaxValue).ToString("P0", CultureInfo.InvariantCulture),
            "the two types agree at decimal's own maximum, where multiplying would have overflowed");
    }

    [Theory]
    [InlineData("1.500")]
    [InlineData("0.00")]
    [InlineData("-1234.5678")]
    [InlineData("0")]
    public void RoundTrip_IsThePlainRendering(string text)
    {
        var value = BigDecimal.Parse(text, CultureInfo.InvariantCulture);

        value.ToString("R", CultureInfo.InvariantCulture).Should().Be(
            value.ToString("G", CultureInfo.InvariantCulture),
            "the mantissa is exact, so the value as stored round-trips and R has nothing else to do");
        value.ToString("r", CultureInfo.InvariantCulture).Should().Be(text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TheNumberNegativePattern_IsHonoured(int pattern)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberNegativePattern = pattern;
        var readOnly = CultureInfo.ReadOnly(culture);

        var value = BigDecimal.Parse("-1234.5", CultureInfo.InvariantCulture);

        value.ToString("N1", readOnly).Should().Be(
            (-1234.5m).ToString("N1", readOnly),
            "the pattern lays out the sign, and pattern 0 writes no sign character at all");
    }

    [Theory]
    [InlineData("1234.5678", "G3")]
    [InlineData("1234.5678", "G6")]
    [InlineData("1.000", "G3")]
    [InlineData("100", "G3")]
    [InlineData("0.000012345", "G3")]
    [InlineData("0.00012345", "G1")]
    [InlineData("99999", "G4")]
    [InlineData("0", "G5")]
    [InlineData("-1234.5678", "g3")]
    public void SignificantDigits_MatchDecimal(string text, string format)
    {
        var value = BigDecimal.Parse(text, CultureInfo.InvariantCulture);
        var expected = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

        value.ToString(format, CultureInfo.InvariantCulture).Should().Be(
            expected.ToString(format, CultureInfo.InvariantCulture),
            "G with a precision counts significant digits, strips the zeros the rounding leaves and "
            + "switches to scientific outside its own range");
    }

    [Fact]
    public void TheUtf8Overload_IsBoundedByTheCallersDestination()
    {
        // D4. The overload used to format through an internal buffer of 366 characters and decline
        // anything longer, whatever the caller passed: MaxValue with F300 needs 379 and the char
        // overload wrote them into a destination of the same size.
        var characters = new char[8_192];
        BigDecimal.MaxValue.TryFormat(characters, out var expected, "F300", CultureInfo.InvariantCulture)
            .Should().BeTrue();

        var destination = new byte[8_192];
        BigDecimal.MaxValue.TryFormat(destination, out var written, "F300", CultureInfo.InvariantCulture)
            .Should().BeTrue("the destination is 8 KB, which is ample");
        written.Should().Be(expected, "both overloads write the same number of units for ASCII text");
        Encoding.UTF8.GetString(destination, 0, written).Should().Be(new string(characters, 0, expected));

        var tooShort = new byte[expected - 1];
        BigDecimal.MaxValue.TryFormat(tooShort, out var none, "F300", CultureInfo.InvariantCulture)
            .Should().BeFalse("one unit short is short, and that is the only reason to decline");
        none.Should().Be(0);
    }

    [Fact]
    public void ALongCustomFormat_IsNotCappedByTheImplementation()
    {
        // The 64 KB cap existed to protect a buffer whose size was a guess. The framework has no
        // such limit, and neither does a length that is computed.
        var format = new string('0', 100_000);

        BigDecimal.One.ToString(format, CultureInfo.InvariantCulture).Should().Be(
            1m.ToString(format, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AFormatString_DoesNotReachANonFiniteValue()
    {
        string[] formats =
        [
            "", "G", "G17", "R", "N2", "C2", "E3", "F4", "P1", "0.00", "#,##0.00;(#,##0.00);nil",
        ];

        (BigDecimal Mine, double Theirs)[] values =
        [
            (BigDecimal.NaN, double.NaN),
            (BigDecimal.PositiveInfinity, double.PositiveInfinity),
            (BigDecimal.NegativeInfinity, double.NegativeInfinity),
        ];

        foreach (var culture in CultureMatrix.Every)
        {
            foreach (var (mine, theirs) in values)
            {
                foreach (var format in formats)
                {
                    mine.ToString(format, culture).Should().Be(
                        theirs.ToString(format, culture),
                        "the specifier is not consulted for a non-finite value");
                }
            }
        }
    }

    [Fact]
    public void AnInvalidSpecifier_DoesNotThrowForANonFiniteValue()
    {
        // The one place in this type where an invalid format string does not throw. double answers
        // the same way, and a finite value still throws for the same strings.
        foreach (var format in new[] { "D", "D5", "X", "Z" })
        {
            BigDecimal.NaN.ToString(format, CultureInfo.InvariantCulture).Should().Be(
                double.NaN.ToString(format, CultureInfo.InvariantCulture));

            var finite = () => BigDecimal.One.ToString(format, CultureInfo.InvariantCulture);
            finite.Should().Throw<FormatException>();
        }
    }

    [Fact]
    public void TryFormat_RefusesADestinationShorterThanTheSymbol()
    {
        Span<char> tooShort = stackalloc char[2];
        BigDecimal.NaN.TryFormat(tooShort, out var written, "N2", CultureInfo.InvariantCulture).Should().BeFalse();
        written.Should().Be(0);

        Span<char> enough = stackalloc char[8];
        BigDecimal.NaN.TryFormat(enough, out written, "N2", CultureInfo.InvariantCulture).Should().BeTrue();
        enough[..written].ToString().Should().Be("NaN");

        Span<byte> tooShortUtf8 = stackalloc byte[2];
        BigDecimal.NegativeInfinity.TryFormat(tooShortUtf8, out var bytes, default, CultureInfo.InvariantCulture)
            .Should().BeFalse();
        bytes.Should().Be(0);

        Span<byte> enoughUtf8 = stackalloc byte[16];
        BigDecimal.NegativeInfinity.TryFormat(enoughUtf8, out bytes, default, CultureInfo.InvariantCulture)
            .Should().BeTrue();
        Encoding.UTF8.GetString(enoughUtf8[..bytes]).Should().Be("-Infinity");
    }

    [Fact]
    public void ACultureWithItsOwnSymbols_IsHonouredWhateverTheSpecifier()
    {
        var foreign = CultureMatrix.Get(CultureCase.ForeignSymbols);

        BigDecimal.NaN.ToString("N2", foreign).Should().Be(foreign.NumberFormat.NaNSymbol);
        BigDecimal.PositiveInfinity.ToString("C2", foreign).Should().Be(foreign.NumberFormat.PositiveInfinitySymbol);
        BigDecimal.NegativeInfinity.ToString("0.00", foreign).Should().Be(foreign.NumberFormat.NegativeInfinitySymbol);
    }

    private static CultureInfo WithGroupSizes(params int[] groupSizes)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();

        culture.NumberFormat.NumberGroupSeparator = ",";
        culture.NumberFormat.NumberGroupSizes = groupSizes;

        return CultureInfo.ReadOnly(culture);
    }
}
