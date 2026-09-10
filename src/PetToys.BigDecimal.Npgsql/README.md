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

> **While the packages are in prerelease** they are published to
> [GitHub Packages][gh-packages-url] rather than to nuget.org, so the command
> above resolves nothing yet. Add the feed to your `nuget.config` first;
> GitHub Packages requires a personal access token with `read:packages` even
> for a public package. The `1.0.0` release goes to nuget.org, from which point
> the command above is all that is needed.

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
`GetFieldValue<BigDecimal>` or through Dapper gets the same mapping and the same
`OverflowException`, without the column name.

Through Entity Framework Core, add
[`PetToys.BigDecimal.Npgsql.EntityFrameworkCore`][efcore-package-url]: it maps a
`numeric` column to a `BigDecimal` property over this package's registration,
and the value the caller can name there is the property, read off the
`DbUpdateException` Entity Framework raises.

## Requirements

- **Npgsql 10.0.3, up to but not including 11.** The mapping registers through
  `Npgsql.Internal`, which is the driver's only extension point for a new type
  and is published as experimental, so the floor is the version this package was
  built against and the range is closed at the major. A driver major that
  reshapes the extension point therefore fails at restore rather than at the
  first read; the ceiling moves once the new major has been tested against.
- **Trimmed and Native AOT publishing both work.** This assembly is marked
  `IsAotCompatible`, and `Npgsql` 10.0.3 is itself marked trimmable, so the
  closure a publish trims carries no unmarked assembly. Both forms are verified
  by publishing a probe over this package and running it against a real server,
  not by a clean analyzer pass. Under Native AOT, build the data source with
  `NpgsqlSlimDataSourceBuilder`, which is Npgsql's own route for that case and
  which `UseBigDecimal` overloads; the slim builder starts with nothing enabled,
  so a `numeric[]` needs `EnableArrays()` on it. A trimmed build can use either
  builder. Serializing a `BigDecimal` to JSON in such an application needs a
  source-generated context, which is a `System.Text.Json` rule rather than one of
  this package's; the core package's README carries the detail.
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
[gh-packages-url]: https://github.com/orgs/pet-toys/packages?repo_name=big-decimal
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
[efcore-package-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Npgsql.EntityFrameworkCore/
[npgsql-home]: https://www.npgsql.org/
