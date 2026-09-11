# PetToys.BigDecimal.Npgsql.Dapper

[![NuGet Version][nuget-v-badge]][nuget-url] [![NuGet Downloads][nuget-dt-badge]][nuget-url] [![Unit Test][test-badge]][test-url] [![Target frameworks][dotnet-badge]][nuget-url] [![License][license-badge]][license-url]

Dapper mapping for [`PetToys.BigDecimal.Core`][core-url]: a PostgreSQL `numeric`
column reads into a `BigDecimal` and a `BigDecimal` parameter writes back, at
any width the type holds and with `System.Decimal` nowhere in the path.

Without it, `Query<BigDecimal>` over a `numeric` column returns a
default-constructed value and throws nothing, because Dapper falls through to
property-by-name mapping and the struct's own properties are not column names.
That silent answer is what this package replaces.

Bring your own connection and SQL; this package only deals with the value
mapping.

## Installation

```sh
dotnet add package PetToys.BigDecimal.Npgsql.Dapper
```

[`PetToys.BigDecimal.Npgsql`][npgsql-package-url] and
[`PetToys.BigDecimal.Core`][core-url] come along as dependencies.

> **Releases go to nuget.org**, so the command above is all that is needed.
> Prereleases are published to [GitHub Packages][gh-packages-url] instead: that
> feed has to be added to your `nuget.config`, and it requires a personal access
> token with `read:packages` even for a public package.

## Usage

There are two registrations and neither works alone. The adapter installs the
codec on the data source; this package registers the type handler with Dapper.

```csharp
using Dapper;
using Npgsql;
using PetToys.BigDecimal.Numerics;

// Once, at start-up: Dapper's registry is static and process-wide.
SqlMapperBigDecimalExtensions.UseBigDecimal();

await using var source = new NpgsqlDataSourceBuilder(connectionString)
    .UseBigDecimal()
    .Build();
```

Writing needs nothing else - a parameter carries the value and no database type
is named at the call site:

```csharp
await using var connection = await source.OpenConnectionAsync();

await connection.ExecuteAsync(
    "insert into invoices (id, total) values (@id, @total)",
    new { id = 1, total = BigDecimal.Parse("123456789012345678901234567890.123456789") });
```

Reading exactly is a different call from `Query<T>`, and the reason is in
[the next section](#why-reading-is-a-different-call):

```csharp
public sealed record Invoice(int Id, BigDecimal Total);

var invoices = await connection.QueryBigDecimalAsync<Invoice>(
    "select id, total from invoices where id = @id",
    new { id = 1 });
```

Every `numeric` column of that query reads as `BigDecimal` - including
`numeric(p, s)`, an aggregate over one, a column of a domain over one, and
`numeric[]` into `BigDecimal[]`. A member of the same result still typed
`System.Decimal` keeps working: it converts where the value fits and throws
`OverflowException` where it does not, so nothing narrows quietly.

For anything `QueryBigDecimal` does not cover - an unbuffered read,
`QueryMultiple`, multi-mapping with `splitOn` - wrap the reader and hand it to
Dapper's own `Parse<T>`:

```csharp
await using var reader = connection
    .ExecuteReader("select id, total from invoices", new { })
    .AsBigDecimalReader();

foreach (var invoice in reader.Parse<Invoice>())
{
}
```

## Why reading is a different call

Dapper's extension point for a custom type is `SqlMapper.TypeHandler<T>`, whose
`Parse` is handed whatever `GetValue` already produced. Over a `numeric` column
that is a `System.Decimal`, and a value wider than one throws inside the driver
before the handler is reached. There is no way to ask Dapper for the exact read
in an ordinary `Query<T>`.

The three ways around that all cost something. Making `BigDecimal` the driver's
default for `numeric` would change what every untyped read in your application
already produces. Casting the column to text in the SQL makes every query this
package's business. Mapping only the write side leaves reading where it was.

So the exact read is its own call, over a reader that answers `numeric` columns
with the driver's own typed accessor. Nothing global changes, and the widening
is scoped to the query you chose it for.

## Registering it does not change anything else

`BigDecimal` is for the columns that need it, not a replacement for
`System.Decimal` across an application. After both registrations, `Query<decimal>`
over `1.50` still answers `1.50`, `Query<dynamic>` still answers a
`System.Decimal`, and a reader still reports `System.Decimal` as the field type
of a `numeric` column. Code written before this package was referenced reads
exactly what it read before, which the test suite asserts rather than the README
promising it.

## What an ordinary `Query<T>` does after the registration

It gets better, but it is still bounded by `System.Decimal`, because that is
what the driver produced before the handler saw anything:

| Value in the column | `Query<T>` | `QueryBigDecimal<T>` |
| ------------------- | ---------- | -------------------- |
| Inside `System.Decimal` | Exact, scale included | Exact |
| Wider than `System.Decimal` | `OverflowException` from the driver | Exact |
| `NaN`, `Infinity`, `-Infinity` | `InvalidCastException` or `OverflowException` from the driver | Exact |

Neither column of that table narrows a value. Where an ordinary read cannot
answer, it fails.

The handler also reads a column you cast yourself - `select total::text` - which
is exact at any width, if you would rather change the SQL than the call.

## What round-trips, and what does not

| Column | Coverage |
| ------ | -------- |
| `numeric(p, s)`, p up to 77 | Lossless. This covers `numeric(38, 18)`, the common money and blockchain precision, with room to spare. |
| `numeric` unconstrained, integer part within the magnitude | Accepted; fractional digits beyond what the magnitude leaves are rounded half to even. PostgreSQL allows 16383 of them, so a value read from such a column can lose digits silently. |
| `numeric` unconstrained, integer part beyond the magnitude | `OverflowException`. PostgreSQL allows 131072 integer digits. |
| `NaN`, `Infinity`, `-Infinity` | Lossless through `QueryBigDecimal`, as `BigDecimal.NaN`, `BigDecimal.PositiveInfinity` and `BigDecimal.NegativeInfinity`. PostgreSQL sorts `NaN` above every other `numeric` value where this type sorts it below every other value; both make `NaN` equal to itself. |

The type's own bounds are 77 significant digits, a largest magnitude of 2^256-1,
and a range of 1e-255 to approximately 1.157e77.

## The scale you read back is the column's, and the server rounds the other way

A value goes out at its own scale and PostgreSQL applies the column's. `1.5`
written into a `numeric(12,4)` is stored and read back as `1.5000`; written into
an unconstrained `numeric` it stays `1.5`.

Where the column's scale is shorter than the value's, the server rounds **half
away from zero**, and this type rounds half to even: `1.00025` written into a
`numeric(12,4)` is stored as `1.0003`, where the type's own rescaling would give
`1.0002`. This package does not pre-empt that - the handler is handed a
parameter and never learns which column it is bound for, so it has no schema to
rescale against. If you need this type's rule, rescale before writing.

A value beyond the column's declared precision is refused by PostgreSQL as
`22003 numeric field overflow`, raised as a `PostgresException`. The server does
not name the column and neither can this package.

## Requirements

- **Dapper 2.1.79 up to but not including 3.**
- **`PetToys.BigDecimal.Npgsql` on the data source.** Without it the read fails
  with `Reading as 'BigDecimal' is not supported for fields having DataTypeName
  'numeric'` and a write is refused naming the type. Neither narrows a value.
- **PostgreSQL 14 or later for the infinities**, which is where `numeric` gained
  the sign codes that carry them.
- **Trimming and Native AOT: Dapper does not work under either, and this
  package's own marking does not change that.** Measured by publishing and
  running rather than read off a warning:
  - `PublishTrimmed`: the registration and a single-column read still work, and
    materialising an object throws `InvalidOperationException` about a missing
    constructor, because the trimmer removed the members Dapper reflects for. An
    anonymous-object parameter stops binding for the same reason.
  - `PublishAot`: `SqlMapper.AddTypeHandler` itself throws
    `NotSupportedException` over `SqlMapper.TypeHandlerCache<T>`, before any
    query runs, because Dapper instantiates that generic reflectively.

  `IsAotCompatible` is set on this project and this assembly produces no trim,
  single-file or AOT diagnostic; `Dapper` 2.1.79 carries no `IsTrimmable`
  metadata and produces `IL2104` and `IL3053`. If you publish trimmed or Native
  AOT, reach `numeric` through
  [`PetToys.BigDecimal.Npgsql`][npgsql-package-url] directly, which is marked
  and whose driver is too.

## Two things about Dapper's registry

It is static and process-wide - it takes no scope argument - so `UseBigDecimal`
is a start-up call rather than a per-connection one. It is idempotent.

It also replaces any handler already registered for `BigDecimal`, silently, and
Dapper publishes no way to read the registry back, so a handler of your own has
to be registered after this one.

## Links

- [Source and documentation][repo-url]
- [Report an issue][issues-url]

## License

Provided under the [Apache License, Version 2.0][license-url].

[repo-url]: https://github.com/pet-toys/big-decimal
[gh-packages-url]: https://github.com/orgs/pet-toys/packages?repo_name=big-decimal
[issues-url]: https://github.com/pet-toys/big-decimal/issues
[nuget-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Npgsql.Dapper/
[nuget-v-badge]: https://img.shields.io/nuget/v/PetToys.BigDecimal.Npgsql.Dapper?style=flat-square&logo=nuget&label=version
[nuget-dt-badge]: https://img.shields.io/nuget/dt/PetToys.BigDecimal.Npgsql.Dapper?style=flat-square&logo=nuget
[test-badge]: https://img.shields.io/github/actions/workflow/status/pet-toys/big-decimal/test.yml?branch=dev&style=flat-square&logo=github&label=test
[test-url]: https://github.com/pet-toys/big-decimal/actions?query=workflow%3Atest+branch%3Adev
[dotnet-badge]: https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet
[license-badge]: https://img.shields.io/github/license/pet-toys/big-decimal?style=flat-square&color=blue
[license-url]: https://www.apache.org/licenses/LICENSE-2.0
[core-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Core/
[npgsql-package-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Npgsql/
