using System.Globalization;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Pins the accessor that reads a culture's group sizes without the copy the property makes.
/// </summary>
/// <remarks>
/// The accessor binds to a private field of <see cref="NumberFormatInfo"/>, so the framework can
/// take it away in a way the compiler cannot see.
/// <see cref="TheField_IsStillReachable"/> is what notices: comparing the accessor's output against
/// the property cannot, because the fallback returns the property, so a renamed field would leave
/// every value correct and every other test here green. Nothing depends on operating-system locale
/// data, for the reason the harness README gives.
/// </remarks>
public sealed class CultureDataTests
{
    [Fact]
    public void TheField_IsStillReachable()
    {
        CultureData.ReadsTheFieldDirectly.Should().BeTrue(
            "a runtime that renames the field costs 32 bytes and 48 ns on every grouped format, "
            + "and the fallback hides that from every assertion about values");
    }

    [Fact]
    public void TheAccessor_AgreesWithTheProperty()
    {
        foreach (var info in Infos())
        {
            // The accessor first, deliberately: the field must be populated before anything has
            // gone through the property, or a lazily initialised field would pass here and fail
            // for a caller who formats before reading the culture.
            var throughField = CultureData.NumberGroupSizes(info).ToArray();
            var throughProperty = info.NumberGroupSizes;

            throughField.Should().Equal(
                throughProperty,
                "the accessor reads the field the property clones, for [{0}]",
                string.Join(",", throughProperty));
        }
    }

    [Fact]
    public void TheAccessor_ReadsTheCurrentValueRatherThanACachedOne()
    {
        var info = new NumberFormatInfo { NumberGroupSizes = [3] };

        CultureData.NumberGroupSizes(info).ToArray().Should().Equal([3]);

        info.NumberGroupSizes = [4, 2];

        CultureData.NumberGroupSizes(info).ToArray().Should().Equal(
            [4, 2],
            "a NumberFormatInfo that is not read-only can change between two calls, so nothing may cache it");
    }

    [Fact]
    public void TheAccessor_AllocatesNothing()
    {
        var readOnly = NumberFormatInfo.InvariantInfo;
        var mutable = new NumberFormatInfo { NumberGroupSizes = [3, 2] };

        Allocations.Measure(() => Allocations.OtherSink = CultureData.NumberGroupSizes(readOnly).Length)
            .Should().Be(0, "the property clones 32 bytes on every read and the accessor exists to avoid it");

        Allocations.Measure(() => Allocations.OtherSink = CultureData.NumberGroupSizes(mutable).Length)
            .Should().Be(0, "the guarantee is not conditional on the format info being read-only");
    }

    private static NumberFormatInfo[] Infos() =>
    [
        NumberFormatInfo.InvariantInfo,
        NumberFormatInfo.CurrentInfo,
        new NumberFormatInfo { NumberGroupSizes = [3, 2] },
        new NumberFormatInfo { NumberGroupSizes = [3, 0] },
        new NumberFormatInfo { NumberGroupSizes = [] },
        new NumberFormatInfo { NumberGroupSizes = [9] },
    ];
}
