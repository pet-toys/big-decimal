# BigDecimal for .NET

[![Unit Test][test-badge]][test-url] [![Target frameworks][dotnet-badge]][repo-url] [![License][license-badge]][license-url]

> A decimal value type that holds what `System.Decimal` cannot: the large
> PostgreSQL `numeric` and ClickHouse `Decimal*` values, exactly, and without
> allocating.

`BigDecimal` is an allocation-free decimal value type: a 256-bit unsigned
magnitude, a sign, and a decimal scale of 0 to 255, denoting
`(-1)^sign * magnitude * 10^-scale`. The whole state lives in the struct and
the working buffers on the stack, so an operation allocates only what it hands
back. It implements [`INumber<T>`][inumber-url] and the rest of the generic
math surface, parses and formats exactly as
[`System.Decimal`][decimal-url] does, and carries a `System.Text.Json`
converter.

Where `decimal` stops at 28 to 29 significant digits, every value of at most
77 significant digits is representable here, the largest representable
magnitude has 78 digits, and the range runs from `1e-255` to roughly
`1.157e77`.

## Why

`decimal` is the right type for money in .NET right up to the moment a database
column outgrows it, and then there is nothing in the base class library to
reach for. `System.Numerics.BigInteger` has the range but no scale and no
rounding rules, `double` is not exact, and a `string` is not arithmetic.

- **Money and blockchain precision.** `numeric(38, 18)` and
  `Decimal128(18)` are the common shapes for on-chain amounts and
  high-precision ledgers. Both are past `decimal` and both fit here with room
  to spare.
- **Reading back what the database actually stored.** A `numeric` column with
  40 integer digits does not fit `decimal`, so the value arrives truncated, as
  a string, or as an exception. Mapping it to `BigDecimal` keeps the digits.
- **Analytics that must not drift.** Sums and products of exact decimal
  quantities stay exact for as long as the magnitude holds them, and round
  half to even when it does not, rather than accumulating binary error.
- **Hot paths.** No allocation on any operation, verified by a test rather than
  claimed, so the type is usable inside a per-row loop. It is 40 bytes wide
  against `decimal`'s 16, though, and copies both operands by value; see
  [what it costs](#what-it-costs) before putting it in the innermost one.

## Features

- **A real numeric type, not a wrapper.** `INumber<BigDecimal>`,
  `ISignedNumber<BigDecimal>`, `IMinMaxValue<BigDecimal>`, `IEquatable`,
  `IComparable`, the full operator set, and `CreateChecked` /
  `CreateSaturating` / `CreateTruncating` with the contracts each of them
  promises.
- **`decimal` semantics inside `decimal`'s domain.** Trailing zeros survive
  arithmetic and formatting, equality is numeric so `1.0` equals `1.00`, and
  excess fractional digits round half to even instead of throwing.
- **Formatting and parsing parity.** The `C`, `E`, `F`, `G`, `N`, `P` and `R`
  specifiers with an optional precision, custom numeric format strings, the
  culture's own group sizes and negative patterns, and the full `NumberStyles`
  set. Both the `char` and the UTF-8 overloads write the same text, bounded
  only by the destination the caller passes.
- **The values a database has and `decimal` does not.** `NaN`,
  `PositiveInfinity` and `NegativeInfinity`, which PostgreSQL `numeric` carries
  and which propagate the way `System.Double` does.
- **Exact integer powers.** `Pow(value, exponent)` is exact whenever the exact
  power is representable, where a hand-written multiplication loop rounds at
  every step.
- **Scale control in both directions.** `WithScale` pads as well as rounds, so
  a value can be presented at a column's declared scale; `Round` only narrows.
- **Zero allocations, on every operation, without exception.** The working
  buffers are on the stack instead; [what it costs](#what-it-costs) has the
  width and the depth.
- **Packaged properly.** `net8.0`, `net9.0` and `net10.0`, strong-named and
  public-signed, with Source Link and a symbol package.

## Installation

```sh
dotnet add package PetToys.BigDecimal.Core
```

The `.Core` suffix names the package, not the API. The type lives in
`PetToys.BigDecimal.Numerics`, which is also the namespace the database
integration packages put their helpers in.

```csharp
using PetToys.BigDecimal.Numerics;
```

> **Releases go to nuget.org**, so the command above is all that is needed.
> Prereleases are published to [GitHub Packages][gh-packages-url] instead: that
> feed has to be added to your `nuget.config`, and it requires a personal access
> token with `read:packages` even for a public package.

## Getting started

```csharp
using System.Globalization;
using PetToys.BigDecimal.Numerics;

var invariant = CultureInfo.InvariantCulture;

// A numeric(48, 18) value: 30 integer digits, well past decimal.
var amount = BigDecimal.Parse(
    "123456789012345678901234567890.123456789012345678", invariant);

Console.WriteLine(amount.Precision);                     // 48
Console.WriteLine(amount.Scale);                         // 18
Console.WriteLine(amount + BigDecimal.Parse("0.000000000000000001", invariant));
// 123456789012345678901234567890.123456789012345679
```

## Usage

Every example below is written against `CultureInfo.InvariantCulture`, because
parsing and formatting are culture sensitive.

### Exact decimal arithmetic

Trailing zeros are part of the value and survive the operation, exactly as they
do for `decimal`. Equality ignores them.

```csharp
BigDecimal.Parse("0.1", invariant) + BigDecimal.Parse("0.2", invariant);
// 0.3        (0.1 + 0.2 as double is 0.30000000000000004)

BigDecimal.Parse("1.10", invariant) * BigDecimal.Parse("1.00", invariant);
// 1.1000

BigDecimal.Parse("1.0", invariant) == BigDecimal.Parse("1.00", invariant);
// true, and both sides hash the same
```

Division without an explicit scale runs to the full precision the magnitude
allows, and reduces an exact quotient to its shortest scale:

```csharp
BigDecimal.Parse("100", invariant) / BigDecimal.Parse("10", invariant);
// 10

BigDecimal.Parse("1", invariant) / BigDecimal.Parse("3", invariant);
// 0.3333333333333333333333333333333333333333333333333333333333333333333333333333

BigDecimal.Divide(
    BigDecimal.Parse("1", invariant),
    BigDecimal.Parse("3", invariant),
    scale: 10,
    MidpointRounding.ToEven);
// 0.3333333333
```

### Presenting a value at a column's scale

`WithScale` is the widening counterpart to `Round`: it pads with zeros as well
as rounding, which is what writing to a fixed-scale column needs.

```csharp
var price = BigDecimal.Parse("1.5", invariant);

price.WithScale(18);                  // 1.500000000000000000, for numeric(38, 18)
BigDecimal.Round(price, 18);          // 1.5, because Round never pads

BigDecimal.Round(BigDecimal.Parse("2.345", invariant), 2);   // 2.34, ties to even
BigDecimal.Round(BigDecimal.Parse("2.355", invariant), 2);   // 2.36

BigDecimal.Parse("2.345", invariant)
    .WithScale(2, MidpointRounding.AwayFromZero);            // 2.35
```

`Precision` reports the number of digits in the unscaled mantissa, so a caller
can decide whether a value fits a column before it tries to write it:

```csharp
var value = BigDecimal.Parse("12345678901234567890.1234567890", invariant);

value.Precision;                      // 30
value.Scale;                          // 10
value.Precision - value.Scale;        // 20 integer digits
```

### Formatting

Formatting matches `decimal` string for string, including the culture's group
sizes and its negative patterns.

```csharp
var v = BigDecimal.Parse("-1234567.891", invariant);

v.ToString("N2", invariant);                              // -1,234,567.89
v.ToString("N2", CultureInfo.GetCultureInfo("de-DE"));    // -1.234.567,89
v.ToString("C2", CultureInfo.GetCultureInfo("en-US"));    // -$1,234,567.89
v.ToString("E4", invariant);                              // -1.2346E+006
v.ToString("#,##0.00", invariant);                        // -1,234,567.89
v.ToString("G", invariant);                               // -1234567.891
```

`TryFormat` writes into a caller-supplied buffer, in `char` or in UTF-8, and is
bounded only by that buffer:

```csharp
Span<char> destination = stackalloc char[64];
if (amount.TryFormat(destination, out var written, "F6", invariant))
{
    // destination[..written] is "123456789012345678901234567890.123457"
}
```

### Powers

`Pow` is exact whenever the exact power is representable, so it is safe for
compounding.

```csharp
BigDecimal.Pow(BigDecimal.Parse("1.05", invariant), 30);
// 4.321942375150662009157288198886473341473378241062164306640625
// all 61 significant digits, not a rounded 1.05 multiplied 30 times

BigDecimal.Pow(BigDecimal.Parse("10", invariant), 40);
// 10000000000000000000000000000000000000000
```

A negative exponent is the reciprocal, to the precision a division without an
explicit scale gives, and it answers wherever its own result fits even when the
power it inverts does not: `Pow(2, -300)` is a value, though 2 to the 300th is
out of range.

### NaN and the infinities

PostgreSQL `numeric` carries all three, so the type does too. They propagate as
`System.Double`'s do, and nothing finite produces one.

```csharp
BigDecimal.NaN.ToString(invariant);                 // NaN
BigDecimal.PositiveInfinity.ToString(invariant);    // Infinity
BigDecimal.Parse("NaN", invariant);                 // parses, whatever the NumberStyles

BigDecimal.One + BigDecimal.NaN;                    // NaN
BigDecimal.PositiveInfinity - BigDecimal.PositiveInfinity;   // NaN

BigDecimal.NaN == BigDecimal.NaN;                   // false
BigDecimal.NaN.Equals(BigDecimal.NaN);              // true
BigDecimal.NaN.CompareTo(BigDecimal.MinValue);      // -1, so Array.Sort is well defined
```

Those three answers are the ones the base class library gives for `double`, and
they are deliberate rather than an oversight.

### JSON

The type carries its own converter, so no registration is needed. A value is
written as a JSON string, which is what preserves digits a `double` reader
would lose.

```csharp
var json = JsonSerializer.Serialize(amount);
// "123456789012345678901234567890.123456789012345678"

JsonSerializer.Deserialize<BigDecimal>(json) == amount;    // true
```

### Generic math

`BigDecimal` satisfies `INumber<T>`, so it drops into code written against the
constraint:

```csharp
static T Total<T>(IEnumerable<T> values) where T : INumber<T> =>
    values.Aggregate(T.Zero, (acc, x) => acc + x);

Total([
    BigDecimal.Parse("0.1", invariant),
    BigDecimal.Parse("0.2", invariant),
    BigDecimal.Parse("0.3", invariant),
]);
// 0.6
```

## Range and database coverage

The magnitude spans 0 to 2^256-1 and the scale 0 to 255. Quote those two bounds
rather than a single digit count: 77 significant digits always fit, a 78-digit
value fits only up to 2^256-1, and the scale decides where those digits sit.

| Column type | Coverage |
| ----------- | -------- |
| ClickHouse `Decimal32(S)`, `Decimal64(S)`, `Decimal128(S)`, `Decimal256(S)` | Lossless, for every precision and scale ClickHouse allows. The widest of them carries 76 significant digits, one fewer than always fit here. |
| ClickHouse `Decimal(P, S)`, P from 1 to 76 | Lossless. |
| PostgreSQL `numeric(p, s)`, p up to 77 | Lossless. This covers `numeric(38, 18)`, the common money and blockchain precision, with room to spare. |
| PostgreSQL `numeric` unconstrained, integer part within the magnitude | Accepted; fractional digits beyond what the magnitude leaves are rounded half to even. PostgreSQL allows 16383 of them, so a value read from such a column can lose digits. |
| PostgreSQL `numeric` unconstrained, integer part beyond the magnitude | `OverflowException`. PostgreSQL allows 131072 integer digits. |
| PostgreSQL `NaN`, `Infinity`, `-Infinity` | Lossless. No ClickHouse decimal has a counterpart, so writing one to a ClickHouse column is refused rather than approximated. |

## What it costs

Allocation-free is not the same as cheap, and the two numbers that decide
whether this type belongs in a given loop are its width and its stack.

**A value is 40 bytes, against `decimal`'s 16.** Four 64-bit magnitude words
and a packed 32-bit field, padded to the alignment. Every binary operator takes
both operands by value, so an operation copies 80 bytes before it does any work,
and `INumber<T>` declares its operators by value: an `in` overload cannot be
added without leaving the interface. That is the price of a fixed-width type
that never touches the heap, and it is why the ratios below are what they are.

**Measured against `System.Decimal`**, on the operand shapes and the machine
recorded in [`BASELINE.md`][baseline-url], zero allocations on every row:

| Operation | Ratio to `decimal` |
| --------- | -----------------: |
| `Add`, `Subtract` | 2.4x, 2.6x |
| `Multiply` | 3.2x |
| `Divide` | 5.3x |
| `Remainder` | 2.8x |
| `Parse`, `TryParse` | 1.1x for `char`, 1.3x for UTF-8 |
| `TryFormat` | 2.8x for `char`, 2.7x for UTF-8 |

Those are one machine's readings on one operand shape each, taken to grade a
budget rather than to publish a benchmark; read them as an order of magnitude,
not a specification. Division is the worst case, and it is the one to measure
yourself if it sits in a hot loop.

**Hashing carries no budget and costs a larger multiple than anything in the
table.** A hash has to agree with numeric equality, so `1.0` and `1.00` land in
the same bucket, which sends every hash through the value's shortest form: a copy
of the magnitude, a test for trailing zeros, and a division pass over it only when
there are zeros to remove. A value that carries none skips that pass - `decimal`
strips zeros for the same reason and skips it too - and measures between 12.7x and
16.1x `decimal`'s hash, the wider mantissa at the top of the range and the
baseline a few instructions under a nanosecond. A value widened to a database
column's scale pays the pass and costs about twice again. Worth knowing before a
`Dictionary<BigDecimal, T>` on a hot path, and a reason to hold keys at their
shortest scale.

**The stack is where the working buffers live.** Counted across the whole call
rather than one frame, a division, a parse and a `ToString` each take between
one and one and a half kilobytes: the entry method's buffers plus the ones the
division primitive and the digit renderer declare under it. That is ordinary for
a call from application code, and it is worth knowing before putting the type on
a deeply recursive path or in an `async` state machine whose stack is already
hot.

## Good to know

- **The 256-bit magnitude is the only hard limit.** When a result's significant
  digits do not fit, the scale is reduced and the fraction is rounded away, as
  far as needed. `OverflowException` is reserved for an integer part that still
  does not fit. There is no wrapping mode and no silent truncation.
- **Zero never carries a sign.** This is the one deliberate divergence from
  `decimal`.
- **Casting from `double` or `float` takes the shortest round-trippable form**,
  not 15 and 7 significant digits, so `(float)(BigDecimal)value` returns
  `value` for every finite `float`, and `(double)(BigDecimal)value` does for
  every finite `double` inside the range. This is a deliberate divergence from
  `decimal`'s own cast.
- **Parsing accepts the same white space `System.Decimal` does**, which is a
  tab, newline, vertical tab, form feed, carriage return or space and nothing
  else. Nineteen further characters that `char.IsWhiteSpace` accepts are
  refused.
- **Division by zero throws** `DivideByZeroException` rather than producing an
  infinity. Nothing finite produces a non-finite value.
- **`Sqrt` and real-valued `Pow` are deliberately out of scope.** Both are
  inexact by nature and cannot be offered without a precision context, which
  this type does not have. `BigInteger` declines `IPowerFunctions<T>` for the
  same reason.

## Packages

| Package | What it does |
| ------- | ------------ |
| [`PetToys.BigDecimal.Core`][core-url] | The `BigDecimal` type itself. No runtime dependencies. |
| [`PetToys.BigDecimal.Npgsql`][npgsql-url] | PostgreSQL `numeric` mapping for [Npgsql][npgsql-home]. |
| [`PetToys.BigDecimal.ClickHouse`][ch-url] | The ClickHouse `Decimal32/64/128/256` family, for [ClickHouse.Driver][ch-driver]. |
| [`PetToys.BigDecimal.Npgsql.EntityFrameworkCore`][ef-url] | PostgreSQL `numeric` columns as `BigDecimal` properties, over the Npgsql adapter. |
| [`PetToys.BigDecimal.Npgsql.Dapper`][dapper-url] | PostgreSQL `numeric` columns read and written as `BigDecimal` through Dapper, over the Npgsql adapter. |
| [`PetToys.BigDecimal.ClickHouse.Dapper`][ch-dapper-url] | The ClickHouse decimal family read and written as `BigDecimal` through Dapper, over the ClickHouse adapter. |

All six versions move in lockstep, and a package brings the ones beneath it along as
dependencies. The binary wire codecs the adapters run on, which are the hard
part, live inside the core and are exercised by its test suite, and they stay
internal on purpose: the supported surface is the mapping each adapter exposes,
not the bytes underneath it. A caller who works the wire directly, without either
driver, is the case that would change that, and it is a
[feature request][issues-url] rather than a gap.

## History

All six packages exist and each public surface is settled. `1.0.0` adds no
scope of its own; what it waited for was a prerelease meeting real code.
Versions are released in lockstep across every package in the repository, which
is why a fix to one adapter moves every other package's version too.

| Milestone | Version | Contents |
| --------- | ------- | -------- |
| Core type | `1.0.0-dev.1` | The `BigDecimal` type: representation, arithmetic, conversions, formatting, parsing, non-finite values, integer powers, JSON. Complete. |
| Database integration | `1.0.0-dev.2` | `PetToys.BigDecimal.Npgsql` and `PetToys.BigDecimal.ClickHouse`: reading and writing `numeric` and `Decimal*` columns through the two drivers. |
| EF Core and Dapper | `1.0.0-dev.3` | `PetToys.BigDecimal.Npgsql.EntityFrameworkCore`: a `numeric` column as a `BigDecimal` property, with no value converter in the path. `PetToys.BigDecimal.Npgsql.Dapper` and `PetToys.BigDecimal.ClickHouse.Dapper`: the same columns read and written exactly through Dapper. The two Dapper packages are split so that an Npgsql caller never pulls the ClickHouse driver, and both are split from the EF Core one so that a Dapper caller never pulls EF Core. Every published assembly is marked trimmable, the core gains a `TypeConverter`, and a ClickHouse parameter the statement did not annotate is refused by name. |
| First stable release | `1.0.0` | The same six packages, unchanged in scope, on nuget.org. |

`1.0.0` is the first release to reach nuget.org and carries all six packages at
once. Everything before it was a `1.0.0-dev.N` prerelease on
[GitHub Packages][gh-packages-url], which is what let each surface meet a real
consumer while it could still change: a package that reaches nuget.org is out
there for good, and this project would rather find out what an adapter gets
wrong before that than after. Feedback is still the most useful thing this
repository can receive: open an [issue][issues-url] or a
[discussion][discussions-url].

## Contributing

Bug reports, feature requests, and pull requests are welcome; see
[CONTRIBUTING][contributing-url]. Security issues go through
[private vulnerability reporting][security-url], not public issues.

Everyone taking part is expected to follow the
[Code of Conduct][conduct-url].

## License

Provided under the [Apache License, Version 2.0][license-url].

[repo-url]: https://github.com/pet-toys/big-decimal
[issues-url]: https://github.com/pet-toys/big-decimal/issues
[discussions-url]: https://github.com/pet-toys/big-decimal/discussions
[test-badge]: https://img.shields.io/github/actions/workflow/status/pet-toys/big-decimal/test.yml?branch=dev&style=flat-square&logo=github&label=test
[test-url]: https://github.com/pet-toys/big-decimal/actions?query=workflow%3Atest+branch%3Adev
[dotnet-badge]: https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet
[license-badge]: https://img.shields.io/github/license/pet-toys/big-decimal?style=flat-square&color=blue
[license-url]: https://www.apache.org/licenses/LICENSE-2.0
[core-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Core/
[npgsql-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Npgsql/
[ch-url]: https://www.nuget.org/packages/PetToys.BigDecimal.ClickHouse/
[ef-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Npgsql.EntityFrameworkCore/
[dapper-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Npgsql.Dapper/
[ch-dapper-url]: https://www.nuget.org/packages/PetToys.BigDecimal.ClickHouse.Dapper/
[gh-packages-url]: https://github.com/orgs/pet-toys/packages?repo_name=big-decimal
[npgsql-home]: https://www.npgsql.org/
[ch-driver]: https://www.nuget.org/packages/ClickHouse.Driver/
[baseline-url]: https://github.com/pet-toys/big-decimal/blob/dev/bench/PetToys.BigDecimal.Core.Benchmarks/BASELINE.md
[decimal-url]: https://learn.microsoft.com/dotnet/api/system.decimal
[inumber-url]: https://learn.microsoft.com/dotnet/api/system.numerics.inumber-1
[contributing-url]: https://github.com/pet-toys/big-decimal/blob/dev/docs/CONTRIBUTING.md
[conduct-url]: https://github.com/pet-toys/big-decimal/blob/dev/docs/CODE_OF_CONDUCT.md
[security-url]: https://github.com/pet-toys/big-decimal/security/advisories/new
