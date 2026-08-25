using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
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
    private static readonly byte[] Utf8 = Encoding.UTF8.GetBytes("123456789.987654321");
    private static readonly byte[] LongUtf8 = Encoding.UTF8.GetBytes("0." + new string('7', 500));
    private static readonly char[] CharBuffer = new char[1024];
    private static readonly byte[] ByteBuffer = new byte[1024];
    private static readonly ulong[] Words = [3, 5, 7, 11];
    private static readonly ulong[] WordDestination = new ulong[4];
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

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

        Add("Parse from chars", "Parse", () => Allocations.Sink = BigDecimal.Parse(Text, Invariant));
        Add("Parse from UTF-8", "Parse", () => Allocations.Sink = BigDecimal.Parse(LongUtf8, Invariant));
        Add("TryParse from chars", "TryParse", () => Allocations.OtherSink = BigDecimal.TryParse(Text, Invariant, out var value) ? value.Scale : -1);
        Add("TryParse from UTF-8", "TryParse", () => Allocations.OtherSink = BigDecimal.TryParse(Utf8, Invariant, out var value) ? value.Scale : -1);
        Add("Parse a long value from chars", "Parse", () => Allocations.Sink = BigDecimal.Parse(LongText, Invariant));

        Add("TryFormat to chars", "TryFormat", () => Allocations.OtherSink = Left.TryFormat(CharBuffer, out var written, default, Invariant) ? written : -1);
        Add("TryFormat to UTF-8", "TryFormat", () => Allocations.OtherSink = Left.TryFormat(ByteBuffer, out var written, default, Invariant) ? written : -1);
        Add("TryFormat a wide value", "TryFormat", () => Allocations.OtherSink = Wide.TryFormat(CharBuffer, out var written, default, Invariant) ? written : -1);

        return entries;
    }
}
