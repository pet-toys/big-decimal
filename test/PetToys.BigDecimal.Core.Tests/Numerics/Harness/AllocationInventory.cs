using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace PetToys.BigDecimal.Numerics.Harness;

/// <summary>
/// The list of operations held to the zero-allocation guarantee, and the list of members excused
/// from it with a reason.
/// </summary>
/// <remarks>
/// <para>
/// A blanket claim needs a blanket check. Every entry here is executed inside a measured window;
/// a companion test walks the public surface of <see cref="BigDecimal"/> by reflection and fails
/// when a member appears in neither list, so that public surface added by a later change has to be
/// classified rather than quietly assumed.
/// </para>
/// <para>
/// The reflection check is keyed on member names, which is what reflection can compare against a
/// hand-written list. Overloads whose behaviour differs — parsing from characters and from UTF-8,
/// division with and without an explicit scale — carry their own entries rather than relying on a
/// sibling's.
/// </para>
/// </remarks>
public static class AllocationInventory
{
    private static readonly BigDecimal Left = BigDecimal.Parse("123456789.987654321", CultureInfo.InvariantCulture);
    private static readonly BigDecimal Right = BigDecimal.Parse("-98765.4321", CultureInfo.InvariantCulture);
    private static readonly BigDecimal Wide = BigDecimal.Parse("0." + new string('9', 200), CultureInfo.InvariantCulture);
    private static readonly BigInteger Mantissa = new(1234567890123456789L);
    private static readonly string Text = "123456789.987654321";
    private static readonly string LongText = "0." + new string('7', 500);
    private static readonly string TooLarge = "1" + new string('0', 78);
    private static readonly byte[] Utf8 = Encoding.UTF8.GetBytes("123456789.987654321");
    private static readonly byte[] LongUtf8 = Encoding.UTF8.GetBytes("0." + new string('7', 500));
    private static readonly char[] CharBuffer = new char[1024];
    private static readonly byte[] ByteBuffer = new byte[1024];
    private static readonly ulong[] Words = [3, 5, 7, 11];
    private static readonly ulong[] WordDestination = new ulong[4];
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly CultureInfo Grouping = CultureMatrix.Get(CultureCase.NonUniformGroups);

    private static readonly BigDecimal LongInteger =
        BigDecimal.Parse("1234567890123456789012345678901234567890.12", CultureInfo.InvariantCulture);

    private static readonly BigDecimal Narrow = BigDecimal.Parse("1.23", CultureInfo.InvariantCulture);
    private static readonly byte[] PostgresBuffer = new byte[PostgresNumeric.MaxByteCount];
    private static readonly byte[] PostgresPayload = Encode(Left);
    private static readonly byte[] PostgresNonFinitePayload = Encode(BigDecimal.NaN);
    private static readonly byte[] PostgresDeepPayload = Fraction(4096);
    private static readonly byte[] ClickHouseBuffer = new byte[ClickHouseDecimal.Decimal256Size];

    private static readonly List<string> Order = [];
    private static readonly Dictionary<string, (string Member, Action Operation)> Entries = Build();

    /// <summary>The labels of every covered operation, in the order they were declared.</summary>
    public static IReadOnlyList<string> Labels { get; } = Order;

    /// <summary>
    /// The members excused from the guarantee, each with the reason its signature makes an
    /// allocation unavoidable.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Exclusions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ToString"] = "Returns a freshly allocated string by contract.",
        ["GetMantissa"] = "Returns a BigInteger, which owns heap storage for anything past one machine word.",
    };

    /// <summary>
    /// Every place the package rents from a pool instead of allocating, and the threshold above
    /// which it does.
    /// </summary>
    /// <remarks>
    /// A rent is not an exception to the guarantee: the entries here are measured at zero like
    /// everything else, because a warm pool hands back a buffer it already owns. It is listed so
    /// that a new rent has to be declared rather than noticed, and so that the threshold is
    /// written down where it can be argued with rather than only in the code that chose it.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> PooledBuffers { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TryFormat to UTF-8"] =
                "Formats through a char buffer sized from the computed length: on the stack up to 512 "
                + "characters, rented above it. 512 covers every standard specifier at any precision a "
                + "culture supplies, so only an explicit precision or a long custom format reaches the pool.",
            ["ToString"] =
                "Writes into the string it returns through string.Create, so the string is the only "
                + "allocation and nothing is rented at all.",
            ["Parse from UTF-8"] =
                "Transcodes into a rented char buffer when the input is longer than the stack bound.",
            ["BigDecimalJsonConverter.Read"] =
                "Rents a byte buffer for a token longer than 365 bytes rather than sizing a fixed one.",
        };

    /// <summary>The member names the inventory covers.</summary>
    public static IReadOnlySet<string> CoveredMembers { get; } =
        new HashSet<string>(EnumerateMembers(), StringComparer.Ordinal);

    /// <summary>Looks up a covered operation by its label.</summary>
    /// <param name="label">The label, from <see cref="Labels"/>.</param>
    /// <returns>The operation to measure.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The label is not in the inventory.</exception>
    public static Action Operation(string label) => Entries.TryGetValue(label, out var entry)
        ? entry.Operation
        : throw new ArgumentOutOfRangeException(nameof(label), label, "Not in the inventory.");

    // Written out so that the entries above invoke the increment and decrement operators
    // themselves rather than the addition and subtraction they are built on.
    private static BigDecimal Increment(BigDecimal value) => ++value;

    private static BigDecimal Decrement(BigDecimal value) => --value;

    // char implements INumberBase explicitly, so its Create* methods are reachable only through a
    // type parameter. That is the shape this change removed from BigDecimal itself.
    private static T Saturating<T, TOther>(TOther value)
        where T : INumberBase<T>
        where TOther : INumberBase<TOther> => T.CreateSaturating(value);

    private static byte[] Encode(BigDecimal value)
    {
        var payload = new byte[PostgresNumeric.MaxByteCount];
        PostgresNumeric.TryWrite(value, payload, out var written);

        return payload[..written];
    }

    /// <summary>
    /// A payload of the given number of base-10000 groups, all of them below the point, laid out
    /// directly rather than decomposed from a number of the same width.
    /// </summary>
    private static byte[] Fraction(int count)
    {
        var groups = new ushort[count];
        Array.Fill(groups, (ushort)1);

        return WireFormatOracle.PostgresLayout(
            count,
            weight: -1,
            WireFormatOracle.PostgresPositive,
            count * WireFormatOracle.PostgresDecDigits,
            groups);
    }

    private static IEnumerable<string> EnumerateMembers()
    {
        foreach (var entry in Entries.Values)
        {
            yield return entry.Member;
        }
    }

    private static Dictionary<string, (string, Action)> Build()
    {
        var entries = new Dictionary<string, (string, Action)>(StringComparer.Ordinal);

        void Add(string label, string member, Action operation)
        {
            entries.Add(label, (member, operation));
            Order.Add(label);
        }

        Add("operator +", "op_Addition", () => Allocations.Sink = Left + Right);
        Add("operator -", "op_Subtraction", () => Allocations.Sink = Left - Right);
        Add("operator *", "op_Multiply", () => Allocations.Sink = Left * Right);
        Add("operator /", "op_Division", () => Allocations.Sink = Left / Right);
        Add("operator %", "op_Modulus", () => Allocations.Sink = Left % Right);
        Add("operator unary -", "op_UnaryNegation", () => Allocations.Sink = -Left);
        Add("operator unary +", "op_UnaryPlus", () => Allocations.Sink = +Left);
        Add("operator ++", "op_Increment", () => Allocations.Sink = Increment(Left));
        Add("operator --", "op_Decrement", () => Allocations.Sink = Decrement(Left));

        Add("Add", "Add", () => Allocations.Sink = BigDecimal.Add(Left, Right));
        Add("Subtract", "Subtract", () => Allocations.Sink = BigDecimal.Subtract(Left, Right));
        Add("Multiply", "Multiply", () => Allocations.Sink = BigDecimal.Multiply(Left, Right));
        Add("Divide", "Divide", () => Allocations.Sink = BigDecimal.Divide(Left, Right));
        Add("Divide at a scale", "Divide", () => Allocations.Sink = BigDecimal.Divide(Left, Right, 20, MidpointRounding.ToEven));
        Add("Remainder", "Remainder", () => Allocations.Sink = BigDecimal.Remainder(Left, Right));

        // Four entries rather than one: the exponent selects between four paths that allocate for
        // different reasons if they allocate at all - the chain, the chain followed by a division
        // at the working width, the constant that never reads the value, and the flags-only answer.
        Add("Pow", "Pow", () => Allocations.Sink = BigDecimal.Pow(Left, 7));
        Add("Pow at a negative exponent", "Pow", () => Allocations.Sink = BigDecimal.Pow(Left, -7));
        Add("Pow at an exponent of zero", "Pow", () => Allocations.Sink = BigDecimal.Pow(Left, 0));
        Add("Pow of a non-finite value", "Pow", () => Allocations.Sink = BigDecimal.Pow(BigDecimal.NaN, 3));
        // The wire codecs are internal, so the reflection sweep does not reach them: an entry
        // missing here is a silence rather than a failure. Each direction of each format is its
        // own entry, and each ClickHouse width is its own again, because the widths are not
        // interchangeable - four and eight bytes are one machine word and sixteen and thirty-two
        // are not.
        Add("PostgresNumeric.GetByteCount", "PostgresNumeric", () => Allocations.OtherSink = PostgresNumeric.GetByteCount(Left));
        Add("PostgresNumeric.TryWrite", "PostgresNumeric", () => Allocations.OtherSink = PostgresNumeric.TryWrite(Left, PostgresBuffer, out _) ? 1 : 0);
        Add("PostgresNumeric.TryWrite of a non-finite value", "PostgresNumeric", () => Allocations.OtherSink = PostgresNumeric.TryWrite(BigDecimal.NaN, PostgresBuffer, out _) ? 1 : 0);
        Add("PostgresNumeric.Read", "PostgresNumeric", () => Allocations.Sink = PostgresNumeric.Read(PostgresPayload));
        Add("PostgresNumeric.Read of a non-finite value", "PostgresNumeric", () => Allocations.Sink = PostgresNumeric.Read(PostgresNonFinitePayload));

        // The one entry that fails if a reader ever sizes anything from ndigits: a payload at
        // PostgreSQL's own limit of fractional digits, thousands of times wider than the result.
        Add("PostgresNumeric.Read at PostgreSQL's fractional limit", "PostgresNumeric", () => Allocations.Sink = PostgresNumeric.Read(PostgresDeepPayload));

        Add("ClickHouseDecimal.Write at Decimal32", "ClickHouseDecimal", () => ClickHouseDecimal.Write(Narrow, 2, ClickHouseBuffer.AsSpan(0, ClickHouseDecimal.Decimal32Size)));
        Add("ClickHouseDecimal.Write at Decimal64", "ClickHouseDecimal", () => ClickHouseDecimal.Write(Narrow, 2, ClickHouseBuffer.AsSpan(0, ClickHouseDecimal.Decimal64Size)));
        Add("ClickHouseDecimal.Write at Decimal128", "ClickHouseDecimal", () => ClickHouseDecimal.Write(Left, 9, ClickHouseBuffer.AsSpan(0, ClickHouseDecimal.Decimal128Size)));
        Add("ClickHouseDecimal.Write at Decimal256", "ClickHouseDecimal", () => ClickHouseDecimal.Write(Left, 9, ClickHouseBuffer.AsSpan(0, ClickHouseDecimal.Decimal256Size)));
        Add("ClickHouseDecimal.Read at Decimal32", "ClickHouseDecimal", () => Allocations.Sink = ClickHouseDecimal.Read(ClickHouseBuffer.AsSpan(0, ClickHouseDecimal.Decimal32Size), 2));
        Add("ClickHouseDecimal.Read at Decimal64", "ClickHouseDecimal", () => Allocations.Sink = ClickHouseDecimal.Read(ClickHouseBuffer.AsSpan(0, ClickHouseDecimal.Decimal64Size), 2));
        Add("ClickHouseDecimal.Read at Decimal128", "ClickHouseDecimal", () => Allocations.Sink = ClickHouseDecimal.Read(ClickHouseBuffer.AsSpan(0, ClickHouseDecimal.Decimal128Size), 9));
        Add("ClickHouseDecimal.Read at Decimal256", "ClickHouseDecimal", () => Allocations.Sink = ClickHouseDecimal.Read(ClickHouseBuffer.AsSpan(0, ClickHouseDecimal.Decimal256Size), 9));

        Add("Negate", "Negate", () => Allocations.Sink = BigDecimal.Negate(Left));
        Add("Abs", "Abs", () => Allocations.Sink = BigDecimal.Abs(Right));
        Add("Floor", "Floor", () => Allocations.Sink = BigDecimal.Floor(Left));
        Add("Ceiling", "Ceiling", () => Allocations.Sink = BigDecimal.Ceiling(Left));
        Add("Truncate", "Truncate", () => Allocations.Sink = BigDecimal.Truncate(Left));
        Add("Min", "Min", () => Allocations.Sink = BigDecimal.Min(Left, Right));
        Add("Max", "Max", () => Allocations.Sink = BigDecimal.Max(Left, Right));
        Add("Clamp", "Clamp", () => Allocations.Sink = BigDecimal.Clamp(Left, Right, BigDecimal.MaxValue));

        Add("Round", "Round", () => Allocations.Sink = BigDecimal.Round(Left));
        Add("Round to a scale", "Round", () => Allocations.Sink = BigDecimal.Round(Left, 3));
        Add("Round with a mode", "Round", () => Allocations.Sink = BigDecimal.Round(Left, MidpointRounding.AwayFromZero));
        Add("Round to a scale with a mode", "Round", () => Allocations.Sink = BigDecimal.Round(Left, 3, MidpointRounding.ToZero));
        Add("WithScale", "WithScale", () => Allocations.Sink = Left.WithScale(24));
        Add("WithScale with a mode", "WithScale", () => Allocations.Sink = Left.WithScale(3, MidpointRounding.ToZero));

        Add("operator ==", "op_Equality", () => Allocations.OtherSink = (Left == Right) ? 1 : 0);
        Add("operator !=", "op_Inequality", () => Allocations.OtherSink = (Left != Right) ? 1 : 0);
        Add("operator <", "op_LessThan", () => Allocations.OtherSink = (Left < Right) ? 1 : 0);
        Add("operator <=", "op_LessThanOrEqual", () => Allocations.OtherSink = (Left <= Right) ? 1 : 0);
        Add("operator >", "op_GreaterThan", () => Allocations.OtherSink = (Left > Right) ? 1 : 0);
        Add("operator >=", "op_GreaterThanOrEqual", () => Allocations.OtherSink = (Left >= Right) ? 1 : 0);
        Add("Equals", "Equals", () => Allocations.OtherSink = Left.Equals(Right) ? 1 : 0);
        Add("CompareTo", "CompareTo", () => Allocations.OtherSink = Left.CompareTo(Right));
        Add("GetHashCode", "GetHashCode", () => Allocations.OtherSink = Left.GetHashCode());

        Add("Zero", "Zero", () => Allocations.Sink = BigDecimal.Zero);
        Add("One", "One", () => Allocations.Sink = BigDecimal.One);
        Add("NegativeOne", "NegativeOne", () => Allocations.Sink = BigDecimal.NegativeOne);
        Add("MaxValue", "MaxValue", () => Allocations.Sink = BigDecimal.MaxValue);
        Add("MinValue", "MinValue", () => Allocations.Sink = BigDecimal.MinValue);
        Add("Scale", "Scale", () => Allocations.OtherSink = Left.Scale);
        Add("Sign", "Sign", () => Allocations.OtherSink = Left.Sign);
        Add("IsZero", "IsZero", () => Allocations.OtherSink = Left.IsZero ? 1 : 0);
        Add("NaN", "NaN", () => Allocations.Sink = BigDecimal.NaN);
        Add("PositiveInfinity", "PositiveInfinity", () => Allocations.Sink = BigDecimal.PositiveInfinity);
        Add("NegativeInfinity", "NegativeInfinity", () => Allocations.Sink = BigDecimal.NegativeInfinity);
        Add("IsNaN", "IsNaN", () => Allocations.OtherSink = BigDecimal.IsNaN(Left) ? 1 : 0);
        Add("IsInfinity", "IsInfinity", () => Allocations.OtherSink = BigDecimal.IsInfinity(Left) ? 1 : 0);
        Add("IsPositiveInfinity", "IsPositiveInfinity", () => Allocations.OtherSink = BigDecimal.IsPositiveInfinity(Left) ? 1 : 0);
        Add("IsNegativeInfinity", "IsNegativeInfinity", () => Allocations.OtherSink = BigDecimal.IsNegativeInfinity(Left) ? 1 : 0);
        Add("IsFinite", "IsFinite", () => Allocations.OtherSink = BigDecimal.IsFinite(Left) ? 1 : 0);
        Add("IsNegative", "IsNegative", () => Allocations.OtherSink = Left.IsNegative ? 1 : 0);
        Add("MaxScale", "MaxScale", () => Allocations.OtherSink = BigDecimal.MaxScale);

        Add("FromWords", "FromWords", () => Allocations.Sink = BigDecimal.FromWords(Words, false, 7));
        Add("GetWords", "GetWords", () => Allocations.OtherSink = Left.GetWords(WordDestination, out _, out _));
        Add("FromScaled", "FromScaled", () => Allocations.Sink = BigDecimal.FromScaled(Mantissa, 9));

        Add("conversion from long", "op_Implicit", () => Allocations.Sink = 1234567890123456789L);
        Add("conversion from decimal", "op_Implicit", () => Allocations.Sink = 123456.789m);
        Add("conversion to decimal", "op_Explicit", () => Allocations.OtherSink = (long)(decimal)Left);
        Add("conversion to double", "op_Explicit", () => Allocations.OtherSink = (long)(double)Left);
        Add("conversion to long", "op_Explicit", () => Allocations.OtherSink = (long)Left);
        Add("conversion from double", "op_Explicit", () => Allocations.Sink = (BigDecimal)0.1);
        Add("conversion from float", "op_Explicit", () => Allocations.Sink = (BigDecimal)0.1f);
        Add("Precision", "Precision", () => Allocations.OtherSink = Left.Precision);

        // One entry per conversion type, in both directions. The member name op_Explicit stands
        // for sixteen conversions and the generic ones are reached through a type parameter rather
        // than through a name at all, so the family counted as covered while converting out of the
        // type boxed 24 bytes for a long target and 32 for a decimal one. The same hole as the
        // format specifier, with a type argument in place of a value.
        Add("CreateChecked from long", "CreateChecked", () => Allocations.Sink = BigDecimal.CreateChecked(1234567890123456789L));
        Add("CreateChecked from double", "CreateChecked", () => Allocations.Sink = BigDecimal.CreateChecked(0.1));
        Add("CreateSaturating from decimal", "CreateSaturating", () => Allocations.Sink = BigDecimal.CreateSaturating(123456.789m));
        Add("CreateSaturating from double", "CreateSaturating", () => Allocations.Sink = BigDecimal.CreateSaturating(double.MaxValue));
        Add("CreateSaturating from Half", "CreateSaturating", () => Allocations.Sink = BigDecimal.CreateSaturating((Half)1.5f));
        Add("CreateSaturating from NFloat", "CreateSaturating", () => Allocations.Sink = BigDecimal.CreateSaturating((NFloat)1.5));
        Add("CreateSaturating from char", "CreateSaturating", () => Allocations.Sink = BigDecimal.CreateSaturating('A'));
        Add("CreateTruncating from Int128", "CreateTruncating", () => Allocations.Sink = BigDecimal.CreateTruncating(Int128.MaxValue));
        Add("CreateTruncating from BigInteger", "CreateTruncating", () => Allocations.Sink = BigDecimal.CreateTruncating(Mantissa));

        Add("conversion to byte", "TryConvertToSaturating", () => Allocations.OtherSink = byte.CreateSaturating(Left));
        Add("conversion to sbyte", "TryConvertToSaturating", () => Allocations.OtherSink = sbyte.CreateSaturating(Left));
        Add("conversion to short", "TryConvertToSaturating", () => Allocations.OtherSink = short.CreateSaturating(Left));
        Add("conversion to ushort", "TryConvertToSaturating", () => Allocations.OtherSink = ushort.CreateSaturating(Left));
        Add("conversion to int", "TryConvertToSaturating", () => Allocations.OtherSink = int.CreateSaturating(Left));
        Add("conversion to uint", "TryConvertToSaturating", () => Allocations.OtherSink = uint.CreateSaturating(Left));
        Add("conversion to long, generic", "TryConvertToSaturating", () => Allocations.OtherSink = long.CreateSaturating(Left));
        Add("conversion to ulong", "TryConvertToSaturating", () => Allocations.OtherSink = (long)ulong.CreateSaturating(Left));
        Add("conversion to char", "TryConvertToSaturating", () => Allocations.OtherSink = Saturating<char, BigDecimal>(Left));
        Add("conversion to nint", "TryConvertToSaturating", () => Allocations.OtherSink = nint.CreateSaturating(Left));
        Add("conversion to nuint", "TryConvertToSaturating", () => Allocations.OtherSink = (long)nuint.CreateSaturating(Left));
        Add("conversion to Int128", "TryConvertToSaturating", () => Allocations.OtherSink = (long)Int128.CreateSaturating(Left));
        Add("conversion to UInt128", "TryConvertToSaturating", () => Allocations.OtherSink = (long)UInt128.CreateSaturating(Left));
        Add("conversion to decimal, generic", "TryConvertToSaturating", () => Allocations.OtherSink = (long)decimal.CreateSaturating(Left));
        Add("conversion to double, generic", "TryConvertToSaturating", () => Allocations.OtherSink = (long)double.CreateSaturating(Left));
        Add("conversion to float, generic", "TryConvertToSaturating", () => Allocations.OtherSink = (long)float.CreateSaturating(Left));
        Add("conversion to Half", "TryConvertToSaturating", () => Allocations.OtherSink = BitConverter.HalfToInt16Bits(Half.CreateSaturating(Left)));
        Add("conversion to NFloat", "TryConvertToSaturating", () => Allocations.OtherSink = (long)(double)NFloat.CreateSaturating(Left));
        // Measured on a value inside one machine word on purpose: past that a BigInteger owns
        // heap storage by contract, which is why GetMantissa is excused outright. What this
        // entry holds to zero is the conversion itself, not the result type.
        Add("conversion to BigInteger", "TryConvertToSaturating", () => Allocations.OtherSink = (long)BigInteger.CreateSaturating(Left));
        Add("conversion to BigDecimal", "TryConvertToSaturating", () => Allocations.Sink = BigDecimal.CreateSaturating(Left));
        Add("conversion to long, checked", "TryConvertToChecked", () => Allocations.OtherSink = long.CreateChecked(Left));
        Add("conversion to decimal, checked", "TryConvertToChecked", () => Allocations.OtherSink = (long)decimal.CreateChecked(Left));

        Add("Parse from chars", "Parse", () => Allocations.Sink = BigDecimal.Parse(Text, Invariant));
        Add("Parse from UTF-8", "Parse", () => Allocations.Sink = BigDecimal.Parse(LongUtf8, Invariant));
        Add("TryParse from chars", "TryParse", () => Allocations.OtherSink = BigDecimal.TryParse(Text, Invariant, out var value) ? value.Scale : -1);
        Add("TryParse from UTF-8", "TryParse", () => Allocations.OtherSink = BigDecimal.TryParse(Utf8, Invariant, out var value) ? value.Scale : -1);
        Add("Parse a long value from chars", "Parse", () => Allocations.Sink = BigDecimal.Parse(LongText, Invariant));
        Add("TryParse a value that does not fit", "TryParse", () => Allocations.OtherSink = BigDecimal.TryParse(TooLarge, Invariant, out var value) ? value.Scale : -1);

        // One entry per format string the corpus carries, on both overloads. Covering TryFormat
        // once with whatever format was convenient is what let grouped formatting allocate 64 bytes
        // per call through two changes and a full benchmark run: every entry used the default
        // format, so the whole grouped path sat outside the inventory. A custom format string is
        // not a specifier and no rule about specifiers reaches it, so the corpus covers both.
        foreach (var format in FormatCorpus.All)
        {
            var current = format;
            Add(
                $"TryFormat to chars, \"{current}\"",
                "TryFormat",
                () => Allocations.OtherSink = Left.TryFormat(CharBuffer, out var written, current, Invariant) ? written : -1);
            Add(
                $"TryFormat to UTF-8, \"{current}\"",
                "TryFormat",
                () => Allocations.OtherSink = Left.TryFormat(ByteBuffer, out var written, current, Invariant) ? written : -1);
        }

        Add("TryFormat a wide value", "TryFormat", () => Allocations.OtherSink = Wide.TryFormat(CharBuffer, out var written, default, Invariant) ? written : -1);
        Add("TryFormat to chars, N2 grouped", "TryFormat", () => Allocations.OtherSink = Left.TryFormat(CharBuffer, out var written, "N2", Grouping) ? written : -1);
        Add("TryFormat to UTF-8, N2 grouped", "TryFormat", () => Allocations.OtherSink = Left.TryFormat(ByteBuffer, out var written, "N2", Grouping) ? written : -1);
        Add("TryFormat N0 over many groups", "TryFormat", () => Allocations.OtherSink = LongInteger.TryFormat(CharBuffer, out var written, "N0", Grouping) ? written : -1);
        Add("TryFormat a grouped custom format", "TryFormat", () => Allocations.OtherSink = LongInteger.TryFormat(CharBuffer, out var written, "#,##0.00", Grouping) ? written : -1);

        return entries;
    }
}
