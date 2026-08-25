# PetToys.BigDecimal.Core

[![NuGet Version][nuget-v-badge]][nuget-url] [![NuGet Downloads][nuget-dt-badge]][nuget-url] [![Unit Test][test-badge]][test-url] [![Target frameworks][dotnet-badge]][nuget-url] [![License][license-badge]][license-url]

A stack-only decimal value: a 256-bit magnitude, a sign, and a scale of 0 to
255. Every value of at most 77 significant digits is representable, the largest
representable magnitude has 78 digits, and the range runs from 1e-255 to
roughly 1.157e77. The whole state lives in the struct, so a `BigDecimal` never
allocates.

It exists because PostgreSQL `numeric` and ClickHouse `Decimal*` columns hold
values that `decimal` cannot represent — a large integer part, a long fraction,
sometimes both in the same column. Inside `decimal`'s own domain the semantics
deliberately mirror `decimal`: trailing zeros survive arithmetic and
formatting, equality is numeric (`1.0 == 1.00`), and excess fractional digits
are rounded half-to-even rather than throwing. The 256-bit magnitude is the
only hard limit: when a value's significant digits do not fit, the scale is
reduced — the fraction rounded away — as far as needed, and
`OverflowException` is reserved for an integer part that still does not fit.

The type implements `INumber<T>`, `ISignedNumber<T>`, `IMinMaxValue<T>`, the
`IParsable`/`ISpanParsable`/`IUtf8SpanParsable` and
`IFormattable`/`ISpanFormattable`/`IUtf8SpanFormattable` families, and ships a
`System.Text.Json` converter.

No runtime dependencies. Database helpers live in separate packages:
[`PetToys.BigDecimal.Npgsql`][npgsql-url] and
[`PetToys.BigDecimal.ClickHouse`][ch-url].

## Range and database coverage

The magnitude spans 0 to 2^256-1 and the scale 0 to 255. Quote those two
bounds rather than a single digit count: 77 significant digits always fit, a
78-digit value fits only up to 2^256-1, and the scale decides where those
digits sit — from 1e-255 to roughly 1.157e77.

| Column type | Coverage |
| ----------- | -------- |
| ClickHouse `Decimal32(S)`, `Decimal64(S)`, `Decimal128(S)`, `Decimal256(S)` | Lossless, for every precision and scale ClickHouse allows. The widest of them carries 76 significant digits, one fewer than always fit here. |
| ClickHouse `Decimal(P, S)`, P from 1 to 76 | Lossless. |
| PostgreSQL `numeric(p, s)`, p up to 77 | Lossless. This covers `numeric(38, 18)`, the common money and blockchain precision, with room to spare. |
| PostgreSQL `numeric` unconstrained, integer part within the magnitude | Accepted; fractional digits beyond what the magnitude leaves are rounded half to even. PostgreSQL allows 16383 of them, so a value read from such a column can lose digits silently. |
| PostgreSQL `numeric` unconstrained, integer part beyond the magnitude | `OverflowException`. PostgreSQL allows 131072 integer digits. |
| PostgreSQL `NaN`, `Infinity`, `-Infinity` | Not representable. These are the only `numeric` values with no counterpart here; the flag bits that will encode them are already reserved. |

Presenting a value at a column's declared scale is what `WithScale` is for: it
pads as well as rounds, where `Round` only ever narrows.

```csharp
var price = BigDecimal.Parse("1.5", CultureInfo.InvariantCulture);
price.WithScale(18);                 // 1.500000000000000000, for numeric(38,18)
BigDecimal.Round(price, 18);         // 1.5 — Round never pads
```

## Installation

```sh
dotnet add package PetToys.BigDecimal.Core
```

The `.Core` suffix belongs to the package, not to the API: the type is
`PetToys.BigDecimal.Numerics.BigDecimal`, the same namespace the database
packages put their helpers in.

## Links

- [Source and documentation][repo-url]
- [Report an issue][issues-url]

## License

Provided under the [Apache License, Version 2.0][license-url].

[repo-url]: https://github.com/pet-toys/big-decimal
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
