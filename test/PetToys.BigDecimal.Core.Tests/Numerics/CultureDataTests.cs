using System.Globalization;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Pins the accessors that read a culture's group sizes without the copy the properties make.
/// </summary>
/// <remarks>
/// The accessors bind to private fields of <see cref="NumberFormatInfo"/>, so the framework can
/// take them away in a way the compiler cannot see.
/// <see cref="EveryField_IsStillReachable"/> is what notices: comparing an accessor's output
/// against its property cannot, because the fallback returns the property, so a renamed field would
/// leave every value correct and every other test here green. There is one probe per property
/// rather than one for the class, so a renamed currency field cannot hide behind a number field
/// that still resolves. Nothing depends on operating-system locale data, for the reason the harness
/// README gives.
/// </remarks>
public sealed class CultureDataTests
{
    [Fact]
    public void EveryField_IsStillReachable()
    {
        const string Because =
            "a runtime that renames the field costs 32 bytes and 48 ns on every grouped format, "
            + "and the fallback hides that from every assertion about values";

        CultureData.ReadsTheNumberFieldDirectly.Should().BeTrue(Because);
        CultureData.ReadsTheCurrencyFieldDirectly.Should().BeTrue(Because);
        CultureData.ReadsThePercentFieldDirectly.Should().BeTrue(Because);
    }

    [Fact]
    public void TheAccessors_AgreeWithTheProperties()
    {
        foreach (var info in Infos())
        {
            // The accessors first, deliberately: the fields must be populated before anything has
            // gone through the properties, or a lazily initialised field would pass here and fail
            // for a caller who formats before reading the culture.
            var numberField = CultureData.NumberGroupSizes(info).ToArray();
            var currencyField = CultureData.CurrencyGroupSizes(info).ToArray();
            var percentField = CultureData.PercentGroupSizes(info).ToArray();

            numberField.Should().Equal(
                info.NumberGroupSizes,
                "the accessor reads the field the property clones, for [{0}]",
                string.Join(",", info.NumberGroupSizes));
            currencyField.Should().Equal(
                info.CurrencyGroupSizes,
                "the currency sizes are read the same way, for [{0}]",
                string.Join(",", info.CurrencyGroupSizes));
            percentField.Should().Equal(
                info.PercentGroupSizes,
                "the percent sizes are read the same way, for [{0}]",
                string.Join(",", info.PercentGroupSizes));
        }
    }

    [Fact]
    public void EachAccessor_ReadsItsOwnProperty()
    {
        var info = new NumberFormatInfo
        {
            NumberGroupSizes = [3],
            CurrencyGroupSizes = [3, 2],
            PercentGroupSizes = [4],
        };

        CultureData.NumberGroupSizes(info).ToArray().Should().Equal([3]);
        CultureData.CurrencyGroupSizes(info).ToArray().Should().Equal([3, 2]);
        CultureData.PercentGroupSizes(info).ToArray().Should().Equal(
            [4],
            "three properties that clone are three fields, and reading one for another is a defect a "
            + "uniform culture cannot show");
    }

    [Fact]
    public void TheAccessors_ReadTheCurrentValueRatherThanACachedOne()
    {
        var info = new NumberFormatInfo { NumberGroupSizes = [3], CurrencyGroupSizes = [3], PercentGroupSizes = [3] };

        CultureData.NumberGroupSizes(info).ToArray().Should().Equal([3]);
        CultureData.CurrencyGroupSizes(info).ToArray().Should().Equal([3]);
        CultureData.PercentGroupSizes(info).ToArray().Should().Equal([3]);

        info.NumberGroupSizes = [4, 2];
        info.CurrencyGroupSizes = [5];
        info.PercentGroupSizes = [6];

        const string Because =
            "a NumberFormatInfo that is not read-only can change between two calls, so nothing may cache it";

        CultureData.NumberGroupSizes(info).ToArray().Should().Equal([4, 2], Because);
        CultureData.CurrencyGroupSizes(info).ToArray().Should().Equal([5], Because);
        CultureData.PercentGroupSizes(info).ToArray().Should().Equal([6], Because);
    }

    [Fact]
    public void TheAccessors_AllocateNothing()
    {
        var readOnly = NumberFormatInfo.InvariantInfo;
        var mutable = new NumberFormatInfo { NumberGroupSizes = [3, 2], CurrencyGroupSizes = [3, 2], PercentGroupSizes = [3, 2] };

        const string BecauseReadOnly =
            "the property clones 32 bytes on every read and the accessor exists to avoid it";
        const string BecauseMutable =
            "the guarantee is not conditional on the format info being read-only";

        Allocations.Measure(() => Allocations.OtherSink = CultureData.NumberGroupSizes(readOnly).Length)
            .Should().Be(0, BecauseReadOnly);
        Allocations.Measure(() => Allocations.OtherSink = CultureData.CurrencyGroupSizes(readOnly).Length)
            .Should().Be(0, BecauseReadOnly);
        Allocations.Measure(() => Allocations.OtherSink = CultureData.PercentGroupSizes(readOnly).Length)
            .Should().Be(0, BecauseReadOnly);

        Allocations.Measure(() => Allocations.OtherSink = CultureData.NumberGroupSizes(mutable).Length)
            .Should().Be(0, BecauseMutable);
        Allocations.Measure(() => Allocations.OtherSink = CultureData.CurrencyGroupSizes(mutable).Length)
            .Should().Be(0, BecauseMutable);
        Allocations.Measure(() => Allocations.OtherSink = CultureData.PercentGroupSizes(mutable).Length)
            .Should().Be(0, BecauseMutable);
    }

    private static NumberFormatInfo[] Infos() =>
    [
        NumberFormatInfo.InvariantInfo,
        NumberFormatInfo.CurrentInfo,
        new NumberFormatInfo { NumberGroupSizes = [3, 2], CurrencyGroupSizes = [2, 3], PercentGroupSizes = [3, 2, 4] },
        new NumberFormatInfo { NumberGroupSizes = [3, 0], CurrencyGroupSizes = [3, 0], PercentGroupSizes = [3, 0] },
        new NumberFormatInfo { NumberGroupSizes = [], CurrencyGroupSizes = [], PercentGroupSizes = [] },
        new NumberFormatInfo { NumberGroupSizes = [9], CurrencyGroupSizes = [9], PercentGroupSizes = [9] },
    ];
}
