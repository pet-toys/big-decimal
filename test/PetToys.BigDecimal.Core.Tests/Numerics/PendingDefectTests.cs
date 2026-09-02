using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
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
    [Fact(Skip = Pending.NonUniformGroupSizes)]
    public void TheNumberSpecifier_HonoursEveryEntryOfNumberGroupSizes()
    {
        // Found by the culture matrix. Groups of three then two is what the Indian subcontinent
        // writes and what NumberGroupSizes exists to express; only its first entry is read.
        var culture = CultureMatrix.Get(CultureCase.NonUniformGroups);
        var value = BigDecimal.Parse("-184467440737095516", CultureInfo.InvariantCulture);

        value.ToString("N0", culture).Should().Be(((decimal)value).ToString("N0", culture));
    }

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

    [Fact(Skip = Pending.NumericContracts)]
    public void CreateSaturating_Saturates()
    {
        // D5. TryConvertToChecked, TryConvertToSaturating and TryConvertToTruncating share one
        // checked implementation, so saturating and truncating conversions throw where they should
        // clamp or discard.
        var beyondByte = BigDecimal.Parse("300", CultureInfo.InvariantCulture);

        // Only the saturating direction is asserted. What CreateTruncating owes for a value past
        // the target's range is for the conversion contracts work to settle against the base
        // class library; a pin guessing at it would leave whoever unskips this unable to tell which
        // side is wrong.
        Create<byte, BigDecimal>(beyondByte).Should().Be(byte.MaxValue);
    }

    [Fact(Skip = Pending.NumericContracts)]
    public void CreateSaturating_FallsBackToTheOtherType()
    {
        // D5. An unsupported source type silently yields zero instead of asking the other type to
        // convert itself.
        Create<BigDecimal, Half>((Half)1.5f)
            .Should().Be(BigDecimal.Parse("1.5", CultureInfo.InvariantCulture));
    }

    [Fact(Skip = Pending.NumericContracts)]
    public void TheCreateMethods_AreCallableOnTheTypeItself()
    {
        // D6. Create* are explicit interface implementations, so BigDecimal.CreateChecked(x) does
        // not compile, unlike every numeric type in the base class library. Asserted by reflection
        // because a test that called it directly could not be committed until the fix lands.
        var methods = typeof(BigDecimal)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        methods.Should().Contain("CreateChecked");
        methods.Should().Contain("CreateSaturating");
        methods.Should().Contain("CreateTruncating");
    }

    [Fact(Skip = Pending.NumericContracts)]
    public void TheGenericConversions_DoNotBox()
    {
        // D7. Both directions box, so a conversion allocates where nothing else in the type does.
        var value = BigDecimal.Parse("42", CultureInfo.InvariantCulture);

        Allocations.Measure(() => Allocations.OtherSink = Create<long, BigDecimal>(value))
            .Should().Be(0, "converting out of BigDecimal must not box");
        Allocations.Measure(() => Allocations.Sink = Create<BigDecimal, long>(42L))
            .Should().Be(0, "converting into BigDecimal must not box");
    }

    [Fact(Skip = Pending.NumericContracts)]
    public void ADoubleRoundTrips()
    {
        // D8. (BigDecimal)double converts through "G15" and (BigDecimal)float through "G7", so a
        // double does not come back: the shortest round-trippable form needs 17 digits.
        const double value = 0.1234567890123456789d;

        ((double)(BigDecimal)value).Should().Be(value);
        ((float)(BigDecimal)0.12345678f).Should().Be(0.12345678f);
    }

    private static T Create<T, TOther>(TOther value)
        where T : System.Numerics.INumberBase<T>
        where TOther : System.Numerics.INumberBase<TOther> =>
        T.CreateSaturating(value);
}
