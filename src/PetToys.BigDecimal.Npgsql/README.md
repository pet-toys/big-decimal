# PetToys.BigDecimal.Npgsql

[![NuGet Version][nuget-v-badge]][nuget-url] [![NuGet Downloads][nuget-dt-badge]][nuget-url] [![Unit Test][test-badge]][test-url] [![Target frameworks][dotnet-badge]][nuget-url] [![License][license-badge]][license-url]

PostgreSQL helpers for [`PetToys.BigDecimal.Core`][core-url]: they map the
arbitrary `numeric` values that PostgreSQL allows - and that `decimal` cannot
hold - onto `BigDecimal` when reading and writing through [Npgsql][npgsql-home].

Bring your own configured `NpgsqlConnection` or data source; this package only
deals with the value mapping.

## Installation

```sh
dotnet add package PetToys.BigDecimal.Npgsql
```

The core type's own package, [`PetToys.BigDecimal.Core`][core-url], comes along
as a dependency.

## Usage

Register the mapping once, on the data source:

```csharp
using Npgsql;
using PetToys.BigDecimal.Numerics;

await using var source = new NpgsqlDataSourceBuilder(connectionString)
    .UseBigDecimal()
    .Build();
```

Then ask for the type where you want it:

```csharp
await using var command = source.CreateCommand(
    "SELECT total FROM invoices WHERE id = $1");
command.Parameters.Add(new NpgsqlParameter { Value = invoiceId });

await using var reader = await command.ExecuteReaderAsync();
await reader.ReadAsync();

BigDecimal total = reader.GetBigDecimal("total");
```

Writing needs nothing but the value:

```csharp
command.Parameters.Add(new NpgsqlParameter { Value = total });
```

`numeric[]` maps as well, to `BigDecimal[]`, `List<BigDecimal>` and their
nullable forms; and a binary `COPY` import goes through the same mapping, which
is the path to use for more than a handful of rows.

`UseBigDecimal` is also an extension on `INpgsqlTypeMapper`, so it works from a
configuration callback and on `NpgsqlSlimDataSourceBuilder`.

## Registering it does not change anything else

`BigDecimal` is for the columns that need it, not a replacement for
`System.Decimal` across an application, and the registration is built that way.
A `numeric` column read with `GetValue` is still a `decimal`, `GetFieldType`
still reports `decimal`, and a `DataTable` still fills with `decimal`. Code
written before this package was referenced reads exactly what it read before.
`BigDecimal` is reached by asking for it, per read.

## What round-trips, and what does not

| Column | Coverage |
| ------ | -------- |
| `numeric(p, s)`, p up to 77 | Lossless. This covers `numeric(38, 18)`, the common money and blockchain precision, with room to spare. |
| `numeric` unconstrained, integer part within the magnitude | Accepted; fractional digits beyond what the magnitude leaves are rounded half to even. PostgreSQL allows 16383 of them, so a value read from such a column can lose digits silently. |
| `numeric` unconstrained, integer part beyond the magnitude | `OverflowException`. PostgreSQL allows 131072 integer digits. |
| `NaN`, `Infinity`, `-Infinity` | Lossless, as `BigDecimal.NaN`, `BigDecimal.PositiveInfinity` and `BigDecimal.NegativeInfinity`. PostgreSQL sorts `NaN` above every other `numeric` value where this type sorts it below every other value; both make `NaN` equal to itself. |

The type's own bounds are 77 significant digits, a largest magnitude of 2^256-1,
and a range of 1e-255 to approximately 1.157e77.

Only one direction can fail. Every value of the type fits a `numeric`, so
writing never overflows and never rounds. Reading is the direction with a
boundary, and `GetBigDecimal` names the column when a value crosses it:

```
Column 'total' at ordinal 0 holds a numeric whose integer part is larger than
BigDecimal can represent: ...
```

That naming is what the accessors are for. Reading the same column through
`GetFieldValue<BigDecimal>`, through Dapper, or through Entity Framework Core
gets the same mapping and the same `OverflowException`, without the column name.

## Requirements

- **Npgsql 10.0.3 or later.** The mapping registers through `Npgsql.Internal`,
  which is the driver's only extension point for a new type and is published as
  experimental, so the floor is the version this package was built against.
- **PostgreSQL 14 or later for the infinities.** That is where `numeric` gained
  the sign codes that carry them. Everything else works on any supported server;
  against an older one, writing an infinity is refused by the server itself and
  this package does not emulate or substitute anything.

## Links

- [Source and documentation][repo-url]
- [Report an issue][issues-url]

## License

Provided under the [Apache License, Version 2.0][license-url].

[repo-url]: https://github.com/pet-toys/big-decimal
[issues-url]: https://github.com/pet-toys/big-decimal/issues
[nuget-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Npgsql/
[nuget-v-badge]: https://img.shields.io/nuget/v/PetToys.BigDecimal.Npgsql?style=flat-square&logo=nuget&label=version
[nuget-dt-badge]: https://img.shields.io/nuget/dt/PetToys.BigDecimal.Npgsql?style=flat-square&logo=nuget
[test-badge]: https://img.shields.io/github/actions/workflow/status/pet-toys/big-decimal/test.yml?branch=dev&style=flat-square&logo=github&label=test
[test-url]: https://github.com/pet-toys/big-decimal/actions?query=workflow%3Atest+branch%3Adev
[dotnet-badge]: https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet
[license-badge]: https://img.shields.io/github/license/pet-toys/big-decimal?style=flat-square&color=blue
[license-url]: https://www.apache.org/licenses/LICENSE-2.0
[core-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Core/
[npgsql-home]: https://www.npgsql.org/
