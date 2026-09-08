using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

[Collection(AmbientCulture.Name)]
public sealed class ParsingTests
{
    private static string Text(BigDecimal value) => value.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public void TheOverloadsWithoutAProvider_AllUseTheCurrentCulture()
    {
        using (CultureScope.For(CultureCase.CommaDecimal))
        {
            var expected = BigDecimal.Parse("1.5", CultureInfo.InvariantCulture);

            BigDecimal.TryParse("1,5", out var fromString).Should().BeTrue();
            BigDecimal.TryParse("1,5".AsSpan(), out var fromChars).Should().BeTrue();
            BigDecimal.TryParse(Encoding.UTF8.GetBytes("1,5"), out var fromUtf8).Should().BeTrue();

            fromString.Should().Be(expected);
            fromChars.Should().Be(expected);
            fromUtf8.Should().Be(expected, "the UTF-8 overload must not silently read a comma as a group separator");
        }
    }

    [Fact]
    public void ALongValue_ParsesTheSameFromCharsAndFromUtf8()
    {
        var text = "0." + new string('1', 500);

        var fromChars = BigDecimal.Parse(text, CultureInfo.InvariantCulture);
        var fromUtf8 = BigDecimal.Parse(Encoding.UTF8.GetBytes(text), CultureInfo.InvariantCulture);

        fromUtf8.Should().Be(fromChars);
        fromUtf8.Scale.Should().Be(fromChars.Scale);
    }

    [Fact]
    public void DigitsBeyondTheCaptureWindow_StillDecideATie()
    {
        // Seventy-seven significant digits ending in an even one, then an exact half, then a
        // run of zeros long enough to leave the parser's capture window, then a stray nine.
        var digits = new string('1', 76) + "2" + "5" + new string('0', 19) + "9";

        var value = BigDecimal.Parse("0." + digits, CultureInfo.InvariantCulture);

        value.Scale.Should().Be(77);
        value.GetMantissa().Should().Be(
            BigInteger.Parse(new string('1', 76) + "3", CultureInfo.InvariantCulture),
            "a non-zero digit past the window makes the remainder greater than half");
    }

    [Fact]
    public void AGenuineTie_StillRoundsToEven()
    {
        var digits = new string('1', 76) + "2" + "5" + new string('0', 20);

        var value = BigDecimal.Parse("0." + digits, CultureInfo.InvariantCulture);

        value.GetMantissa().Should().Be(
            BigInteger.Parse(new string('1', 76) + "2", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ALongFraction_RoundsAsIfEveryDigitHadBeenSeen()
    {
        var random = new Random(20260825);
        for (var i = 0; i < 200; i++)
        {
            var length = random.Next(78, 300);
            var builder = new StringBuilder(length);
            builder.Append((char)('1' + random.Next(9)));
            for (var j = 1; j < length; j++)
            {
                builder.Append(random.Next(3) == 0 ? (char)('0' + random.Next(10)) : '0');
            }

            var digits = builder.ToString();
            var value = BigDecimal.Parse("0." + digits, CultureInfo.InvariantCulture);

            value.GetMantissa().Should().Be(
                RoundHalfToEven(BigInteger.Parse(digits, CultureInfo.InvariantCulture), length - value.Scale),
                "input {0} rounded to scale {1}",
                digits,
                value.Scale);
        }
    }

    [Theory]
    [InlineData("en-US", "1,234")]
    [InlineData("en-US", "1,234.56")]
    [InlineData("en-US", "1,23")]
    [InlineData("en-US", "12,34,567")]
    [InlineData("en-US", "1,2,3")]
    [InlineData("en-US", "1,,234")]
    [InlineData("en-US", ",234")]
    [InlineData("en-US", "1,234,")]
    [InlineData("en-US", "1234,")]
    [InlineData("en-US", ",")]
    [InlineData("en-US", "1,234.5,6")]
    [InlineData("en-US", "-1,234")]
    [InlineData("en-US", "1,234e2")]
    [InlineData("en-US", ",,234")]
    [InlineData("en-US", "-,234")]
    [InlineData("en-US", "1.5,6")]
    [InlineData("en-US", ".5,6")]
    [InlineData("de-DE", "1.234,56")]
    [InlineData("de-DE", "1.23")]
    [InlineData("de-DE", "1..234")]
    [InlineData("de-DE", "1.2.3")]
    public void GroupSeparators_AreAcceptedExactlyWhereDecimalAcceptsThem(string culture, string text)
    {
        var info = CultureInfo.GetCultureInfo(culture);

        var expected = decimal.TryParse(text, NumberStyles.Number, info, out var reference);
        var actual = BigDecimal.TryParse(text, NumberStyles.Number, info, out var value);

        actual.Should().Be(expected, "'{0}' under {1}", text, culture);
        if (expected)
        {
            Text(value).Should().Be(reference.ToString(CultureInfo.InvariantCulture));
        }
    }

    public static TheoryData<int, bool> WhiteSpaceCandidates()
    {
        // Derived from char.IsWhiteSpace rather than written out, so that a framework release
        // adding a codepoint to it is covered without editing this file. That predicate is what
        // the parser used to trim with, and it accepts nineteen characters decimal refuses.
        var data = new TheoryData<int, bool>();
        for (var codepoint = 0; codepoint <= char.MaxValue; codepoint++)
        {
            if (char.IsWhiteSpace((char)codepoint))
            {
                data.Add(codepoint, true);
                data.Add(codepoint, false);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(WhiteSpaceCandidates))]
    public void WhiteSpace_IsConsumedExactlyWhereDecimalConsumesIt(int codepoint, bool leading)
    {
        var pad = ((char)codepoint).ToString();
        var text = leading ? pad + "123" : "123" + pad;

        var expected = decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var reference);

        BigDecimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var fromChars)
            .Should().Be(expected, "U+{0:X4} {1} a value", codepoint, leading ? "before" : "after");
        BigDecimal.TryParse(Encoding.UTF8.GetBytes(text), NumberStyles.Number, CultureInfo.InvariantCulture, out var fromUtf8)
            .Should().Be(expected, "the UTF-8 overload transcodes into the same prologue");

        if (expected)
        {
            var rendered = reference.ToString(CultureInfo.InvariantCulture);
            Text(fromChars).Should().Be(rendered);
            Text(fromUtf8).Should().Be(rendered);
        }
    }

    [Fact]
    public void TheConsumedWhiteSpace_IsTheSixCharactersDecimalConsumes()
    {
        // A run rather than a single character, so that a trim which stops after one fails.
        char[] accepted = ['\u0009', '\u000A', '\u000B', '\u000C', '\u000D', '\u0020'];

        foreach (var character in accepted)
        {
            var run = new string(character, 3);
            var text = run + "1.5" + run;

            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                .Should().BeTrue("U+{0:X4} is white space to decimal", (int)character);
            BigDecimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                .Should().BeTrue("U+{0:X4} is white space to decimal", (int)character);
            Text(value).Should().Be("1.5");
        }
    }

    [Fact]
    public void TheTrim_StopsAtTheFirstCharacterOutsideTheSet()
    {
        // A trim that consumed the rest of the run once it had seen one accepted character
        // would pass every single-character case above and still swallow the separator D15
        // was about. Both orders, because a leading accepted character and a leading refused
        // one take different branches.
        string[] mixed = ["\t\u00A0123", "\u00A0\t123", "123\t\u00A0", "123\u00A0\t"];

        foreach (var text in mixed)
        {
            decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                .Should().BeFalse("decimal refuses U+00A0 wherever it stands");
            BigDecimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _)
                .Should().BeFalse("the trim must stop rather than run past it");
        }
    }

    [Fact]
    public void ClearingTheWhiteSpaceStyles_RefusesWhatTheyWouldHaveConsumed()
    {
        const NumberStyles noLeading = NumberStyles.Number & ~NumberStyles.AllowLeadingWhite;
        const NumberStyles noTrailing = NumberStyles.Number & ~NumberStyles.AllowTrailingWhite;

        decimal.TryParse(" 1.5", noLeading, CultureInfo.InvariantCulture, out _).Should().BeFalse();
        BigDecimal.TryParse(" 1.5", noLeading, CultureInfo.InvariantCulture, out _).Should().BeFalse();

        decimal.TryParse("1.5 ", noTrailing, CultureInfo.InvariantCulture, out _).Should().BeFalse();
        BigDecimal.TryParse("1.5 ", noTrailing, CultureInfo.InvariantCulture, out _).Should().BeFalse();
    }

    [Fact]
    public void ALeadingGroupSeparator_IsRefusedEvenWhenItIsWhitespace()
    {
        // Found by the culture matrix, and the reason the white-space set had to be narrowed:
        // where the separator is a non-breaking space it was consumed as leading white space
        // and the value came back as 234.
        var culture = CultureMatrix.Get(CultureCase.SpaceGroups);
        var text = culture.NumberFormat.NumberGroupSeparator + "234";

        decimal.TryParse(text, NumberStyles.Number, culture, out _).Should().BeFalse();
        BigDecimal.TryParse(text, NumberStyles.Number, culture, out _).Should().BeFalse();
    }

    [Fact]
    public void WithoutAllowThousands_TheGroupSeparatorIsRefused()
    {
        decimal.TryParse("1,234", NumberStyles.Float, CultureInfo.InvariantCulture, out _).Should().BeFalse();
        BigDecimal.TryParse("1,234", NumberStyles.Float, CultureInfo.InvariantCulture, out _).Should().BeFalse();
    }

    [Fact]
    public void ALongUtf8Payload_ParsesWithoutAllocating()
    {
        var utf8 = Encoding.UTF8.GetBytes("0." + new string('1', 500));

        Allocations.Measure(() => Allocations.Sink = BigDecimal.Parse(utf8, CultureInfo.InvariantCulture), 64)
            .Should().Be(0);
        Allocations.Measure(
                () => Allocations.OtherSink = BigDecimal.TryParse(utf8, CultureInfo.InvariantCulture, out var value) ? value.Scale : -1,
                64)
            .Should().Be(0);
    }

    [Fact]
    public void Exponents_ShiftTheScale()
    {
        Text(BigDecimal.Parse("1.5e3", CultureInfo.InvariantCulture)).Should().Be("1500");
        Text(BigDecimal.Parse("1.5e-3", CultureInfo.InvariantCulture)).Should().Be("0.0015");
    }

    private static BigInteger RoundHalfToEven(BigInteger value, int drop)
    {
        if (drop <= 0)
        {
            return value;
        }

        var divisor = BigInteger.Pow(10, drop);
        var quotient = BigInteger.DivRem(value, divisor, out var remainder);
        var twice = remainder * 2;
        return twice > divisor || (twice == divisor && !quotient.IsEven)
            ? quotient + BigInteger.One
            : quotient;
    }
}
