using System.Globalization;
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

    private static CultureInfo WithGroupSizes(params int[] groupSizes)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();

        culture.NumberFormat.NumberGroupSeparator = ",";
        culture.NumberFormat.NumberGroupSizes = groupSizes;

        return CultureInfo.ReadOnly(culture);
    }
}
