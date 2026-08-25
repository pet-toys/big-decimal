using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

public sealed class ParsingTests
{
    private static string Text(BigDecimal value) => value.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public void TheOverloadsWithoutAProvider_AllUseTheCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var expected = BigDecimal.Parse("1.5", CultureInfo.InvariantCulture);

            BigDecimal.TryParse("1,5", out var fromString).Should().BeTrue();
            BigDecimal.TryParse("1,5".AsSpan(), out var fromChars).Should().BeTrue();
            BigDecimal.TryParse(Encoding.UTF8.GetBytes("1,5"), out var fromUtf8).Should().BeTrue();

            fromString.Should().Be(expected);
            fromChars.Should().Be(expected);
            fromUtf8.Should().Be(expected, "the UTF-8 overload must not silently read a comma as a group separator");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
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
