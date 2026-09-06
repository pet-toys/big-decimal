using System.Globalization;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Executable pins for defects that are known, specified and not yet fixed. Each one asserts what
/// the specification requires and is skipped with a reason naming the change that will enable it.
/// </summary>
/// <remarks>
/// None of these asserts the behaviour the code has today. A test that pinned the wrong answer
/// would turn the eventual fix into a red build, and would read as an endorsement in the meantime.
/// </remarks>
public sealed class PendingDefectTests
{
    [Fact(Skip = Pending.LeadingWhitespaceSeparator)]
    public void ALeadingGroupSeparator_IsRefusedEvenWhenItIsWhitespace()
    {
        // Found by the culture matrix. A leading separator is refused, as decimal refuses it —
        // unless the culture's separator happens to be a non-breaking space, where it is swallowed
        // as leading white space and the value comes back as 234.
        var culture = CultureMatrix.Get(CultureCase.SpaceGroups);
        var text = culture.NumberFormat.NumberGroupSeparator + "234";

        decimal.TryParse(text, NumberStyles.Number, culture, out _).Should().BeFalse();
        BigDecimal.TryParse(text, NumberStyles.Number, culture, out _).Should().BeFalse();
    }

    [Fact(Skip = Pending.Formatting)]
    public void TheUtf8Overload_IsBoundedByTheCallersDestination()
    {
        // D4. The UTF-8 TryFormat is bounded by an internal buffer of its own rather than by the
        // span it was handed, so a long format fails into 8 KB while the char overload succeeds.
        var destination = new byte[8_192];
        var characters = new char[8_192];

        BigDecimal.MaxValue.TryFormat(characters, out var expected, "F300", CultureInfo.InvariantCulture)
            .Should().BeTrue();
        BigDecimal.MaxValue.TryFormat(destination, out var written, "F300", CultureInfo.InvariantCulture)
            .Should().BeTrue("the destination is 8 KB, which is ample");
        written.Should().Be(expected, "both overloads write the same number of units");
    }
}
