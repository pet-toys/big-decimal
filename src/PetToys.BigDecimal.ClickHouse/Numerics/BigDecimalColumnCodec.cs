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
/// <para>
/// This is where the whole contract of the package lives, and it is deliberately small: the driver
/// exposes no payload-level extension point, so the only shape a hook ever sees is a value that has
/// already been decoded into a <see cref="BigInteger"/> mantissa and a scale.
/// </para>
/// <para>
/// The two directions are not symmetric and use different machinery on purpose. Reading is a
/// mantissa and a scale becoming a value, which <see cref="BigDecimal.FromScaled"/> already is;
/// routing it through the byte codec would decompose and recompose the same number for no gain.
/// Writing has real work in it - a rescale, a range check and a refusal - and every part of that is
/// already written and specified in <see cref="ClickHouseDecimal"/>, so this goes through the codec
/// rather than beside it.
/// </para>
/// <para>
/// The driver's decimal and the core's codec are both called <c>ClickHouseDecimal</c>. In this
/// namespace the unqualified name is the codec, and the driver's is aliased to
/// <c>DriverDecimal</c>.
/// </para>
/// </remarks>
internal static class BigDecimalColumnCodec
{
    // 10^0 to 10^76, so the precision check does not build a power per value. Filled once.
    private static readonly BigInteger[] PowersOfTen = BuildPowersOfTen();

    /// <summary>Converts a value the driver read from a column.</summary>
    /// <param name="value">The driver's decimal.</param>
    /// <returns>The same number, exactly.</returns>
    /// <exception cref="OverflowException">
    /// The mantissa is wider than 256 bits, which no ClickHouse column can produce.
    /// </exception>
    /// <remarks>
    /// Reading is total for anything a server can send: ClickHouse allows at most 76 digits of
    /// precision and a scale of at most 76, inside this type's 77 significant digits and its scale
    /// of 255. The overflow above is reachable only from a value composed by hand, and it is left
    /// reachable rather than asserted away, because the alternative to raising there is silently
    /// truncating a number.
    /// </remarks>
    internal static BigDecimal FromColumn(DriverDecimal value) =>
        BigDecimal.FromScaled(value.Mantissa, value.Scale);

    /// <summary>Converts every element of an array the driver read from a column.</summary>
    /// <param name="values">The driver's decimals.</param>
    /// <returns>The same numbers, exactly, in the same order.</returns>
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
    /// <para>
    /// The rescale is the codec's, which rounds half to even. That matters because the two parties
    /// this package sits between do not: the driver truncates toward zero when it lowers a scale,
    /// and so does the server when it parses decimal text into a narrower column. Handing either of
    /// them a value already at the column's scale is what keeps one rounding rule across the
    /// package.
    /// </para>
    /// <para>
    /// The codec checks the width and says so in the type's own terms, which are the wrong terms
    /// here: the boundary crossed is the column's, not the type's. Both failures are re-raised with
    /// the same type, naming the column and its declared form, and carrying the original as the
    /// inner exception.
    /// </para>
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

        // The precision is the caller's to enforce, which the codec says in as many words: it is a
        // property of the declared type rather than of the payload, and the two bounds do not
        // subsume each other. A mantissa of 9e18 fits the eight bytes of a Decimal64 and exceeds
        // the eighteen digits of Decimal64(2), where the server answers "Too many digits".
        if (BigInteger.Abs(mantissa) >= PowersOfTen[type.Precision])
        {
            throw new OverflowException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Describe(target, type)} cannot hold this value: at scale {type.Scale} it has more digits than the declared precision of {type.Precision}."));
        }

        return new DriverDecimal(mantissa, type.Scale);
    }

    /// <summary>Renders a value as the decimal text a query parameter carries.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="type">The column's declared type.</param>
    /// <param name="target">The parameter the value is aimed at, for the message.</param>
    /// <returns>The value at the column's scale, in plain decimal notation.</returns>
    /// <exception cref="OverflowException">
    /// The value at the column's scale is outside the column's width or its declared precision.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <paramref name="value"/> is NaN or an infinity, which no ClickHouse decimal represents.
    /// </exception>
    /// <remarks>
    /// The text is composed here rather than by either decimal type's own formatter. It has to be
    /// invariant, because it is spliced into a statement where a comma would be a second argument,
    /// and it has to be plain rather than exponential, because the server parses it as a decimal
    /// literal. Composing it from the mantissa and the scale is both by construction.
    /// </remarks>
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

    /// <summary>Names the target and its type the way a message should read.</summary>
    /// <param name="target">The column or parameter name.</param>
    /// <param name="type">Its declared type.</param>
    /// <returns>The phrase both failures open with.</returns>
    private static string Describe(string target, ClickHouseColumnType type) =>
        string.Create(CultureInfo.InvariantCulture, $"'{target}' of type {type.Declared}");

    /// <summary>Builds the powers of ten the precision check compares against.</summary>
    /// <returns>10 to the power of each index, 0 to the largest precision ClickHouse allows.</returns>
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
