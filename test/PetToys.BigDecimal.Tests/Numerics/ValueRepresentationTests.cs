using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

public sealed class ValueRepresentationTests
{
    private const uint ReservedMask = 0x7FFF_FF00u;

    private static BigDecimal Parse(string text) => BigDecimal.Parse(text, CultureInfo.InvariantCulture);

    private static string Text(BigDecimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static uint FlagsOf(BigDecimal value)
    {
        Span<BigDecimal> single = [value];
        return MemoryMarshal.Read<uint>(MemoryMarshal.AsBytes(single)[32..]);
    }

    [Fact]
    public void Default_IsZeroAtScaleZero()
    {
        var values = new BigDecimal[1];

        foreach (var value in new[] { default(BigDecimal), BigDecimal.Zero, values[0] })
        {
            value.IsZero.Should().BeTrue();
            value.Scale.Should().Be(0);
            value.Sign.Should().Be(0);
            Text(value).Should().Be("0");
        }
    }

    [Fact]
    public void Layout_IsUnmanagedAndFortyBytes()
    {
        RuntimeHelpers.IsReferenceOrContainsReferences<BigDecimal>().Should().BeFalse();
        Unsafe.SizeOf<BigDecimal>().Should().Be(40);
    }

    [Fact]
    public void Flags_CarrySignInBit31AndScaleInTheLowByte()
    {
        FlagsOf(BigDecimal.One).Should().Be(0u);
        FlagsOf(BigDecimal.NegativeOne).Should().Be(0x8000_0000u);
        FlagsOf(Parse("1.00")).Should().Be(2u);
        FlagsOf(Parse("-1.00")).Should().Be(0x8000_0002u);
    }

    [Fact]
    public void ReservedFlagBits_AreClearOnEveryConstructionPath()
    {
        Span<ulong> words = [3, 0, 0, 0];

        var produced = new[]
        {
            default(BigDecimal),
            BigDecimal.Zero,
            BigDecimal.One,
            BigDecimal.NegativeOne,
            BigDecimal.MaxValue,
            BigDecimal.MinValue,
            Parse("-123.4500"),
            Parse("0." + new string('9', 255)),
            Parse("1.5") + Parse("2.25"),
            Parse("1.5") * Parse("-2.25"),
            Parse("1") / Parse("7"),
            Parse("7.5") % Parse("2"),
            BigDecimal.Round(Parse("1.55"), 1),
            Parse("1.5").WithScale(30),
            BigDecimal.FromWords(words, true, 7),
            BigDecimal.FromScaled(new BigInteger(-12345), 3),
            (BigDecimal)decimal.MinValue,
            (BigDecimal)(-1.5),
            (BigDecimal)long.MinValue,
        };

        foreach (var value in produced)
        {
            (FlagsOf(value) & ReservedMask).Should().Be(0u, "reserved bits 8..30 are held for NaN and the infinities");
        }
    }

    [Fact]
    public void Zero_NeverCarriesASign()
    {
        Parse("-0.0").IsNegative.Should().BeFalse();
        Parse("-0.0").Sign.Should().Be(0);
        Parse("-0.0").Scale.Should().Be(1);
        Text(Parse("-0.0")).Should().Be("0.0");

        BigDecimal.Negate(BigDecimal.Zero).IsNegative.Should().BeFalse();
        (BigDecimal.NegativeOne * BigDecimal.Zero).IsNegative.Should().BeFalse();
        (Parse("1.5") - Parse("1.5")).IsNegative.Should().BeFalse();
        BigDecimal.Abs(BigDecimal.Zero).IsNegative.Should().BeFalse();
    }

    [Fact]
    public void Zero_FromWords_DropsTheRequestedSign()
    {
        Span<ulong> words = [0, 0, 0, 0];

        var value = BigDecimal.FromWords(words, isNegative: true, scale: 2);

        value.IsNegative.Should().BeFalse();
        value.Should().Be(BigDecimal.Zero);
        value.Scale.Should().Be(2);
    }

    [Fact]
    public void TrailingZeros_SurviveAndStayObservable()
    {
        var one = Parse("1.0");
        var other = Parse("1.00");

        one.Should().Be(other);
        one.CompareTo(other).Should().Be(0);
        one.GetHashCode().Should().Be(other.GetHashCode());
        one.Scale.Should().Be(1);
        other.Scale.Should().Be(2);
        Text(one).Should().Be("1.0");
        Text(other).Should().Be("1.00");
        other.GetMantissa().Should().Be(new BigInteger(100));
    }

    [Fact]
    public void Arithmetic_KeepsTheWiderScale()
    {
        Text(Parse("1.10") + Parse("2.90")).Should().Be("4.00");
        Text(Parse("1.10") + Parse("2.90")).Should().Be((1.10m + 2.90m).ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FromScaled_KeepsMantissaAndScale()
    {
        var value = BigDecimal.FromScaled(new BigInteger(100), 2);

        value.Scale.Should().Be(2);
        value.GetMantissa().Should().Be(new BigInteger(100));
        Text(value).Should().Be("1.00");
    }

    [Fact]
    public void Extremes_AreTwoToThe256MinusOne()
    {
        var expected = (BigInteger.Pow(2, 256) - BigInteger.One).ToString(CultureInfo.InvariantCulture);

        Text(BigDecimal.MaxValue).Should().Be(expected);
        Text(BigDecimal.MinValue).Should().Be("-" + expected);
        expected.Length.Should().Be(78);

        Parse(Text(BigDecimal.MaxValue)).Should().Be(BigDecimal.MaxValue);
        Parse(Text(BigDecimal.MinValue)).Should().Be(BigDecimal.MinValue);
    }

    [Fact]
    public void SeventySevenDigits_AlwaysFit_SeventyEightDoNot()
    {
        var act = () => Parse(new string('9', 78));

        Text(Parse("1" + new string('0', 77))).Should().HaveLength(78);
        Text(Parse(new string('9', 77))).Should().HaveLength(77);
        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Words_RoundTrip()
    {
        var original = Parse("-123456789012345678901234567890.12345");

        Span<ulong> words = stackalloc ulong[4];
        var count = original.GetWords(words, out var isNegative, out var scale);

        count.Should().Be(2);
        isNegative.Should().BeTrue();
        scale.Should().Be(5);
        BigDecimal.FromWords(words, isNegative, scale).Should().Be(original);
        BigDecimal.FromWords(words, isNegative, scale).Scale.Should().Be(original.Scale);
    }

    [Fact]
    public void GetWords_WritesEveryWordAndReportsTheSignificantOnes()
    {
        Span<ulong> words = [ulong.MaxValue, ulong.MaxValue, ulong.MaxValue, ulong.MaxValue];

        var count = BigDecimal.One.GetWords(words, out var isNegative, out var scale);

        count.Should().Be(1);
        isNegative.Should().BeFalse();
        scale.Should().Be(0);
        words[0].Should().Be(1UL);
        words[1].Should().Be(0UL);
        words[2].Should().Be(0UL);
        words[3].Should().Be(0UL);
    }

    [Fact]
    public void GetWords_ReportsZeroWordsForZero()
    {
        Span<ulong> words = stackalloc ulong[4];

        BigDecimal.Zero.GetWords(words, out _, out _).Should().Be(0);
    }

    [Fact]
    public void GetWords_RejectsAShortDestination()
    {
        var act = () =>
        {
            Span<ulong> words = stackalloc ulong[3];
            BigDecimal.Zero.GetWords(words, out _, out _);
        };

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("destination.Length");
    }

    [Fact]
    public void FromWords_AcceptsAnOverLongSourceThatIsZeroAboveTheMagnitude()
    {
        Span<ulong> words = [1, 2, 3, 4, 0, 0];

        var value = BigDecimal.FromWords(words, false, 0);

        Span<ulong> back = stackalloc ulong[4];
        value.GetWords(back, out _, out _).Should().Be(4);
        back[3].Should().Be(4UL);
    }

    [Fact]
    public void FromWords_RejectsAMagnitudeWiderThanFourWords()
    {
        var act = () =>
        {
            Span<ulong> words = [1, 2, 3, 4, 5];
            BigDecimal.FromWords(words, false, 0);
        };

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void FromWords_AcceptsAnEmptySource()
    {
        var value = BigDecimal.FromWords([], false, 4);

        value.IsZero.Should().BeTrue();
        value.Scale.Should().Be(4);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void FromWords_RejectsAScaleOutsideTheDomain(int scale)
    {
        var act = () =>
        {
            Span<ulong> words = [1];
            BigDecimal.FromWords(words, false, scale);
        };

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(scale));
    }
}
