using System;
using System.Globalization;
using System.Text;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Runs parsing and formatting under a matrix of cultures whose separators and group sizes are set
/// by the harness rather than read from the operating system.
/// </summary>
[Collection(AmbientCulture.Name)]
public sealed class CultureFuzzTests
{
    [Theory]
    [FuzzData]
    public void FormattingAndParsing_RoundTripUnderEveryCulture(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var culture = CultureMatrix.Get(CultureMatrix.All[random.Next(CultureMatrix.All.Length)]);
            var context = FuzzContext.Of(seed, index, drawn);
            var text = drawn.Value.ToString(culture);

            OracleValue.Observe(BigDecimal.Parse(text, culture))
                .Should().Be(OracleValue.From(drawn), "{0} round trips through \"{1}\"", context, text);

            OracleValue.Observe(BigDecimal.Parse(Encoding.UTF8.GetBytes(text), culture))
                .Should().Be(OracleValue.From(drawn), "{0} round trips through UTF-8 \"{1}\"", context, text);
        }
    }

    [Theory]
    [FuzzData]
    public void EveryFormatString_AgreesWithDecimalUnderEveryCulture(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);
        Span<byte> utf8 = stackalloc byte[512];

        for (var index = 0; index < cases; index++)
        {
            var drawn = generator.Next();
            var described = OracleValue.From(drawn);
            if (!DecimalParityOracle.TryToDecimal(described, out var reference))
            {
                continue;
            }

            var cultureIndex = random.Next(CultureMatrix.Every.Length);
            var culture = CultureMatrix.Every[cultureIndex];
            var format = FormatCorpus.All[random.Next(FormatCorpus.All.Length)];

            var context = FuzzContext.Of(seed, index, drawn);
            var expected = reference.ToString(format, culture);

            drawn.Value.ToString(format, culture).Should().Be(
                expected, "{0} formatted with \"{1}\" under culture {2}", context, format, cultureIndex);

            // The two overloads are one contract. Written here rather than in its own test so that
            // every format string the corpus carries is checked on both, not only the ones somebody
            // thought to list twice.
            drawn.Value.TryFormat(utf8, out var written, format, culture).Should().BeTrue(
                "{0} fits 512 bytes with \"{1}\"", context, format);
            Encoding.UTF8.GetString(utf8[..written]).Should().Be(
                expected, "{0} writes the same text to UTF-8 with \"{1}\"", context, format);
        }
    }

    [Theory]
    [MemberData(nameof(RejectedFormats))]
    public void ARejectedFormatString_IsRejectedByBoth(string format)
    {
        // A single letter is a standard specifier or it is an error, and the same letters twice
        // over are a custom format of literals. The distinction is not recognised versus
        // unrecognised, which is what a reading of the issue would have implemented.
        var value = BigDecimal.Parse("-1234.5678", CultureInfo.InvariantCulture);

        var decimalThrew = false;
        try
        {
            _ = (-1234.5678m).ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            decimalThrew = true;
        }

        decimalThrew.Should().BeTrue("the corpus lists what decimal rejects");
        value.Invoking(v => v.ToString(format, CultureInfo.InvariantCulture)).Should().Throw<FormatException>();

        var doubled = format + format;
        value.ToString(doubled, CultureInfo.InvariantCulture).Should().Be(
            (-1234.5678m).ToString(doubled, CultureInfo.InvariantCulture),
            "twice the same string is not a standard specifier at all, so it is a custom format of literals");
    }

    public static TheoryData<string> RejectedFormats()
    {
        var data = new TheoryData<string>();
        foreach (var format in FormatCorpus.Rejected)
        {
            data.Add(format);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(GroupSeparatorCases))]
    public void GroupSeparatorLeniency_MatchesDecimal(CultureCase cultureCase, string shape)
    {
        var culture = CultureMatrix.Get(cultureCase);

        // One pass, not two Replace calls: under a culture that swaps the two characters, a second
        // Replace would rewrite the separators the first one just produced and the shape would
        // collapse into a different one.
        var text = Localise(shape, culture);

        // A leading separator that is whitespace is accepted where decimal refuses it; that is
        // pinned in PendingDefectTests rather than asserted here.
        if (shape.StartsWith(',') && char.IsWhiteSpace(culture.NumberFormat.NumberGroupSeparator[0]))
        {
            return;
        }

        var acceptedByDecimal = decimal.TryParse(text, NumberStyles.Number, culture, out var reference);
        var accepted = BigDecimal.TryParse(text, NumberStyles.Number, culture, out var value);

        accepted.Should().Be(acceptedByDecimal, "\"{0}\" under {1}", text, cultureCase);

        if (acceptedByDecimal)
        {
            value.Should().Be((BigDecimal)reference, "\"{0}\" under {1}", text, cultureCase);
        }
    }

    [Theory]
    [MemberData(nameof(CultureCases))]
    public void TheAmbientCulture_IsRestoredEvenWhenTheBodyThrows(CultureCase cultureCase)
    {
        var before = CultureInfo.CurrentCulture;

        var act = () =>
        {
            using (CultureScope.For(cultureCase))
            {
                throw new InvalidOperationException("deliberate");
            }
        };

        act.Should().Throw<InvalidOperationException>();
        CultureInfo.CurrentCulture.Should().BeSameAs(before);
    }

    [Fact]
    public void TheRealGermanCulture_BehavesLikeTheSynthesizedOne()
    {
        // The matrix is synthesized so that it cannot drift with ICU. This one test keeps a real
        // locale in play, so the synthesized cultures are not the only thing ever exercised.
        var german = CultureInfo.GetCultureInfo("de-DE");
        var value = BigDecimal.Parse("1234567.89", CultureInfo.InvariantCulture);

        value.ToString("N2", german).Should().Be(((decimal)value).ToString("N2", german));
        BigDecimal.Parse("1.234.567,89", german).Should().Be(value);
    }

    [Fact]
    public void TheAmbientCulture_DecidesForTheOverloadsWithoutAProvider()
    {
        using (CultureScope.For(CultureCase.CommaDecimal))
        {
#pragma warning disable CA1305 // Reaching for the ambient culture is the point of this test.
            BigDecimal.Parse("1,5").Should().Be(BigDecimal.Parse("1.5", CultureInfo.InvariantCulture));
            BigDecimal.Parse("1,5".AsSpan()).Should().Be(BigDecimal.Parse("1.5", CultureInfo.InvariantCulture));
            BigDecimal.Parse(Encoding.UTF8.GetBytes("1,5")).Should().Be(BigDecimal.Parse("1.5", CultureInfo.InvariantCulture));
            BigDecimal.Parse("1.5", CultureInfo.InvariantCulture).ToString().Should().Be("1,5");
#pragma warning restore CA1305
        }
    }

    private static string Localise(string shape, CultureInfo culture)
    {
        var group = culture.NumberFormat.NumberGroupSeparator;
        var point = culture.NumberFormat.NumberDecimalSeparator;
        var builder = new StringBuilder(shape.Length + 8);

        foreach (var character in shape)
        {
            _ = character switch
            {
                ',' => builder.Append(group),
                '.' => builder.Append(point),
                _ => builder.Append(character),
            };
        }

        return builder.ToString();
    }

    public static TheoryData<CultureCase, string> GroupSeparatorCases()
    {
        // decimal is extremely lenient about grouping, and parity means matching that leniency
        // rather than tightening it. The last shape is the one decimal rejects.
        string[] shapes =
        [
            "1,234", "1,,234", "1,23", "12,34,567", "1,2,3", "1,234,", "1,234.5", ",234",
        ];

        var data = new TheoryData<CultureCase, string>();
        foreach (var cultureCase in CultureMatrix.All)
        {
            foreach (var shape in shapes)
            {
                data.Add(cultureCase, shape);
            }
        }

        return data;
    }

    public static TheoryData<CultureCase> CultureCases()
    {
        var data = new TheoryData<CultureCase>();
        foreach (var cultureCase in CultureMatrix.All)
        {
            data.Add(cultureCase);
        }

        return data;
    }
}
