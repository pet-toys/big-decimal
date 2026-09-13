# PetToys.BigDecimal.Core

[![NuGet Version][nuget-v-badge]][nuget-url] [![NuGet Downloads][nuget-dt-badge]][nuget-url] [![Unit Test][test-badge]][test-url] [![Target frameworks][dotnet-badge]][nuget-url] [![License][license-badge]][license-url]

An allocation-free decimal value type: a 256-bit magnitude, a sign, and a scale
of 0 to 255. Every value of at most 77 significant digits is representable, the
largest representable magnitude has 78 digits, and the range runs from 1e-255 to
roughly 1.157e77. The whole state lives in the struct and the working buffers
on the stack, so an operation allocates only what it hands back.

It exists because PostgreSQL `numeric` and ClickHouse `Decimal*` columns hold
values that `decimal` cannot represent - a large integer part, a long fraction,
sometimes both in the same column. Inside `decimal`'s own domain the semantics
deliberately mirror `decimal`: trailing zeros survive arithmetic and
formatting, equality is numeric (`1.0 == 1.00`), and excess fractional digits
are rounded half-to-even rather than throwing. The 256-bit magnitude is the
only hard limit: when a value's significant digits do not fit, the scale is
reduced - the fraction rounded away - as far as needed, and
`OverflowException` is reserved for an integer part that still does not fit.

The type implements `INumber<T>`, `ISignedNumber<T>`, `IMinMaxValue<T>`, the
`IParsable`/`ISpanParsable`/`IUtf8SpanParsable` and
`IFormattable`/`ISpanFormattable`/`IUtf8SpanFormattable` families, and ships a
`System.Text.Json` converter.

`Pow(value, exponent)` raises a value to an integer power, and it is exact
whenever the exact power is representable: 61 significant digits of `1.05` to
the 30th come back digit for digit, where a hand-written multiplication loop
would have rounded at every step. A power too wide to represent gives up
fractional digits, rounded half to even once from a 154-digit working value
against the 77 a result keeps - room enough that the digit the rounding reads
is the exact power's, short of the near-tie the method's own remarks describe.
A negative exponent is the reciprocal, to the same precision a division without
an explicit scale gives, and it answers wherever its own result fits even when
the power it inverts does not.

Formatting matches `decimal` string for string: the `C`, `E`, `F`, `G`, `N`,
`P` and `R` specifiers with an optional precision, custom numeric format
strings, and the culture's own group sizes and negative patterns - so a culture
that writes `(1,234.5)` gets that rather than a leading sign. Both the `char`
and the UTF-8 overload write the same text, bounded only by the destination the
caller passes.

No runtime dependencies. Database helpers live in separate packages:
[`PetToys.BigDecimal.Npgsql`][npgsql-url] and
[`PetToys.BigDecimal.ClickHouse`][ch-url].

## Range and database coverage

The magnitude spans 0 to 2^256-1 and the scale 0 to 255. Quote those two
bounds rather than a single digit count: 77 significant digits always fit, a
78-digit value fits only up to 2^256-1, and the scale decides where those
digits sit - from 1e-255 to roughly 1.157e77.

| Column type | Coverage |
| ----------- | -------- |
| ClickHouse `Decimal32(S)`, `Decimal64(S)`, `Decimal128(S)`, `Decimal256(S)` | Lossless, for every precision and scale ClickHouse allows. The widest of them carries 76 significant digits, one fewer than always fit here. |
| ClickHouse `Decimal(P, S)`, P from 1 to 76 | Lossless. |
| PostgreSQL `numeric(p, s)`, p up to 77 | Lossless. This covers `numeric(38, 18)`, the common money and blockchain precision, with room to spare. |
| PostgreSQL `numeric` unconstrained, integer part within the magnitude | Accepted; fractional digits beyond what the magnitude leaves are rounded half to even. PostgreSQL allows 16383 of them, so a value read from such a column can lose digits silently. |
| PostgreSQL `numeric` unconstrained, integer part beyond the magnitude | `OverflowException`. PostgreSQL allows 131072 integer digits. |
| PostgreSQL `NaN`, `Infinity`, `-Infinity` | Lossless, as `BigDecimal.NaN`, `BigDecimal.PositiveInfinity` and `BigDecimal.NegativeInfinity`. No ClickHouse decimal has a counterpart, so writing one to a ClickHouse column is refused rather than approximated. PostgreSQL sorts `NaN` above every other `numeric` value where this type sorts it below every other value; both make `NaN` equal to itself. |

Presenting a value at a column's declared scale is what `WithScale` is for: it
pads as well as rounds, where `Round` only ever narrows.

```csharp
var price = BigDecimal.Parse("1.5", CultureInfo.InvariantCulture);
price.WithScale(18);                 // 1.500000000000000000, for numeric(38,18)
BigDecimal.Round(price, 18);         // 1.5 - Round never pads
```

## What it costs

Allocation-free is not the same as cheap. A value is **40 bytes** against
`decimal`'s 16 - four 64-bit magnitude words and a packed 32-bit field - and
every binary operator takes both operands by value, so an operation copies 80
bytes before it does any work. `INumber<T>` declares its operators by value, so
an `in` overload cannot be added without leaving the interface.

Against `System.Decimal`, on the operand shapes and the machine recorded in
[`BASELINE.md`][baseline-url] and with zero allocations on every row: `Add` and
`Subtract` at 2.4x and 2.6x, `Multiply` 3.2x, `Divide` 5.3x, `Remainder` 2.8x,
`Parse` 1.1x, `TryFormat` 2.8x, the UTF-8 overloads within 0.2x of the `char`
ones either way. One machine, one shape per row, taken to grade
a budget rather than to publish a benchmark - an order of magnitude, not a
specification. Division is the worst case and the one to measure yourself.

`GetHashCode` carries no budget and is a larger multiple than anything above.
Agreeing with numeric equality sends every hash through the value's shortest
form: a copy of the magnitude, a test for trailing zeros, and a division pass
over it only when there are zeros to remove. A value carrying none skips that
pass, as `decimal` does for the same reason, and measures 12.7x to 16.1x
`decimal`'s hash - the wider mantissa at the top of the range, against a
baseline of a few instructions under a nanosecond. One widened to a database
column's scale pays the pass and costs about 2x as much, which is a reason to
hold dictionary keys at their shortest scale.

The working buffers are on the stack: counted across the whole call rather than
one frame, a division, a parse and a `ToString` each take between one and one
and a half kilobytes. Ordinary for a call from application code, worth knowing
before a deeply recursive path or an `async` state machine whose stack is
already hot.

## Trimming and Native AOT

The assembly is marked `IsAotCompatible`, which implies `IsTrimmable`. That is a
gate rather than a claim: the trim, single-file and AOT analyzers run over this
project and warnings are errors, so a reflective path could not be added without
failing the build. It is also verified by publishing a probe application over
the package and running it - trimmed on every supported framework and Native AOT
on the newest, on Windows and on Linux - because a clean analyzer pass and a
binary that throws on first use look identical from inside the build.

One thing changes for you, and it is `System.Text.Json` rather than this type.
Both `PublishTrimmed` and `PublishAot` turn off reflection-based serialization,
so `JsonSerializer.Serialize(value)` throws `InvalidOperationException` there for
any type at all. Reach the converter through a source-generated context instead:
the `[JsonConverter]` attribute `BigDecimal` carries is honoured on that path,
and the text is identical to what a jitted build produces.

```csharp
[JsonSerializable(typeof(Invoice))]
internal sealed partial class AppJsonContext : JsonSerializerContext;

var json = JsonSerializer.Serialize(invoice, AppJsonContext.Default.Invoice);
```

Formatting stays culture-aware in a trimmed and Native AOT binary. Nothing here
needs `InvariantGlobalization`, and a value formatted under a culture whose
separators differ from the invariant one produces that culture's separators, as
it does when jitted.

## Reading a value from text that is not code

`TypeDescriptor.GetConverter(typeof(BigDecimal))` answers a converter, so
configuration binding, model binding and anything else that reaches a type
through reflection reads a `BigDecimal` from a string with no registration of
yours:

```csharp
// appsettings.json: { "Limits": { "Ceiling": "123456789012345678901234.5678" } }
builder.Services.Configure<Limits>(builder.Configuration.GetSection("Limits"));
```

It converts text and nothing else, in both directions, and it carries no rules
of its own - it calls this type's parse and format, so the two cannot disagree.
A culture you pass is honoured; no culture means the invariant one, which is
what a value out of a configuration file needs.

Numbers still convert to numbers through the cast operators and generic math.
The converter deliberately refuses them, so there is one route with compiler
checking rather than two with different rules.

## This type implements no `IConvertible`, on purpose

`Convert.ToDecimal(value)` does not compile against it, and a library that does
not recognise the type cannot fall back to converting it either. That is the
point. The interface would have to answer for a value of up to 77 significant
digits, and the two honest answers are to narrow it - losing the number exactly
where nobody is looking - or to throw, which turns a loud failure into one that
only appears for values too large to fit, which test data rarely is.

Two packages in this repository depend on the absence: a `BigDecimal` reaching
`ClickHouse.Driver` without the parameter formatter installed fails before
anything is sent, and one reaching Dapper without a handler registered is
refused by name. If a library refuses your value, that is this decision working.

## Installation

```sh
dotnet add package PetToys.BigDecimal.Core
```

The `.Core` suffix belongs to the package, not to the API: the type is
`PetToys.BigDecimal.Numerics.BigDecimal`, the same namespace the database
packages put their helpers in.

> **Releases go to nuget.org**, so the command above is all that is needed.
> Prereleases are published to [GitHub Packages][gh-packages-url] instead: that
> feed has to be added to your `nuget.config`, and it requires a personal access
> token with `read:packages` even for a public package.

## Links

- [Source and documentation][repo-url]
- [Report an issue][issues-url]

## License

Provided under the [Apache License, Version 2.0][license-url].

[repo-url]: https://github.com/pet-toys/big-decimal
[baseline-url]: https://github.com/pet-toys/big-decimal/blob/dev/bench/PetToys.BigDecimal.Core.Benchmarks/BASELINE.md
[gh-packages-url]: https://github.com/orgs/pet-toys/packages?repo_name=big-decimal
[issues-url]: https://github.com/pet-toys/big-decimal/issues
[nuget-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Core/
[nuget-v-badge]: https://img.shields.io/nuget/v/PetToys.BigDecimal.Core?style=flat-square&logo=nuget&label=version
[nuget-dt-badge]: https://img.shields.io/nuget/dt/PetToys.BigDecimal.Core?style=flat-square&logo=nuget
[test-badge]: https://img.shields.io/github/actions/workflow/status/pet-toys/big-decimal/test.yml?branch=dev&style=flat-square&logo=github&label=test
[test-url]: https://github.com/pet-toys/big-decimal/actions?query=workflow%3Atest+branch%3Adev
[dotnet-badge]: https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet
[license-badge]: https://img.shields.io/github/license/pet-toys/big-decimal?style=flat-square&color=blue
[license-url]: https://www.apache.org/licenses/LICENSE-2.0
[npgsql-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Npgsql/
[ch-url]: https://www.nuget.org/packages/PetToys.BigDecimal.ClickHouse/
