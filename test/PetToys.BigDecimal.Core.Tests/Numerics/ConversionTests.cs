using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using AwesomeAssertions;
using PetToys.BigDecimal.Numerics.Harness;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Pins how a value converts to and from other numeric types, against <see cref="decimal"/>
/// wherever the specified behaviour is parity with it.
/// </summary>
/// <remarks>
/// <see cref="decimal"/> is the oracle because it faces the same question this type does: it is a
/// real-valued type rather than a bit pattern, so truncating a value it cannot hold means clamping
/// rather than keeping the low bits. What the base class library means by each of the three
/// conversion contracts is not written down anywhere, so it is read off <see cref="decimal"/> by
/// running it rather than guessed from the documentation.
/// </remarks>
public sealed class ConversionTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // Values that straddle the bounds of the recognised targets, plus the fractions that decide
    // which way the truncation goes. Everything here is inside decimal's own domain, because
    // outside it decimal cannot be the oracle.
    private static readonly string[] BoundaryValues =
    [
        "0", "1", "-1", "1.7", "-1.7", "0.5", "-0.5",
        "127", "128", "255", "256", "300", "-300", "-129",
        "32767", "32768", "65535", "65536",
        "2147483647", "2147483648", "-2147483649", "4294967295", "4294967296",
        "9223372036854775807", "9223372036854775808", "-9223372036854775809",
        "18446744073709551615", "18446744073709551616",
        "79228162514264337593543950335", "-79228162514264337593543950335",
        "1e28", "-1e28",
    ];

    [Fact]
    public void TheThreeContracts_MatchDecimal()
    {
        foreach (var text in BoundaryValues)
        {
            var value = BigDecimal.Parse(text, NumberStyles.Float, Invariant);
            var reference = decimal.Parse(text, NumberStyles.Float, Invariant);

            AgreeWithDecimal<byte>(text, value, reference);
            AgreeWithDecimal<sbyte>(text, value, reference);
            AgreeWithDecimal<short>(text, value, reference);
            AgreeWithDecimal<ushort>(text, value, reference);
            AgreeWithDecimal<int>(text, value, reference);
            AgreeWithDecimal<uint>(text, value, reference);
            AgreeWithDecimal<long>(text, value, reference);
            AgreeWithDecimal<ulong>(text, value, reference);
            AgreeWithDecimal<Int128>(text, value, reference);
            AgreeWithDecimal<UInt128>(text, value, reference);
            AgreeWithDecimal<nint>(text, value, reference);
            AgreeWithDecimal<nuint>(text, value, reference);
            AgreeWithDecimal<char>(text, value, reference);
            AgreeWithDecimal<decimal>(text, value, reference);
            AgreeWithDecimal<Half>(text, value, reference);
            AgreeWithDecimal<float>(text, value, reference);
            AgreeWithDecimal<double>(text, value, reference);
            AgreeWithDecimal<NFloat>(text, value, reference);
            AgreeWithDecimal<BigInteger>(text, value, reference);
        }
    }

    [Fact]
    public void ATruncatingConversion_IsASaturatingOne()
    {
        // Truncation is defined on the two's-complement representation of an integer, which a
        // scaled decimal value does not have. byte.CreateTruncating(300) is 44 because an int has
        // one; byte.CreateTruncating(300m) is 255 because a decimal does not.
        foreach (var text in BoundaryValues)
        {
            var value = BigDecimal.Parse(text, NumberStyles.Float, Invariant);

            SaturatingAndTruncatingAgree<byte>(text, value);
            SaturatingAndTruncatingAgree<sbyte>(text, value);
            SaturatingAndTruncatingAgree<short>(text, value);
            SaturatingAndTruncatingAgree<ushort>(text, value);
            SaturatingAndTruncatingAgree<int>(text, value);
            SaturatingAndTruncatingAgree<uint>(text, value);
            SaturatingAndTruncatingAgree<long>(text, value);
            SaturatingAndTruncatingAgree<ulong>(text, value);
            SaturatingAndTruncatingAgree<Int128>(text, value);
            SaturatingAndTruncatingAgree<UInt128>(text, value);
            SaturatingAndTruncatingAgree<char>(text, value);
            SaturatingAndTruncatingAgree<decimal>(text, value);
            SaturatingAndTruncatingAgree<double>(text, value);
            SaturatingAndTruncatingAgree<BigInteger>(text, value);
        }
    }

    [Fact]
    public void AValueBeyondEveryTarget_SaturatesAtItsOwnExtreme()
    {
        // Beyond decimal's domain, so decimal cannot be the oracle and the contract is asserted
        // directly. The checked variant refuses what the saturating one clamps, except for a
        // target that can represent the overflow itself.
        byte.CreateSaturating(BigDecimal.MaxValue).Should().Be(byte.MaxValue);
        byte.CreateSaturating(BigDecimal.MinValue).Should().Be(byte.MinValue);
        sbyte.CreateSaturating(BigDecimal.MinValue).Should().Be(sbyte.MinValue);
        Int128.CreateSaturating(BigDecimal.MaxValue).Should().Be(Int128.MaxValue);
        Int128.CreateSaturating(BigDecimal.MinValue).Should().Be(Int128.MinValue);
        UInt128.CreateSaturating(BigDecimal.MaxValue).Should().Be(UInt128.MaxValue);
        UInt128.CreateSaturating(BigDecimal.MinValue).Should().Be(UInt128.Zero);
        decimal.CreateSaturating(BigDecimal.MaxValue).Should().Be(decimal.MaxValue);
        decimal.CreateSaturating(BigDecimal.MinValue).Should().Be(decimal.MinValue);

        // The decimal boundary itself, which decimal cannot parse and so cannot be an oracle for.
        var justPast = BigDecimal.Parse("79228162514264337593543950335.5", Invariant);
        decimal.CreateSaturating(justPast).Should().Be(decimal.MaxValue);
        decimal.CreateSaturating(-justPast).Should().Be(decimal.MinValue);
        var pastDecimal = () => decimal.CreateChecked(justPast);
        pastDecimal.Should().Throw<OverflowException>();

        var throws = () => byte.CreateChecked(BigDecimal.MaxValue);
        throws.Should().Throw<OverflowException>();
        var alsoThrows = () => decimal.CreateChecked(BigDecimal.MaxValue);
        alsoThrows.Should().Throw<OverflowException>();

        // A target that has infinities receives one rather than an exception, in every variant.
        // float.CreateChecked(double.MaxValue) is Infinity for the same reason.
        Half.CreateChecked(BigDecimal.MaxValue).Should().Be(Half.PositiveInfinity);
        Half.CreateSaturating(BigDecimal.MinValue).Should().Be(Half.NegativeInfinity);
        float.CreateChecked(BigDecimal.MaxValue).Should().Be(float.PositiveInfinity);
        double.CreateChecked(BigDecimal.MaxValue).Should().Be(Math.Pow(2, 256), "the magnitude rounds to 2^256 exactly");

        // BigInteger is unbounded, so nothing about it can overflow.
        BigInteger.CreateChecked(BigDecimal.MaxValue).Should().Be(
            BigInteger.Pow(2, 256) - BigInteger.One);
    }

    [Fact]
    public void AnUnrecognisedType_IsRefusedByAllThreeCreateMethods()
    {
        // Returning Zero, which CreateSaturating and CreateTruncating did, is indistinguishable
        // from a successful conversion of the number zero.
        var checkedCreate = () => BigDecimal.CreateChecked(default(ForeignNumber));
        var saturatingCreate = () => BigDecimal.CreateSaturating(default(ForeignNumber));
        var truncatingCreate = () => BigDecimal.CreateTruncating(default(ForeignNumber));

        checkedCreate.Should().Throw<NotSupportedException>();
        saturatingCreate.Should().Throw<NotSupportedException>();
        truncatingCreate.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ATypeThatConvertsItself_IsAskedSecond()
    {
        // BigDecimal does not recognise this type, so the only way to a value is through the
        // type's own TryConvertTo, which answers 42.
        var expected = BigDecimal.Parse("42", Invariant);

        BigDecimal.CreateChecked(default(AnsweringNumber)).Should().Be(expected);
        BigDecimal.CreateSaturating(default(AnsweringNumber)).Should().Be(expected);
        BigDecimal.CreateTruncating(default(AnsweringNumber)).Should().Be(expected);
    }

    [Fact]
    public void TheCreateMethods_AreCallableOnTheTypeItself()
    {
        // The assertion is that these call sites compile. They are explicit interface
        // implementations in every version before this one, so none of them could be written.
        BigDecimal.CreateChecked(42).Should().Be(BigDecimal.Parse("42", Invariant));
        BigDecimal.CreateSaturating(42.5).Should().Be(BigDecimal.Parse("42.5", Invariant));
        BigDecimal.CreateTruncating(42m).Should().Be(BigDecimal.Parse("42", Invariant));
    }

    [Fact]
    public void EveryRecognisedType_ConvertsInBothDirections()
    {
        RoundTripsThrough<byte>(7);
        RoundTripsThrough<sbyte>(-7);
        RoundTripsThrough<short>(-3000);
        RoundTripsThrough<ushort>(3000);
        RoundTripsThrough<int>(-123456789);
        RoundTripsThrough<uint>(123456789);
        RoundTripsThrough<long>(-1234567890123456789);
        RoundTripsThrough<ulong>(1234567890123456789);
        RoundTripsThrough<Int128>(Int128.MinValue);
        RoundTripsThrough<UInt128>(UInt128.MaxValue);
        RoundTripsThrough<nint>(-123456);
        RoundTripsThrough<nuint>(123456);
        RoundTripsThrough<char>('A');
        RoundTripsThrough<Half>((Half)1.5f);
        RoundTripsThrough<float>(1.0000001f);
        RoundTripsThrough<double>(0.1);
        RoundTripsThrough<NFloat>((NFloat)1.5);
        RoundTripsThrough<BigInteger>(BigInteger.Pow(10, 40));
        RoundTripsThrough<decimal>(123456.789m);
    }

    [Fact]
    public void AHalf_ConvertsRatherThanYieldingZero()
    {
        // The fallback alone cannot rescue this pair: Half does not recognise BigDecimal either,
        // so without Half in our own list the result is an exception, and before this change it
        // was a silent zero.
        BigDecimal.CreateSaturating((Half)1.5f).Should().Be(BigDecimal.Parse("1.5", Invariant));
        BigDecimal.CreateChecked((Half)1.5f).Should().Be(BigDecimal.Parse("1.5", Invariant));
    }

    [Fact]
    public void ANonFiniteSource_ConvertsToTheMatchingValueUnderEveryContract()
    {
        // Until the type had its own NaN and infinities the checked contract threw here and the
        // other two flattened NaN to zero and an infinity to MaxValue. The value is representable
        // now, so none of the three has anything left to refuse and all three agree.
        foreach (var converted in new[]
        {
            BigDecimal.CreateChecked(double.NaN),
            BigDecimal.CreateSaturating(double.NaN),
            BigDecimal.CreateTruncating(double.NaN),
            BigDecimal.CreateSaturating(float.NaN),
            BigDecimal.CreateSaturating(Half.NaN),
            (BigDecimal)double.NaN,
            (BigDecimal)float.NaN,
        })
        {
            BigDecimal.IsNaN(converted).Should().BeTrue();
        }

        BigDecimal.CreateChecked(double.PositiveInfinity).Should().Be(BigDecimal.PositiveInfinity);
        BigDecimal.CreateSaturating(double.PositiveInfinity).Should().Be(BigDecimal.PositiveInfinity);
        BigDecimal.CreateTruncating(float.PositiveInfinity).Should().Be(BigDecimal.PositiveInfinity);
        BigDecimal.CreateSaturating(double.NegativeInfinity).Should().Be(BigDecimal.NegativeInfinity);
        ((BigDecimal)float.NegativeInfinity).Should().Be(BigDecimal.NegativeInfinity);
    }

    [Fact]
    public void AFiniteSourceTooLargeForTheType_StillSaturatesAndStillThrowsWhenChecked()
    {
        // The non-finite values did not take this behaviour with them: a finite source outside
        // the range clamps under the saturating contract and throws under the checked one, and
        // in particular it does not become an infinity.
        BigDecimal.CreateSaturating(double.MaxValue).Should().Be(BigDecimal.MaxValue);
        BigDecimal.CreateSaturating(double.MinValue).Should().Be(BigDecimal.MinValue);
        BigDecimal.CreateSaturating(BigInteger.Pow(10, 100)).Should().Be(BigDecimal.MaxValue);
        BigDecimal.CreateSaturating(-BigInteger.Pow(10, 100)).Should().Be(BigDecimal.MinValue);

        var checkedTooLarge = () => BigDecimal.CreateChecked(double.MaxValue);
        checkedTooLarge.Should().Throw<OverflowException>();
    }

    [Fact]
    public void ANonFiniteValue_ConvertsOutByContractRatherThanByDestination()
    {
        // Measured against what the base class library does converting a non-finite double to an
        // integer type: the checked route throws and the saturating one answers. A saturating
        // conversion that throws would not be one, in this direction either.
        var castToDecimal = () => (decimal)BigDecimal.NaN;
        var castToInteger = () => (long)BigDecimal.PositiveInfinity;
        var castToBigInteger = () => (BigInteger)BigDecimal.NegativeInfinity;
        var checkedToInt = () => int.CreateChecked(BigDecimal.NaN);
        var checkedToDecimal = () => decimal.CreateChecked(BigDecimal.PositiveInfinity);

        castToDecimal.Should().Throw<OverflowException>();
        castToInteger.Should().Throw<OverflowException>();
        castToBigInteger.Should().Throw<OverflowException>();
        checkedToInt.Should().Throw<OverflowException>();
        checkedToDecimal.Should().Throw<OverflowException>();

        int.CreateSaturating(BigDecimal.NaN).Should().Be(0);
        int.CreateTruncating(BigDecimal.NaN).Should().Be(0);
        int.CreateSaturating(BigDecimal.PositiveInfinity).Should().Be(int.MaxValue);
        int.CreateSaturating(BigDecimal.NegativeInfinity).Should().Be(int.MinValue);
        uint.CreateSaturating(BigDecimal.NegativeInfinity).Should().Be(0u);
        long.CreateSaturating(BigDecimal.PositiveInfinity).Should().Be(long.MaxValue);

        // Not cross-checked against double here, deliberately: int.CreateSaturating(double.NaN)
        // is int.MinValue on net8.0 and 0 from net9.0 on, so "what double does" has two answers
        // and cannot be the oracle. Found by running this suite on all three frameworks, not by
        // reading release notes. The modern answer is the one pinned above, on every framework
        // this package targets, so the constants are the contract rather than the platform.
    }

    [Fact]
    public void TheSaturatingRouteOut_AnswersNaNWithZeroForEveryDestination()
    {
        // decimal and BigInteger each reach the destination by a path of their own, and each of
        // them fell through to a cast that throws: decimal because both range comparisons are
        // false against NaN, BigInteger because its cast could never overflow before and so
        // ignored the saturate flag.
        decimal.CreateSaturating(BigDecimal.NaN).Should().Be(0m);
        decimal.CreateTruncating(BigDecimal.NaN).Should().Be(0m);
        BigInteger.CreateSaturating(BigDecimal.NaN).Should().Be(BigInteger.Zero);
        BigInteger.CreateTruncating(BigDecimal.NaN).Should().Be(BigInteger.Zero);

        decimal.CreateSaturating(BigDecimal.NaN).Should().Be(decimal.CreateSaturating(double.NaN));
        BigInteger.CreateSaturating(BigDecimal.NaN).Should().Be(BigInteger.CreateSaturating(double.NaN));

        // The infinities keep their own answers: decimal clamps, BigInteger has no extreme to
        // clamp to and throws, and both of those are what the same call does for a double.
        decimal.CreateSaturating(BigDecimal.PositiveInfinity).Should().Be(decimal.MaxValue);
        decimal.CreateSaturating(BigDecimal.NegativeInfinity).Should().Be(decimal.MinValue);

        var bigInfinity = () => BigInteger.CreateSaturating(BigDecimal.PositiveInfinity);
        var theirs = () => BigInteger.CreateSaturating(double.PositiveInfinity);
        bigInfinity.Should().Throw<OverflowException>();
        theirs.Should().Throw<OverflowException>();
    }

    [Fact]
    public void ANonFiniteValue_RoundTripsThroughTheBinaryFloats()
    {
        double.IsNaN((double)BigDecimal.NaN).Should().BeTrue();
        ((double)BigDecimal.PositiveInfinity).Should().Be(double.PositiveInfinity);
        ((double)BigDecimal.NegativeInfinity).Should().Be(double.NegativeInfinity);
        float.IsNaN((float)BigDecimal.NaN).Should().BeTrue();
        ((float)BigDecimal.NegativeInfinity).Should().Be(float.NegativeInfinity);

        ((BigDecimal)(double)BigDecimal.PositiveInfinity).Should().Be(BigDecimal.PositiveInfinity);
        BigDecimal.IsNaN((BigDecimal)(double)BigDecimal.NaN).Should().BeTrue();
    }

    [Fact]
    public void TheShortestForm_IsTakenRatherThanAFixedDigitCount()
    {
        // "G15" gave 0.1 as well, and gave 3.14159265358979 for pi, which does not come back.
        ((BigDecimal)0.1).ToString(null, Invariant).Should().Be("0.1");
        ((BigDecimal)0.1f).ToString(null, Invariant).Should().Be("0.1");
        ((BigDecimal)double.Pi).ToString(null, Invariant).Should().Be("3.141592653589793");
        ((BigDecimal)(1.0 / 3.0)).ToString(null, Invariant).Should().Be("0.3333333333333333");
    }

    [Fact]
    public void TheDivergenceFromDecimal_IsAsserted()
    {
        // Named in the specification as a deliberate departure, so it is asserted as one. decimal
        // rounds a float to 7 significant digits and a double to 15 because it has 28 digits to
        // spend; this type has 77 and hands the value back.
        var ours = (BigDecimal)1.0000001f;
        var theirs = (decimal)1.0000001f;

        ours.ToString(null, Invariant).Should().Be("1.0000001");
        theirs.Should().Be(1m, "decimal rounds the value away entirely");
        ours.Should().NotBe((BigDecimal)theirs, "the departure is deliberate and must stay visible");

        ((BigDecimal)16777216f).ToString(null, Invariant).Should().Be("16777216");
        ((decimal)16777216f).Should().Be(16777220m);
    }

    [Fact]
    public void TheTopOfTheWindow_RoundTripsAndOneDigitMoreThrows()
    {
        const double top = 1.2345678901234567E76;
        const double past = 1.2345678901234567E77;

        var converted = (BigDecimal)top;
        converted.Precision.Should().Be(77);
        ((double)converted).Should().Be(top);

        var throws = () => (BigDecimal)past;
        throws.Should().Throw<OverflowException>();
    }

    [Fact]
    public void TheBottomOfTheWindow_RoundTripsAndOnePlaceFurtherDoesNot()
    {
        const double bottom = 1.2345678901234567E-239;
        const double past = 1.2345678901234567E-240;

        var converted = (BigDecimal)bottom;
        converted.Scale.Should().Be(BigDecimal.MaxScale);
        ((double)converted).Should().Be(bottom);

        // Not a fault of the conversion: the scale limit is where every operation stops, and the
        // value is rounded there rather than refused.
        var rounded = (BigDecimal)past;
        rounded.Scale.Should().Be(BigDecimal.MaxScale);
        ((double)rounded).Should().NotBe(past);

        ((BigDecimal)1E-255).Should().NotBe(BigDecimal.Zero);
        ((BigDecimal)1E-256).Should().Be(BigDecimal.Zero, "below the scale limit a value rounds to zero");
    }

    [Fact]
    public void EveryFiniteHalf_SurvivesTheRoundTrip()
    {
        // Half is small enough to enumerate, so this one is exhaustive rather than sampled: all
        // 63 488 finite bit patterns, through the conversion and back.
        for (var bits = 0; bits <= ushort.MaxValue; bits++)
        {
            var value = BitConverter.Int16BitsToHalf(unchecked((short)bits));
            if (!Half.IsFinite(value))
            {
                continue;
            }

            Half.CreateChecked(BigDecimal.CreateChecked(value)).Should().Be(
                value,
                "the Half with bit pattern {0} must come back",
                bits);
        }
    }

    [Theory]
    [FuzzData]
    public void AFiniteFloat_SurvivesTheRoundTrip(int seed, int cases)
    {
        var random = new Random(seed);

        for (var index = 0; index < cases; index++)
        {
            var value = BitConverter.UInt32BitsToSingle(unchecked((uint)random.Next(int.MinValue, int.MaxValue)));
            if (!float.IsFinite(value))
            {
                continue;
            }

            ((float)(BigDecimal)value).Should().Be(
                value,
                "the whole finite range of float fits, so seed {0} case {1} must come back",
                seed,
                index);
        }
    }

    [Theory]
    [FuzzData]
    public void ADoubleInsideTheWindow_SurvivesTheRoundTrip(int seed, int cases)
    {
        var random = new Random(seed);

        for (var index = 0; index < cases; index++)
        {
            // Exponents chosen so the shortest form always fits: 77 integer digits at the top and
            // a scale of 255 at the bottom are the two walls, and this stays inside both.
            var exponent = random.Next(-200, 60);
            var value = ((random.NextDouble() * 9) + 1) * Math.Pow(10, exponent);
            if (!double.IsFinite(value) || value == 0)
            {
                continue;
            }

            if (random.Next(2) == 0)
            {
                value = -value;
            }

            ((double)(BigDecimal)value).Should().Be(
                value,
                "seed {0} case {1} is inside the window",
                seed,
                index);
        }
    }

    [Fact]
    public void AGenericConversion_AllocatesNothingInEitherDirection()
    {
        // The inventory carries an entry per recognised type; this is the pair that measured 24
        // and 32 bytes before the switch through object was replaced.
        var value = BigDecimal.Parse("42.5", Invariant);

        Allocations.Measure(() => Allocations.OtherSink = long.CreateSaturating(value))
            .Should().Be(0, "converting out of BigDecimal must not box");
        Allocations.Measure(() => Allocations.OtherSink = (long)decimal.CreateSaturating(value))
            .Should().Be(0, "nor when the target is wider than the sink");
        Allocations.Measure(() => Allocations.Sink = BigDecimal.CreateSaturating(42L))
            .Should().Be(0, "converting into BigDecimal must not box");
        Allocations.Measure(() => Allocations.Sink = BigDecimal.CreateChecked(0.1))
            .Should().Be(0, "nor when the source is a double");
    }

    private static void AgreeWithDecimal<TTarget>(string text, BigDecimal value, decimal reference)
        where TTarget : INumberBase<TTarget>
    {
        Outcome(() => TTarget.CreateChecked(value)).Should().Be(
            Outcome(() => TTarget.CreateChecked(reference)),
            "{0} converted to {1}, checked",
            text,
            typeof(TTarget).Name);

        Outcome(() => TTarget.CreateSaturating(value)).Should().Be(
            Outcome(() => TTarget.CreateSaturating(reference)),
            "{0} converted to {1}, saturating",
            text,
            typeof(TTarget).Name);

        Outcome(() => TTarget.CreateTruncating(value)).Should().Be(
            Outcome(() => TTarget.CreateTruncating(reference)),
            "{0} converted to {1}, truncating",
            text,
            typeof(TTarget).Name);
    }

    private static void SaturatingAndTruncatingAgree<TTarget>(string text, BigDecimal value)
        where TTarget : INumberBase<TTarget>
    {
        Outcome(() => TTarget.CreateTruncating(value)).Should().Be(
            Outcome(() => TTarget.CreateSaturating(value)),
            "{0} converted to {1}: truncating and saturating are one operation here",
            text,
            typeof(TTarget).Name);
    }

    private static void RoundTripsThrough<TOther>(TOther value)
        where TOther : INumberBase<TOther>
    {
        var converted = BigDecimal.CreateChecked(value);

        TOther.CreateChecked(converted).Should().Be(value, "{0} survives both directions", typeof(TOther).Name);
    }

    /// <summary>
    /// Renders what a conversion produced, an exception included, so that the two oracles can be
    /// compared on failures as well as on values.
    /// </summary>
    private static string Outcome<TResult>(Func<TResult> conversion)
    {
        try
        {
            return string.Create(Invariant, $"{conversion()}");
        }
        catch (OverflowException)
        {
            return "OverflowException";
        }
    }
}
