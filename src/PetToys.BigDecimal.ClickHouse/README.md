# PetToys.BigDecimal.ClickHouse

[![NuGet Version][nuget-v-badge]][nuget-url] [![NuGet Downloads][nuget-dt-badge]][nuget-url] [![Unit Test][test-badge]][test-url] [![Target frameworks][dotnet-badge]][nuget-url] [![License][license-badge]][license-url]

ClickHouse helpers for [`PetToys.BigDecimal.Core`][core-url]: they map the
`Decimal32`, `Decimal64`, `Decimal128`, and `Decimal256` column types — whose
range and scale go well past `decimal` — onto `BigDecimal` when reading and
writing through [ClickHouse.Driver][ch-driver].

Bring your own configured connection; this package only deals with the value
mapping.

## Installation

```sh
dotnet add package PetToys.BigDecimal.ClickHouse
```

The core type's own package, [`PetToys.BigDecimal.Core`][core-url], comes along
as a dependency.

## Links

- [Source and documentation][repo-url]
- [Report an issue][issues-url]

## License

Provided under the [Apache License, Version 2.0][license-url].

[repo-url]: https://github.com/pet-toys/big-decimal
[issues-url]: https://github.com/pet-toys/big-decimal/issues
[nuget-url]: https://www.nuget.org/packages/PetToys.BigDecimal.ClickHouse/
[nuget-v-badge]: https://img.shields.io/nuget/v/PetToys.BigDecimal.ClickHouse?style=flat-square&logo=nuget&label=version
[nuget-dt-badge]: https://img.shields.io/nuget/dt/PetToys.BigDecimal.ClickHouse?style=flat-square&logo=nuget
[test-badge]: https://img.shields.io/github/actions/workflow/status/pet-toys/big-decimal/test.yml?branch=dev&style=flat-square&logo=github&label=test
[test-url]: https://github.com/pet-toys/big-decimal/actions?query=workflow%3Atest+branch%3Adev
[dotnet-badge]: https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet
[license-badge]: https://img.shields.io/github/license/pet-toys/big-decimal?style=flat-square&color=blue
[license-url]: https://www.apache.org/licenses/LICENSE-2.0
[core-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Core/
[ch-driver]: https://www.nuget.org/packages/ClickHouse.Driver/
