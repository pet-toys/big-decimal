using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// NaN and the two infinities, verified against <see cref="double"/> rather than against
/// constants written by hand.
/// </summary>
/// <remarks>
/// <see cref="decimal"/> has none of these values, so the parity oracle the rest of the suite uses
/// is silent here and <see cref="double"/> takes its place. Where the two deliberately differ the
/// divergence is asserted as a divergence, so that agreeing again fails.
/// </remarks>
public sealed class NonFiniteTests
{
    private static readonly char[] Operators = ['+', '-', '*', '/', '%'];

    /// <summary>The five operands the table is taken over, paired with the double that stands for each.</summary>
    public static TheoryData<string> Operands => ["+Inf", "-Inf", "NaN", "1", "0"];

    private static BigDecimal Big(string name) => name switch
    {
        "+Inf" => BigDecimal.PositiveInfinity,
        "-Inf" => BigDecimal.NegativeInfinity,
        "NaN" => BigDecimal.NaN,
        "1" => BigDecimal.One,
        _ => BigDecimal.Zero,
    };

    private static double Double(string name) => name switch
    {
        "+Inf" => double.PositiveInfinity,
        "-Inf" => double.NegativeInfinity,
        "NaN" => double.NaN,
        "1" => 1.0,
        _ => 0.0,
    };

    private static T Apply<T>(T left, char op, T right)
        where T : INumber<T>
    {
        return op switch
        {
            '+' => left + right,
            '-' => left - right,
            '*' => left * right,
            '/' => left / right,
            _ => left % right,
        };
    }

    // A static abstract interface member can only be reached through a type parameter, so the
    // handful this file compares across the two types go through these.
    private static T MaxNumberOf<T>(T x, T y)
        where T : INumber<T> => T.MaxNumber(x, y);

    private static T MinNumberOf<T>(T x, T y)
        where T : INumber<T> => T.MinNumber(x, y);

    private static int SignOf<T>(T value)
        where T : INumber<T> => T.Sign(value);

    private static T MaxMagnitudeOf<T>(T x, T y)
        where T : INumberBase<T> => T.MaxMagnitude(x, y);

    private static T MinMagnitudeOf<T>(T x, T y)
        where T : INumberBase<T> => T.MinMagnitude(x, y);

    private static T MaxMagnitudeNumberOf<T>(T x, T y)
        where T : INumberBase<T> => T.MaxMagnitudeNumber(x, y);

    private static T MinMagnitudeNumberOf<T>(T x, T y)
        where T : INumberBase<T> => T.MinMagnitudeNumber(x, y);

    private static bool IsCanonicalOf<T>(T value)
        where T : INumberBase<T> => T.IsCanonical(value);

    private static bool IsRealNumberOf<T>(T value)
        where T : INumberBase<T> => T.IsRealNumber(value);

    private static bool IsIntegerOf<T>(T value)
        where T : INumberBase<T> => T.IsInteger(value);

    private static bool IsNormalOf<T>(T value)
        where T : INumberBase<T> => T.IsNormal(value);

    private static bool IsSubnormalOf<T>(T value)
        where T : INumberBase<T> => T.IsSubnormal(value);

    private static string Describe<T>(T value)
        where T : INumberBase<T>
    {
        if (T.IsNaN(value))
        {
            return "NaN";
        }

        if (T.IsPositiveInfinity(value))
        {
            return "Infinity";
        }

        if (T.IsNegativeInfinity(value))
        {
            return "-Infinity";
        }

        var text = value.ToString(null, CultureInfo.InvariantCulture);

        // double has a negative zero and this type does not, which is the one entry of the table
        // that differs. It is asserted on its own below rather than compared away here.
        return text == "-0" ? "0" : text;
    }

    private static (string Name, bool Value)[] Predicates<T>(T value)
        where T : INumberBase<T> =>
    [
        ("IsCanonical", T.IsCanonical(value)),
        ("IsComplexNumber", T.IsComplexNumber(value)),
        ("IsEvenInteger", T.IsEvenInteger(value)),
        ("IsFinite", T.IsFinite(value)),
        ("IsImaginaryNumber", T.IsImaginaryNumber(value)),
        ("IsInfinity", T.IsInfinity(value)),
        ("IsInteger", T.IsInteger(value)),
        ("IsNaN", T.IsNaN(value)),
        ("IsNegative", T.IsNegative(value)),
        ("IsNegativeInfinity", T.IsNegativeInfinity(value)),
        ("IsNormal", T.IsNormal(value)),
        ("IsOddInteger", T.IsOddInteger(value)),
        ("IsPositive", T.IsPositive(value)),
        ("IsPositiveInfinity", T.IsPositiveInfinity(value)),
        ("IsRealNumber", T.IsRealNumber(value)),
        ("IsSubnormal", T.IsSubnormal(value)),
        ("IsZero", T.IsZero(value)),
    ];

    [Theory]
    [MemberData(nameof(Operands))]
    public void TheArithmeticTable_IsDoubles(string leftName)
    {
        var left = Big(leftName);
        var leftDouble = Double(leftName);

        foreach (var rightName in new[] { "+Inf", "-Inf", "NaN", "1", "0" })
        {
            var right = Big(rightName);
            if (BigDecimal.IsFinite(left) && BigDecimal.IsFinite(right))
            {
                // Two finite operands are the rest of the suite's business, and their division by
                // zero throws rather than answering, which is asserted in its own case below.
                continue;
            }

            foreach (var op in Operators)
            {
                var expected = Describe(Apply(leftDouble, op, Double(rightName)));

                Describe(Apply(left, op, right)).Should().Be(
                    expected,
                    "{0} {1} {2} follows double",
                    leftName,
                    op,
                    rightName);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Operands))]
    public void ThePowerTable_IsDoubles(string baseName)
    {
        if (BigDecimal.IsFinite(Big(baseName)))
        {
            // A finite base is the rest of this change's business. Its one non-finite-looking edge,
            // a zero base with a negative exponent, is asserted below as the divergence it is.
            return;
        }

        var value = Big(baseName);
        var asDouble = Double(baseName);

        foreach (var exponent in new[] { 0, 1, 2, 3, -1, -2, -3, int.MaxValue })
        {
            Describe(BigDecimal.Pow(value, exponent)).Should().Be(
                Describe(Math.Pow(asDouble, exponent)),
                "{0} ^ {1} follows double",
                baseName,
                exponent);
        }
    }

    [Fact]
    public void AnExponentOfZero_DoesNotReadANaNBase()
    {
        // The one place in the package where a NaN operand does not propagate, because x^0 does not
        // read x at all. It is double's answer and IEEE 754's.
        BigDecimal.Pow(BigDecimal.NaN, 0).Should().Be(BigDecimal.One);
        double.IsNaN(Math.Pow(double.NaN, 0)).Should().BeFalse();
    }

    [Fact]
    public void ANegativeExponentOverNegativeInfinity_IsZeroWithNoSign()
    {
        var result = BigDecimal.Pow(BigDecimal.NegativeInfinity, -1);

        result.IsZero.Should().BeTrue();
        result.IsNegative.Should().BeFalse();

        // The divergence, and the same one 1 / -Infinity carries: double has a negative zero here.
        double.IsNegative(Math.Pow(double.NegativeInfinity, -1)).Should().BeTrue();
    }

    [Fact]
    public void AZeroBaseWithANegativeExponent_ThrowsWhereDoubleGivesAnInfinity()
    {
        var act = () => BigDecimal.Pow(BigDecimal.Zero, -1);

        act.Should().Throw<DivideByZeroException>();
        double.IsPositiveInfinity(Math.Pow(0.0, -1)).Should().BeTrue();
    }

    [Fact]
    public void ANaNOperand_WinsWhicheverSideItIsOn()
    {
        foreach (var other in new[] { BigDecimal.PositiveInfinity, BigDecimal.NegativeInfinity, BigDecimal.One, BigDecimal.Zero })
        {
            foreach (var op in Operators)
            {
                BigDecimal.IsNaN(Apply(BigDecimal.NaN, op, other)).Should().BeTrue();
                BigDecimal.IsNaN(Apply(other, op, BigDecimal.NaN)).Should().BeTrue();
            }
        }
    }

    [Fact]
    public void TheRemainder_IsNotSymmetric()
    {
        // The entry a reader gets wrong from memory, and the reason the table is asserted whole.
        BigDecimal.IsNaN(BigDecimal.PositiveInfinity % BigDecimal.One).Should().BeTrue();
        (BigDecimal.One % BigDecimal.PositiveInfinity).Should().Be(BigDecimal.One);
    }

    [Fact]
    public void DividingByAnInfinity_GivesZeroWithNoSign()
    {
        // The one divergence from double in the whole table: it answers -0 here and this type has
        // no negative zero to answer with. Asserted as a divergence so that agreeing again fails.
        var quotient = BigDecimal.One / BigDecimal.NegativeInfinity;

        quotient.Should().Be(BigDecimal.Zero);
        quotient.IsNegative.Should().BeFalse();
        quotient.ToString(null, CultureInfo.InvariantCulture).Should().Be("0");
        (1.0 / double.NegativeInfinity).ToString(CultureInfo.InvariantCulture).Should().Be(
            "-0",
            "double is the thing being diverged from, so its answer is pinned too");
    }

    [Fact]
    public void NoFiniteOperand_ProducesANonFiniteValue()
    {
        // The natural reading of "the type has infinities now" is that these stop throwing. They
        // do not: the infinities exist because a numeric column holds them, not as a saturation.
        var divideByZero = () => BigDecimal.One / BigDecimal.Zero;
        var zeroByZero = () => BigDecimal.Zero / BigDecimal.Zero;
        var remainderByZero = () => BigDecimal.One % BigDecimal.Zero;
        var scaledDivideByZero = () => BigDecimal.Divide(BigDecimal.One, BigDecimal.Zero, 4, MidpointRounding.ToEven);
        var product = () => BigDecimal.MaxValue * BigDecimal.MaxValue;
        var sum = () => BigDecimal.MaxValue + BigDecimal.MaxValue;

        divideByZero.Should().Throw<DivideByZeroException>();
        zeroByZero.Should().Throw<DivideByZeroException>();
        remainderByZero.Should().Throw<DivideByZeroException>();
        scaledDivideByZero.Should().Throw<DivideByZeroException>();
        product.Should().Throw<OverflowException>();
        sum.Should().Throw<OverflowException>();
    }

    [Fact]
    public void AnInfiniteDividend_IsNotDivisionByZero()
    {
        // The guard has to sit above the zero-divisor check, or this throws instead of answering.
        (BigDecimal.PositiveInfinity / BigDecimal.Zero).Should().Be(BigDecimal.PositiveInfinity);
        (BigDecimal.NegativeInfinity / BigDecimal.Zero).Should().Be(BigDecimal.NegativeInfinity);
        (double.PositiveInfinity / 0.0).Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void MultiplyingAnInfinityByZero_DoesNotTakeTheZeroShortcut()
    {
        // The multiplication fast path returns zero as soon as either operand's magnitude is zero,
        // and a non-finite value's magnitude is zero, so the ordering of the two checks is what
        // this case is about.
        BigDecimal.IsNaN(BigDecimal.Zero * BigDecimal.PositiveInfinity).Should().BeTrue();
        BigDecimal.IsNaN(BigDecimal.NegativeInfinity * BigDecimal.Zero).Should().BeTrue();
    }

    [Fact]
    public void EqualityOrderingAndHashing_AnswerNaNByThreeDifferentRules()
    {
        var nan = BigDecimal.NaN;

        // IEEE, for the operator. The two NaNs are built by different routes so that the
        // comparison is between two values rather than between a value and itself.
        var alsoNaN = BigDecimal.Parse("NaN", CultureInfo.InvariantCulture);
        var theirNaN = double.Parse("NaN", CultureInfo.InvariantCulture);

        (nan == alsoNaN).Should().BeFalse();
        (nan != alsoNaN).Should().BeTrue();

        // double's own operator is not asserted here: CA2242 refuses the expression, which is the
        // analyzer agreeing that == is the wrong way to ask about a NaN. Equals is the way, and it
        // answers the other way round, which is the whole point of this case.
        theirNaN.Equals(double.NaN).Should().BeTrue();

        // The total order, for Equals and GetHashCode.
        nan.Equals(BigDecimal.NaN).Should().BeTrue();
        nan.Equals((object)BigDecimal.NaN).Should().BeTrue();
        nan.GetHashCode().Should().Be(BigDecimal.NaN.GetHashCode());
        double.NaN.Equals(double.NaN).Should().BeTrue();

        // And the total order again, for CompareTo, which places NaN below everything.
        nan.CompareTo(BigDecimal.NaN).Should().Be(0);
        nan.CompareTo(BigDecimal.NegativeInfinity).Should().Be(double.NaN.CompareTo(double.NegativeInfinity));
        nan.CompareTo(BigDecimal.One).Should().Be(double.NaN.CompareTo(1.0));
        BigDecimal.One.CompareTo(nan).Should().Be(1.0.CompareTo(double.NaN));
    }

    [Fact]
    public void EveryRelationalOperator_IsFalseAgainstNaN()
    {
        var nan = BigDecimal.NaN;

        foreach (var other in new[] { BigDecimal.NaN, BigDecimal.PositiveInfinity, BigDecimal.NegativeInfinity, BigDecimal.One, BigDecimal.Zero })
        {
            (nan < other).Should().BeFalse();
            (nan <= other).Should().BeFalse();
            (nan > other).Should().BeFalse();
            (nan >= other).Should().BeFalse();
            (other < nan).Should().BeFalse();
            (other <= nan).Should().BeFalse();
            (other > nan).Should().BeFalse();
            (other >= nan).Should().BeFalse();
            (other == nan).Should().BeFalse();
        }
    }

    [Fact]
    public void TheInfinities_BracketEveryFiniteValue()
    {
        (BigDecimal.PositiveInfinity > BigDecimal.MaxValue).Should().BeTrue();
        (BigDecimal.NegativeInfinity < BigDecimal.MinValue).Should().BeTrue();
        (BigDecimal.PositiveInfinity == BigDecimal.PositiveInfinity).Should().BeTrue();
        (BigDecimal.PositiveInfinity <= BigDecimal.PositiveInfinity).Should().BeTrue();
        (BigDecimal.NegativeInfinity == BigDecimal.PositiveInfinity).Should().BeFalse();
        BigDecimal.PositiveInfinity.GetHashCode().Should().NotBe(BigDecimal.NegativeInfinity.GetHashCode());
        BigDecimal.PositiveInfinity.GetHashCode().Should().NotBe(BigDecimal.Zero.GetHashCode());
    }

    [Fact]
    public void ANaN_SurvivesTheCollectionTypes()
    {
        // The reason Equals and the operator are allowed to disagree at all: a NaN put into a
        // dictionary has to be findable again, and a set has to deduplicate two of them.
        var dictionary = new Dictionary<BigDecimal, int> { [BigDecimal.NaN] = 7 };
        dictionary[BigDecimal.NaN].Should().Be(7);

        new HashSet<BigDecimal> { BigDecimal.NaN, BigDecimal.NaN }.Should().HaveCount(1);
        EqualityComparer<BigDecimal>.Default.Equals(BigDecimal.NaN, BigDecimal.NaN).Should().BeTrue();

        var doubles = new Dictionary<double, int> { [double.NaN] = 7 };
        doubles[double.NaN].Should().Be(7);
    }

    [Fact]
    public void Sorting_IsTotalAndMatchesDouble()
    {
        var values = new[]
        {
            BigDecimal.One,
            BigDecimal.NaN,
            BigDecimal.PositiveInfinity,
            BigDecimal.NegativeInfinity,
            BigDecimal.Zero,
            BigDecimal.NegativeOne,
        };

        var doubles = new[] { 1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.0, -1.0 };

        Array.Sort(values);
        Array.Sort(doubles);

        values.Select(Describe).Should().Equal(doubles.Select(Describe));
    }

    [Theory]
    [MemberData(nameof(Operands))]
    public void ThePredicateTable_IsDoubles(string name)
    {
        var mine = Predicates(Big(name));
        var theirs = Predicates(Double(name));

        for (var i = 0; i < mine.Length; i++)
        {
            var (predicate, actual) = mine[i];
            var expected = theirs[i].Value;

            // The one entry that diverges, and it is the source that is odd: double's default NaN
            // carries the hardware sign bit, so it reports IsNegative. This type's NaN has no sign
            // at all, the way its zero has none.
            if (predicate == "IsNegative" && name == "NaN")
            {
                actual.Should().BeFalse("this type's NaN carries no sign");
                expected.Should().BeTrue("double's own NaN does, which is what makes this a divergence");
                continue;
            }

            actual.Should().Be(expected, "{0}({1}) follows double", predicate, name);
        }
    }

    [Fact]
    public void ThePredicatesAReaderWouldGuessWrong_AreAsserted()
    {
        // Three answers that read backwards from their names, kept as their own case so that a
        // future edit has to argue with them rather than notice them.
        IsCanonicalOf(BigDecimal.NaN).Should().BeTrue();
        IsRealNumberOf(BigDecimal.PositiveInfinity).Should().BeTrue();
        IsRealNumberOf(BigDecimal.NaN).Should().BeFalse();
        IsIntegerOf(BigDecimal.PositiveInfinity).Should().BeFalse();
        IsNormalOf(BigDecimal.PositiveInfinity).Should().BeFalse();
        IsSubnormalOf(BigDecimal.PositiveInfinity).Should().BeFalse();
    }

    [Fact]
    public void TheNumberSelectors_IgnoreNaNWhereThePlainOnesPropagateIt()
    {
        foreach (var (leftName, rightName) in new[] { ("NaN", "1"), ("1", "NaN"), ("NaN", "NaN") })
        {
            var left = Big(leftName);
            var right = Big(rightName);
            var leftDouble = Double(leftName);
            var rightDouble = Double(rightName);

            Describe(MaxNumberOf(left, right)).Should().Be(Describe(MaxNumberOf(leftDouble, rightDouble)));
            Describe(MinNumberOf(left, right)).Should().Be(Describe(MinNumberOf(leftDouble, rightDouble)));
            Describe(MaxMagnitudeNumberOf(left, right)).Should().Be(Describe(MaxMagnitudeNumberOf(leftDouble, rightDouble)));
            Describe(MinMagnitudeNumberOf(left, right)).Should().Be(Describe(MinMagnitudeNumberOf(leftDouble, rightDouble)));

            Describe(BigDecimal.Max(left, right)).Should().Be(Describe(double.Max(leftDouble, rightDouble)));
            Describe(BigDecimal.Min(left, right)).Should().Be(Describe(double.Min(leftDouble, rightDouble)));
            Describe(MaxMagnitudeOf(left, right)).Should().Be(Describe(MaxMagnitudeOf(leftDouble, rightDouble)));
            Describe(MinMagnitudeOf(left, right)).Should().Be(Describe(MinMagnitudeOf(leftDouble, rightDouble)));
        }
    }

    [Fact]
    public void AnInfinity_OutweighsEveryFiniteMagnitude()
    {
        MaxMagnitudeOf(BigDecimal.NegativeInfinity, BigDecimal.MaxValue).Should().Be(BigDecimal.NegativeInfinity);
        MinMagnitudeOf(BigDecimal.NegativeInfinity, BigDecimal.MaxValue).Should().Be(BigDecimal.MaxValue);
        MaxMagnitudeOf(BigDecimal.PositiveInfinity, BigDecimal.NegativeInfinity).Should().Be(BigDecimal.PositiveInfinity);
    }

    [Fact]
    public void TheShapingOperations_PassANonFiniteValueThrough()
    {
        foreach (var value in new[] { BigDecimal.NaN, BigDecimal.PositiveInfinity, BigDecimal.NegativeInfinity })
        {
            BigDecimal.Round(value, 2).Should().Be(value);
            BigDecimal.Round(value).Should().Be(value);
            BigDecimal.Truncate(value).Should().Be(value);
            BigDecimal.Floor(value).Should().Be(value);
            BigDecimal.Ceiling(value).Should().Be(value);
            value.WithScale(7).Should().Be(value);
            value.WithScale(7).Scale.Should().Be(0);
        }
    }

    [Fact]
    public void NegationAndAbs_ActOnAnInfinitysSignAndLeaveNaNAlone()
    {
        (-BigDecimal.PositiveInfinity).Should().Be(BigDecimal.NegativeInfinity);
        (-BigDecimal.NegativeInfinity).Should().Be(BigDecimal.PositiveInfinity);
        BigDecimal.IsNaN(-BigDecimal.NaN).Should().BeTrue();

        BigDecimal.Abs(BigDecimal.NegativeInfinity).Should().Be(BigDecimal.PositiveInfinity);
        BigDecimal.Abs(BigDecimal.PositiveInfinity).Should().Be(BigDecimal.PositiveInfinity);
        BigDecimal.IsNaN(BigDecimal.Abs(BigDecimal.NaN)).Should().BeTrue();
    }

    [Fact]
    public void Sign_AnswersTheInfinitiesAndRefusesNaNThroughBothRoutes()
    {
        BigDecimal.PositiveInfinity.Sign.Should().Be(1);
        BigDecimal.NegativeInfinity.Sign.Should().Be(-1);

        var property = () => BigDecimal.NaN.Sign;
        var contract = () => SignOf(BigDecimal.NaN);
        var theirs = () => double.Sign(double.NaN);

        property.Should().Throw<ArithmeticException>();
        contract.Should().Throw<ArithmeticException>("the property and the interface must not disagree");
        theirs.Should().Throw<ArithmeticException>("which is where the behaviour comes from");
    }

    [Fact]
    public void Clamp_PassesNaNThroughAndClampsAnInfinity()
    {
        BigDecimal.IsNaN(BigDecimal.Clamp(BigDecimal.NaN, BigDecimal.Zero, BigDecimal.One)).Should().BeTrue();
        BigDecimal.Clamp(BigDecimal.PositiveInfinity, BigDecimal.Zero, BigDecimal.One).Should().Be(BigDecimal.One);
        BigDecimal.Clamp(BigDecimal.NegativeInfinity, BigDecimal.Zero, BigDecimal.One).Should().Be(BigDecimal.Zero);

        double.IsNaN(double.Clamp(double.NaN, 0, 1)).Should().BeTrue();
        double.Clamp(double.PositiveInfinity, 0, 1).Should().Be(1);
    }

    [Fact]
    public void TheWordLevelAccessors_RefuseANonFiniteValue()
    {
        // Both used to report four zero words and a zero mantissa, which FromWords and FromScaled
        // read back as Zero: a NaN would have reached a database column as 0 with nothing raised.
        // These two are the door the adapters build on, so they refuse rather than answer.
        foreach (var value in new[] { BigDecimal.NaN, BigDecimal.PositiveInfinity, BigDecimal.NegativeInfinity })
        {
            var words = new ulong[4];
            var getWords = () => value.GetWords(words, out _, out _);
            var getMantissa = () => value.GetMantissa();

            getWords.Should().Throw<InvalidOperationException>();
            getMantissa.Should().Throw<InvalidOperationException>();
        }

        // The finite round trip is untouched.
        var finite = BigDecimal.Parse("-123.4500", CultureInfo.InvariantCulture);
        var buffer = new ulong[4];
        var length = finite.GetWords(buffer, out var negative, out var scale);
        length.Should().BePositive();
        BigDecimal.FromWords(buffer, negative, scale).Should().Be(finite);
        BigDecimal.FromScaled(finite.GetMantissa(), finite.Scale).Should().Be(finite);
    }

    [Fact]
    public void Clamp_AnswersNaNForANaNBoundAsWellAsForANaNValue()
    {
        // A NaN bound leaves min > max false, so it raises nothing on its own, and without this
        // the value came back as though the range had been checked.
        var five = BigDecimal.CreateChecked(5);

        BigDecimal.IsNaN(BigDecimal.Clamp(five, BigDecimal.NaN, BigDecimal.CreateChecked(10))).Should().BeTrue();
        BigDecimal.IsNaN(BigDecimal.Clamp(five, BigDecimal.Zero, BigDecimal.NaN)).Should().BeTrue();
        BigDecimal.IsNaN(BigDecimal.Clamp(five, BigDecimal.NaN, BigDecimal.NaN)).Should().BeTrue();

        // Not cross-checked against double: double.Clamp(5, double.NaN, 10) is NaN on net10.0 and
        // 5 on net8.0 and net9.0, so the oracle has two answers. The constant above is the
        // contract, on every framework - a cross-check against double can be one against a version.
    }

    [Fact]
    public void ScaleAndPrecision_AreZeroByDecisionRatherThanByParity()
    {
        // double has neither property, so there is nothing to follow here and the answer is this
        // type's own. Written down so it cannot drift into being read as parity with something.
        foreach (var value in new[] { BigDecimal.NaN, BigDecimal.PositiveInfinity, BigDecimal.NegativeInfinity })
        {
            value.Scale.Should().Be(0);
            value.Precision.Should().Be(0);
        }
    }
}
