# BigDecimal for .NET

[![Unit Test][test-badge]][test-url] [![Target frameworks][dotnet-badge]][repo-url] [![License][license-badge]][license-url]

> A decimal that survives the round trip. PostgreSQL `numeric` and ClickHouse
> `Decimal*` columns hold values `decimal` cannot represent — this is the type
> that maps them, plus the helpers that read and write them.

`BigDecimal` is a stack-only decimal value: a 256-bit magnitude, a sign, and a
scale of 0 to 255. Every value of at most 77 significant digits is
representable, the largest representable magnitude has 78 digits, and the range
runs from 1e-255 to roughly 1.157e77 — enough for every ClickHouse `Decimal*`
value and every PostgreSQL `numeric(p, s)` up to 77 digits of precision. The
whole state lives in the struct, so it never allocates. Inside `decimal`'s own
domain it deliberately mirrors `decimal`: trailing zeros survive arithmetic and
formatting, equality is numeric (`1.0 == 1.00`), and excess fractional digits
are rounded half-to-even rather than throwing.

## Packages

| Package | Version | Downloads | What it does |
| ------- | ------- | --------- | ------------ |
| [`PetToys.BigDecimal`][core-url] | [![NuGet Version][core-v-badge]][core-url] | [![NuGet Downloads][core-dt-badge]][core-url] | The `BigDecimal` type itself. No runtime dependencies. |
| [`PetToys.BigDecimal.Npgsql`][npgsql-url] | [![NuGet Version][npgsql-v-badge]][npgsql-url] | [![NuGet Downloads][npgsql-dt-badge]][npgsql-url] | Helpers for reading and writing PostgreSQL `numeric` through [Npgsql][npgsql-home]. |
| [`PetToys.BigDecimal.ClickHouse`][ch-url] | [![NuGet Version][ch-v-badge]][ch-url] | [![NuGet Downloads][ch-dt-badge]][ch-url] | Helpers for the ClickHouse `Decimal32/64/128/256` family through [ClickHouse.Driver][ch-driver]. |

Planned, once the core settles: Dapper and EF Core support for PostgreSQL, and
Dapper support for ClickHouse.

## Installation

```sh
dotnet add package PetToys.BigDecimal
```

Add the integration package for the database you talk to:

```sh
dotnet add package PetToys.BigDecimal.Npgsql
dotnet add package PetToys.BigDecimal.ClickHouse
```

## Contributing

Bug reports, feature requests, and pull requests are welcome — see
[CONTRIBUTING][contributing-url]. Security issues go through
[private vulnerability reporting][security-url], not public issues.

## License

Provided under the [Apache License, Version 2.0][license-url].

[repo-url]: https://github.com/pet-toys/big-decimal
[test-badge]: https://img.shields.io/github/actions/workflow/status/pet-toys/big-decimal/test.yml?branch=dev&style=flat-square&logo=github&label=test
[test-url]: https://github.com/pet-toys/big-decimal/actions?query=workflow%3Atest+branch%3Adev
[dotnet-badge]: https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet
[license-badge]: https://img.shields.io/github/license/pet-toys/big-decimal?style=flat-square&color=blue
[license-url]: https://www.apache.org/licenses/LICENSE-2.0
[core-url]: https://www.nuget.org/packages/PetToys.BigDecimal/
[core-v-badge]: https://img.shields.io/nuget/v/PetToys.BigDecimal?style=flat-square&logo=nuget&label=version
[core-dt-badge]: https://img.shields.io/nuget/dt/PetToys.BigDecimal?style=flat-square&logo=nuget
[npgsql-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Npgsql/
[npgsql-v-badge]: https://img.shields.io/nuget/v/PetToys.BigDecimal.Npgsql?style=flat-square&logo=nuget&label=version
[npgsql-dt-badge]: https://img.shields.io/nuget/dt/PetToys.BigDecimal.Npgsql?style=flat-square&logo=nuget
[ch-url]: https://www.nuget.org/packages/PetToys.BigDecimal.ClickHouse/
[ch-v-badge]: https://img.shields.io/nuget/v/PetToys.BigDecimal.ClickHouse?style=flat-square&logo=nuget&label=version
[ch-dt-badge]: https://img.shields.io/nuget/dt/PetToys.BigDecimal.ClickHouse?style=flat-square&logo=nuget
[npgsql-home]: https://www.npgsql.org/
[ch-driver]: https://www.nuget.org/packages/ClickHouse.Driver/
[contributing-url]: https://github.com/pet-toys/big-decimal/blob/dev/docs/CONTRIBUTING.md
[security-url]: https://github.com/pet-toys/big-decimal/security/advisories/new
