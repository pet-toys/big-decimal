using System;
using System.Buffers.Binary;
using System.Numerics;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Compares division by a single word against <see cref="WordsDivisionOracle"/>, the form the
/// package used before the primitive was replaced.
/// </summary>
/// <remarks>
/// <para>
/// A defect inside this primitive is invisible from outside until the operands are wide enough to
/// reach it: the trial quotient of the multi-word loop was wrong while every example test passed,
/// because the shape that triggers it is a quotient word of all ones. So the primitive is compared
/// directly against the form it replaces, over the generator's own widths and boundaries, and not
/// only through the public operations that call it.
/// </para>
/// <para>
/// The two sides are the same algorithm until the primitive changes, so these tests pass trivially
/// today. That is the point of writing them first: a test that has never run green against a known
/// good implementation is not a safety net.
/// </para>
/// </remarks>
public sealed class WordsDivisionTests
{
    // 5^27, the largest power of five a word holds. The package divides by it on every
    // trailing-zero test; the constant is private there, so it is repeated rather than reached.
    private const ulong FivesPerWord = 7_450_580_596_923_828_125UL;

    // The narrowest operands whose trial quotient saturates: three numerator words over two
    // divisor words, with an exact quotient word of 2^64 - 1. DivisionTests pins the same pair
    // through the public surface; this pins the primitive itself, which is what the rewrite moves.
    private const string SaturatingDividend = "3011454863741893821962847635079523438298849281864699066895";

    private const string SaturatingDivisor = "163251295280550000920325954839477876654";

    [Theory]
    [FuzzData]
    public void DivRem2By1_MatchesTheOracle(int seed, int cases)
    {
        var random = new Random(seed);

        for (var index = 0; index < cases; index++)
        {
            var divisor = Normalized(random);
            var reciprocal = Words.Reciprocal(divisor);
            var low = NextWord(random);

            // The high half is the running remainder in every caller, so it is always below the
            // divisor. A quarter of the cases sit on the boundary itself, where the quotient word
            // is all ones and an estimate-and-correct implementation has the least room.
            var high = random.Next(4) == 0 ? divisor - 1 : NextWord(random) % divisor;

            var quotient = Words.DivRem2By1(high, low, divisor, reciprocal, out var remainder);
            var required = WordsDivisionOracle.DivRem2By1(high, low, divisor, out var requiredRemainder);

            quotient.Should().Be(required, "[seed {0} case {1}] {2}:{3} over {4}: quotient", seed, index, high, low, divisor);
            remainder.Should().Be(
                requiredRemainder,
                "[seed {0} case {1}] {2}:{3} over {4}: remainder",
                seed,
                index,
                high,
                low,
                divisor);
        }
    }

    // A high half of divisor - 1 over a low half of all ones gives a quotient word of 2^64 - 1:
    // the dividend is MaxValue x divisor + divisor - 1, which is what the multi-word trial
    // quotient saturates on, reached directly rather than through operands wide enough for it.
    [Theory]
    [InlineData(9_223_372_036_854_775_807UL, ulong.MaxValue, 9_223_372_036_854_775_808UL)]
    [InlineData(ulong.MaxValue - 1, ulong.MaxValue, ulong.MaxValue)]
    [InlineData(9_999_999_999_999_999_999UL, ulong.MaxValue, 10_000_000_000_000_000_000UL)]
    [InlineData(9_223_372_036_854_775_808UL, 0UL, 18_446_744_069_414_584_320UL)]
    [InlineData(0UL, ulong.MaxValue, 9_223_372_036_854_775_808UL)]
    [InlineData(0UL, 0UL, ulong.MaxValue)]
    [InlineData(1UL, 0UL, 9_223_372_036_854_775_809UL)]
    [InlineData(ulong.MaxValue - 1, 0UL, ulong.MaxValue)]

    // Both need the second correction, the one that moves the quotient up. No boundary case above
    // reaches it: removing that branch leaves every case here green and fails only the randomised
    // theory, at one case in a thousand. Found there, kept here so the shape survives a filter.
    [InlineData(9_240_188_774_902_505_938UL, 16_658_877_116_815_098_501UL, 9_638_917_963_980_254_000UL)]
    [InlineData(9_461_628_071_965_608_955UL, 17_993_631_896_169_171_291UL, 10_695_376_612_292_763_194UL)]
    public void DivRem2By1_MatchesTheOracleAtTheContractBoundary(ulong high, ulong low, ulong divisor)
    {
        var quotient = Words.DivRem2By1(high, low, divisor, Words.Reciprocal(divisor), out var remainder);
        var required = WordsDivisionOracle.DivRem2By1(high, low, divisor, out var requiredRemainder);

        quotient.Should().Be(required, "the quotient");
        remainder.Should().Be(requiredRemainder, "the remainder");
    }

    [Theory]
    [FuzzData]
    public void Reciprocal_MatchesItsDefinition(int seed, int cases)
    {
        var random = new Random(seed);

        for (var index = 0; index < cases; index++)
        {
            var divisor = Normalized(random);

            ((BigInteger)Words.Reciprocal(divisor))
                .Should().Be(
                    ReciprocalOf(divisor),
                    "[seed {0} case {1}] the reciprocal of {2}",
                    seed,
                    index,
                    divisor);
        }
    }

    // A table is data, and data drifts from what it describes. Every entry is checked against the
    // divisor it belongs to rather than against the way it was built, so an edited constant, a
    // reordered table or a shortened one fails here instead of returning wrong quotients.
    [Fact]
    public void Pow10Divisors_AgreeWithThePowersOfTen()
    {
        Words.Pow10Divisors.Length.Should().Be(Words.Pow10.Length, "one prepared divisor per power of ten");

        for (var index = 0; index < Words.Pow10.Length; index++)
        {
            var value = Words.Pow10[index];
            var prepared = Words.Pow10Divisors[index];

            prepared.Shift.Should().Be(BitOperations.LeadingZeroCount(value), "the shift of 10^{0}", index);
            if (index == Words.MaxZerosPerPass)
            {
                Words.TenPow19Divisor.Should().Be(prepared, "the divisor the digit peeler reaches by name");
            }

            (prepared.Normalized >> prepared.Shift).Should().Be(value, "10^{0} undone by its own shift", index);
            ((BigInteger)prepared.Reciprocal)
                .Should().Be(ReciprocalOf(prepared.Normalized), "the reciprocal of 10^{0}", index);
        }
    }

    [Fact]
    public void Pow5Divisors_AgreeWithThePowersOfFive()
    {
        // Twenty-eight entries, 5^0 to 5^27, the largest power of five a word holds.
        Words.Pow5Divisors.Length.Should().Be(28, "one prepared divisor per power of five a word holds");

        for (var index = 0; index < Words.Pow5Divisors.Length; index++)
        {
            var value = (ulong)BigInteger.Pow(5, index);
            var prepared = Words.Pow5Divisors[index];

            prepared.Shift.Should().Be(BitOperations.LeadingZeroCount(value), "the shift of 5^{0}", index);
            (prepared.Normalized >> prepared.Shift).Should().Be(value, "5^{0} undone by its own shift", index);
            ((BigInteger)prepared.Reciprocal)
                .Should().Be(ReciprocalOf(prepared.Normalized), "the reciprocal of 5^{0}", index);
        }
    }

    [Theory]
    [FuzzData]
    public void DivRemSmall_MatchesTheOracle(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);
        var measured = new ulong[BigDecimal.WordCount];
        var required = new ulong[BigDecimal.WordCount];

        for (var index = 0; index < cases; index++)
        {
            var value = generator.Next();
            var context = FuzzContext.Of(seed, index, value);
            var divisor = NextDivisor(random);
            var length = ToWords(BigInteger.Abs(value.Unscaled), measured);
            measured.CopyTo(required, 0);

            var measuredLength = Words.DivRemSmall(measured, length, Words.Divisor.For(divisor), out var measuredRemainder);
            var requiredLength = WordsDivisionOracle.DivRemSmall(required, length, divisor, out var requiredRemainder);

            measuredLength.Should().Be(requiredLength, "{0} over {1}: quotient length", context, divisor);
            measured.Should().Equal(required, "{0} over {1}: quotient words", context, divisor);
            measuredRemainder.Should().Be(requiredRemainder, "{0} over {1}: remainder", context, divisor);
        }
    }

    [Theory]
    [FuzzData]
    public void RemSmall_MatchesTheOracle(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);
        var words = new ulong[BigDecimal.WordCount];

        for (var index = 0; index < cases; index++)
        {
            var value = generator.Next();
            var context = FuzzContext.Of(seed, index, value);
            var divisor = NextDivisor(random);
            var length = ToWords(BigInteger.Abs(value.Unscaled), words);

            Words.RemSmall(words, length, Words.Divisor.For(divisor))
                .Should().Be(
                    WordsDivisionOracle.RemSmall(words, length, divisor),
                    "{0} over {1}: remainder",
                    context,
                    divisor);
        }
    }

    [Theory]
    [FuzzData]
    public void DivRem_WithASingleWordDivisor_MatchesTheOracle(int seed, int cases)
    {
        var random = new Random(seed);
        var generator = new ValueGenerator(random);
        var numerator = new ulong[BigDecimal.WordCount];
        var source = new ulong[BigDecimal.WordCount];
        var measured = new ulong[BigDecimal.WordCount];
        var required = new ulong[BigDecimal.WordCount];

        for (var index = 0; index < cases; index++)
        {
            var value = generator.Next();
            var context = FuzzContext.Of(seed, index, value);
            var divisor = NextDivisor(random);
            var length = ToWords(BigInteger.Abs(value.Unscaled), source);
            source.CopyTo(numerator, 0);

            // Neither side clears above the length it writes, and the real caller clears the
            // quotient before every division for exactly that reason. Comparing whole buffers
            // otherwise compares the previous case's quotient.
            measured.AsSpan().Clear();
            required.AsSpan().Clear();

            var measuredLength = Words.DivRem(
                numerator,
                length,
                [divisor],
                1,
                measured,
                out var measuredRemainderLength);
            var requiredLength = WordsDivisionOracle.DivRemIntoQuotient(
                source,
                length,
                divisor,
                required,
                out var requiredRemainder);

            measuredLength.Should().Be(requiredLength, "{0} over {1}: quotient length", context, divisor);
            measured.Should().Equal(required, "{0} over {1}: quotient words", context, divisor);
            measuredRemainderLength.Should().Be(
                requiredRemainder == 0 ? 0 : 1,
                "{0} over {1}: remainder length",
                context,
                divisor);
            numerator[0].Should().Be(requiredRemainder, "{0} over {1}: remainder", context, divisor);
        }
    }

    // The high half of every step after the first is the running remainder, always below the
    // divisor. The case that matters is the step where it is exactly one below, on the
    // precondition itself, which a leading word of divisor - 1 produces on the following word.
    [Theory]
    [InlineData(9UL, ulong.MaxValue, 10UL)]
    [InlineData(9_999_999_999_999_999_999UL, ulong.MaxValue, 10_000_000_000_000_000_000UL)]
    [InlineData(7_450_580_596_923_828_124UL, ulong.MaxValue, FivesPerWord)]
    [InlineData(9_223_372_036_854_775_807UL, ulong.MaxValue, 9_223_372_036_854_775_808UL)]
    [InlineData(ulong.MaxValue - 1, ulong.MaxValue, ulong.MaxValue)]
    [InlineData(2UL, ulong.MaxValue, 3UL)]
    [InlineData(0UL, ulong.MaxValue, 10UL)]
    [InlineData(ulong.MaxValue, ulong.MaxValue, ulong.MaxValue)]
    [InlineData(ulong.MaxValue, 0UL, 9_223_372_036_854_775_808UL)]
    [InlineData(ulong.MaxValue, ulong.MaxValue, 1UL)]
    public void DivRemSmall_MatchesTheOracleAtTheContractBoundary(ulong high, ulong low, ulong divisor)
    {
        ulong[] measured = [low, high];
        ulong[] required = [low, high];

        var measuredLength = Words.DivRemSmall(measured, 2, Words.Divisor.For(divisor), out var measuredRemainder);
        var requiredLength = WordsDivisionOracle.DivRemSmall(required, 2, divisor, out var requiredRemainder);

        measuredLength.Should().Be(requiredLength, "quotient length");
        measured.Should().Equal(required, "quotient words");
        measuredRemainder.Should().Be(requiredRemainder, "remainder");
    }

    [Fact]
    public void DivRem_MatchesABigIntegerReferenceWhereTheTrialQuotientSaturates()
    {
        var dividend = BigInteger.Parse(SaturatingDividend, System.Globalization.CultureInfo.InvariantCulture);
        var divisor = BigInteger.Parse(SaturatingDivisor, System.Globalization.CultureInfo.InvariantCulture);
        var expectedQuotient = BigInteger.DivRem(dividend, divisor, out var expectedRemainder);

        var numerator = new ulong[BigDecimal.WordCount];
        var denominator = new ulong[BigDecimal.WordCount];
        var quotient = new ulong[BigDecimal.WordCount];
        var numeratorLength = ToWords(dividend, numerator);
        var denominatorLength = ToWords(divisor, denominator);

        var quotientLength = Words.DivRem(
            numerator,
            numeratorLength,
            denominator,
            denominatorLength,
            quotient,
            out var remainderLength);

        ToInteger(quotient, quotientLength).Should().Be(expectedQuotient, "the quotient");
        ToInteger(numerator, remainderLength).Should().Be(expectedRemainder, "the remainder");
    }

    private static ulong NextDivisor(Random random) => random.Next(8) switch
    {
        0 => 10UL,
        1 => Words.TenPow19,
        2 => Words.Pow10[random.Next(Words.Pow10.Length)],
        3 => FivesPerWord,
        4 => 1UL,
        5 => ulong.MaxValue,
        6 => 1UL << random.Next(64),
        _ => NextWord(random) | 1UL,
    };

    private static BigInteger ReciprocalOf(ulong normalized) =>
        (((BigInteger.One << 128) - BigInteger.One) / normalized) - (BigInteger.One << 64);

    private static ulong Normalized(Random random) => random.Next(6) switch
    {
        0 => 1UL << 63,
        1 => ulong.MaxValue,
        2 => Words.TenPow19,
        3 => (1UL << 63) + 1UL,
        4 => 0xFFFF_FFFF_0000_0000UL,
        _ => NextWord(random) | (1UL << 63),
    };

    private static ulong NextWord(Random random)
    {
        Span<byte> buffer = stackalloc byte[8];
        random.NextBytes(buffer);

        return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
    }

    private static int ToWords(BigInteger magnitude, Span<ulong> destination)
    {
        destination.Clear();
        Span<byte> bytes = stackalloc byte[destination.Length * 8];
        bytes.Clear();

        // A magnitude that does not fit leaves the buffer as it found it, so both sides would then
        // agree on zero and the case would pass without testing anything.
        magnitude.TryWriteBytes(bytes, out _, isUnsigned: true, isBigEndian: false)
            .Should().BeTrue("{0} has to fit {1} words to be a case at all", magnitude, destination.Length);

        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i * 8, 8));
        }

        var length = destination.Length;
        while (length > 0 && destination[length - 1] == 0)
        {
            length--;
        }

        return length;
    }

    private static BigInteger ToInteger(ReadOnlySpan<ulong> words, int length)
    {
        var value = BigInteger.Zero;
        for (var i = length - 1; i >= 0; i--)
        {
            value = (value << 64) | words[i];
        }

        return value;
    }
}
