using System;
using System.Numerics;
using AwesomeAssertions;
using Xunit;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// What the codec refuses to put on the wire, asserted where a reader of the wire suite looks for
/// it. No server takes part: these are the cases where nothing is ever sent.
/// </summary>
/// <remarks>
/// These cases are also what keeps this project out of the zero-tests report on the legs that
/// exclude the integration category.
/// </remarks>
public sealed class ClickHouseDecimalRefusalTests
{
    public static TheoryData<int> Widths =>
    [
        ClickHouseDecimal.Decimal32Size,
        ClickHouseDecimal.Decimal64Size,
        ClickHouseDecimal.Decimal128Size,
        ClickHouseDecimal.Decimal256Size,
    ];

    [Theory]
    [MemberData(nameof(Widths))]
    public void AValueBeyondTheWidth_IsRefusedBeforeAnythingIsSent(int width)
    {
        // The largest two's complement value of the width, and the first one past it.
        var largest = (BigInteger.One << ((width * 8) - 1)) - BigInteger.One;

        var destination = new byte[width];

        var fits = () => ClickHouseDecimal.Write(BigDecimal.FromScaled(largest, 0), 0, destination);
        var beyond = () => ClickHouseDecimal.Write(BigDecimal.FromScaled(largest + BigInteger.One, 0), 0, destination);

        fits.Should().NotThrow("the boundary itself is inside the width");
        beyond.Should().Throw<OverflowException>("one unit past the boundary is not");
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public void ANonFiniteValue_IsRefusedByName(int width)
    {
        var destination = new byte[width];

        var column = width switch
        {
            ClickHouseDecimal.Decimal32Size => "Decimal32",
            ClickHouseDecimal.Decimal64Size => "Decimal64",
            ClickHouseDecimal.Decimal128Size => "Decimal128",
            _ => "Decimal256",
        };

        foreach (var (value, name) in new[]
        {
            (BigDecimal.NaN, "NaN"),
            (BigDecimal.PositiveInfinity, "Infinity"),
            (BigDecimal.NegativeInfinity, "-Infinity"),
        })
        {
            var written = () => ClickHouseDecimal.Write(value, 0, destination);

            written.Should().Throw<NotSupportedException>()
                .WithMessage($"*{name}*", "the message names which of the three it was")
                .WithMessage($"*{column}*", "and the width it was targeted at");
        }
    }
}
