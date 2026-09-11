using System;
using System.Globalization;
using System.Numerics;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Checks the narrowing to <see cref="decimal"/> against <see cref="decimal"/> itself, over values
/// that lie outside its domain.
/// </summary>
/// <remarks>
/// <para>
/// The parity suite draws inside <see cref="decimal"/>'s own domain, where nothing is rounded and
/// the conversion is a copy. A narrowing starts outside it, which is why this comparison is here
/// rather than there, and why no randomised test in the suite reached the rounding before this one.
/// </para>
/// <para>
/// The reference is <see cref="decimal.Parse(string, NumberStyles, IFormatProvider)"/> of the
/// value's own plain text rather than <c>BigIntegerOracle</c>. The rule such an oracle would have
/// to implement - give up the fewest digits that leave a representable value, round the exact
/// remainder half to even - is the rule under test, so an oracle and an implementation written from
/// one reading of it would agree with each other while both were wrong. The base class library was
/// written from someone else's reading, and its ties were measured rather than assumed: it rounds
/// half to even and decides a tie from the whole of the input, a non-zero digit far to the right of
/// the twenty-ninth included.
/// </para>
/// </remarks>
public sealed class ConversionFuzzTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly ValueClass[] Classes = Enum.GetValues<ValueClass>();

    [Theory]
    [FuzzData]
    public void NarrowingToDecimal_MatchesDecimalParse(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);
        var rounded = 0;

        for (var index = 0; index < cases; index++)
        {
            // Scale 0 to 40 is where a narrowed value is usually still representable. The
            // magnitude stays a class draw: a digit-count draw reaches no power of ten, no word
            // boundary and no extremum, which is where a reduction carries.
            var drawn = generator.Next(Classes[random.Next(Classes.Length)], random.Next(0, 41));

            if (AgreesWithDecimal(FuzzContext.Of(seed, index, drawn), drawn))
            {
                rounded++;
            }
        }

        // A case that overflows on both sides agrees without the rounding running at all, so a
        // batch of nothing but those would pass while reporting nothing. Batches measure 23 to 53
        // in a hundred; the floor is set well under that.
        rounded.Should().BeGreaterThan(cases / 10, "the draw has to reach the rounding");
    }

    [Theory]
    [FuzzData]
    public void NarrowingToDecimal_MatchesDecimalParseAcrossTheWholeDomain(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);
        var rounded = 0;

        for (var index = 0; index < cases; index++)
        {
            // The generator's own scale draw, reaching 255: mostly values no scale holds, and the
            // ones below half a unit in the last place, which come back as zero at scale 28.
            var drawn = generator.Next();

            if (AgreesWithDecimal(FuzzContext.Of(seed, index, drawn), drawn))
            {
                rounded++;
            }
        }

        rounded.Should().BeGreaterThan(cases / 10, "the draw has to reach the rounding");
    }

    /// <summary>Compares one case, and reports whether it was one the rounding actually ran for.</summary>
    /// <param name="context">Where the case came from, for the failure message.</param>
    /// <param name="drawn">The value to narrow.</param>
    /// <returns><see langword="true"/> when the value needed narrowing and was representable once narrowed.</returns>
    private static bool AgreesWithDecimal(in FuzzContext context, FuzzValue drawn)
    {
        var text = OracleValue.From(drawn).ToDecimalString();

        OracleValue required;
        try
        {
            required = DecimalParityOracle.From(decimal.Parse(text, NumberStyles.Float, Invariant));
        }
        catch (OverflowException)
        {
            // A refusal is an outcome, and the boundary is where the conversion is most fragile.
            var refused = () => (decimal)drawn.Value;

            refused.Should().Throw<OverflowException>("{0} decimal refuses {1}", context, text);

            return false;
        }

        decimal produced;
        try
        {
            produced = (decimal)drawn.Value;
        }
        catch (OverflowException error)
        {
            throw new InvalidOperationException(
                string.Create(
                    Invariant,
                    $"{context} narrowing to decimal threw where decimal itself gives {required}."),
                error);
        }

        DecimalParityOracle.From(produced).Should().Be(required, "{0} narrowed to decimal", context);

        // A value already inside decimal's domain is copied, not narrowed, and agreeing about a
        // copy says nothing about the rounding.
        return drawn.Scale > DecimalParityOracle.MaxScale
            || BigInteger.Abs(drawn.Unscaled) > DecimalParityOracle.MaxMagnitude;
    }
}
