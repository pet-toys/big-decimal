# PetToys.BigDecimal

[![NuGet Version][nuget-v-badge]][nuget-url] [![NuGet Downloads][nuget-dt-badge]][nuget-url] [![Unit Test][test-badge]][test-url] [![Target frameworks][dotnet-badge]][nuget-url] [![License][license-badge]][license-url]

A stack-only decimal floating-point value with a 256-bit mantissa and a decimal
scale of 0..255, covering at least 76 significant decimal digits. The whole
state lives in the struct, so a `BigDecimal` never allocates.

It exists because PostgreSQL `numeric` and ClickHouse `Decimal*` columns hold
values that `decimal` cannot represent — a large integer part, a long fraction,
sometimes both in the same column. Inside `decimal`'s own domain the semantics
deliberately mirror `decimal`: trailing zeros survive arithmetic and
formatting, equality is numeric (`1.0 == 1.00`), and excess fractional digits
are rounded half-to-even rather than throwing. `OverflowException` is reserved
for an integer part that cannot fit the 256-bit mantissa.

The type implements `INumber<T>`, `ISignedNumber<T>`, `IMinMaxValue<T>`, the
`IParsable`/`ISpanParsable`/`IUtf8SpanParsable` and
`IFormattable`/`ISpanFormattable`/`IUtf8SpanFormattable` families, and ships a
`System.Text.Json` converter.

No runtime dependencies. Database helpers live in separate packages:
[`PetToys.BigDecimal.Npgsql`][npgsql-url] and
[`PetToys.BigDecimal.ClickHouse`][ch-url].

## Installation

```sh
dotnet add package PetToys.BigDecimal
```

## Links

- [Source and documentation][repo-url]
- [Report an issue][issues-url]

## License

Provided under the [Apache License, Version 2.0][license-url].

[repo-url]: https://github.com/pet-toys/big-decimal
[issues-url]: https://github.com/pet-toys/big-decimal/issues
[nuget-url]: https://www.nuget.org/packages/PetToys.BigDecimal/
[nuget-v-badge]: https://img.shields.io/nuget/v/PetToys.BigDecimal?style=flat-square&logo=nuget&label=version
[nuget-dt-badge]: https://img.shields.io/nuget/dt/PetToys.BigDecimal?style=flat-square&logo=nuget
[test-badge]: https://img.shields.io/github/actions/workflow/status/pet-toys/big-decimal/test.yml?branch=dev&style=flat-square&logo=github&label=test
[test-url]: https://github.com/pet-toys/big-decimal/actions?query=workflow%3Atest+branch%3Adev
[dotnet-badge]: https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet
[license-badge]: https://img.shields.io/github/license/pet-toys/big-decimal?style=flat-square&color=blue
[license-url]: https://www.apache.org/licenses/LICENSE-2.0
[npgsql-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Npgsql/
[ch-url]: https://www.nuget.org/packages/PetToys.BigDecimal.ClickHouse/
