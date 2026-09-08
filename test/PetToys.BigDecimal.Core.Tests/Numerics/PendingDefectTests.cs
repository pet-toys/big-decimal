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
}
