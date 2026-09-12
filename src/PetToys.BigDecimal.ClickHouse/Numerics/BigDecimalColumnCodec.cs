using System;
using System.Globalization;
using System.Numerics;
using DriverDecimal = ClickHouse.Driver.Numerics.ClickHouseDecimal;

namespace PetToys.BigDecimal.Numerics;

/// <summary>
/// Converts between <see cref="BigDecimal"/> and the arbitrary-precision decimal the driver hands
/// to and takes from a hook.
/// </summary>
/// <remarks>
/// A hook only ever sees a value already decoded into a <see cref="BigInteger"/> mantissa and a
/// scale, so reading is <see cref="BigDecimal.FromScaled"/>, while writing goes through
/// <see cref="ClickHouseDecimal"/> for its rescale, range check and refusal. The driver's decimal
/// is aliased to <c>DriverDecimal</c>; the unqualified name is the core's codec.
/// </remarks>
internal static class BigDecimalColumnCodec
{
    private static readonly BigInteger[] PowersOfTen = BuildPowersOfTen();

    /// <summary>Converts a value the driver read from a column, exactly.</summary>
    /// <exception cref="OverflowException">
    /// The mantissa is wider than 256 bits, which no ClickHouse column can produce.
    /// </exception>
    internal static BigDecimal FromColumn(DriverDecimal value) =>
        BigDecimal.FromScaled(value.Mantissa, value.Scale);

    internal static BigDecimal[] FromColumn(DriverDecimal[] values) =>
        Array.ConvertAll(values, FromColumn);

    /// <summary>Converts a value for a column, at that column's scale and width.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="type">The column's declared type.</param>
    /// <param name="target">The column or parameter the value is aimed at, for the message.</param>
    /// <returns>The driver's decimal, already at the column's scale.</returns>
    /// <exception cref="OverflowException">
    /// The value at the column's scale is outside the column's width or its declared precision.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="value"/> is NaN or an infinity, which no ClickHouse decimal represents.
    /// </exception>
    /// <remarks>
    /// The rescale is the codec's, half to even, where the driver and the server both truncate;
    /// the codec's failures are re-raised with the same type, naming the column.
    /// </remarks>
    internal static DriverDecimal ToColumn(BigDecimal value, ClickHouseColumnType type, string target)
    {
        Span<byte> payload = stackalloc byte[type.Width];

        try
        {
            ClickHouseDecimal.Write(value, type.Scale, payload);
        }
        catch (OverflowException exception)
        {
            throw new OverflowException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Describe(target, type)} cannot hold this value: at scale {type.Scale} its magnitude is outside the {type.Width} bytes the type is stored in."),
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new NotSupportedException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Describe(target, type)} cannot hold this value: {exception.Message}"),
                exception);
        }

        var mantissa = new BigInteger(payload, isUnsigned: false, isBigEndian: false);

        // Neither bound subsumes the other: 9e18 fits Decimal64's eight bytes and exceeds its
        // eighteen digits, where the server answers "Too many digits".
        if (BigInteger.Abs(mantissa) >= PowersOfTen[type.Precision])
        {
            throw new OverflowException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Describe(target, type)} cannot hold this value: at scale {type.Scale} it has more digits than the declared precision of {type.Precision}."));
        }

        return new DriverDecimal(mantissa, type.Scale);
    }

    /// <summary>Renders a value at the column's scale as the plain, invariant decimal text a query parameter carries.</summary>
    /// <exception cref="OverflowException">
    /// The value at the column's scale is outside the column's width or its declared precision.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="value"/> is NaN or an infinity, which no ClickHouse decimal represents.
    /// </exception>
    internal static string ToText(BigDecimal value, ClickHouseColumnType type, string target)
    {
        var converted = ToColumn(value, type, target);
        var digits = BigInteger.Abs(converted.Mantissa)
            .ToString(CultureInfo.InvariantCulture)
            .PadLeft(type.Scale + 1, '0');
        var sign = converted.Mantissa.Sign < 0 ? "-" : string.Empty;

        return type.Scale == 0
            ? sign + digits
            : sign + digits[..^type.Scale] + "." + digits[^type.Scale..];
    }

    private static string Describe(string target, ClickHouseColumnType type) =>
        string.Create(CultureInfo.InvariantCulture, $"'{target}' of type {type.Declared}");

    private static BigInteger[] BuildPowersOfTen()
    {
        var powers = new BigInteger[ClickHouseColumnType.MaxPrecision + 1];
        powers[0] = BigInteger.One;
        for (var i = 1; i < powers.Length; i++)
        {
            powers[i] = powers[i - 1] * 10;
        }

        return powers;
    }
}
