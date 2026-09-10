# PetToys.BigDecimal.ClickHouse.Dapper

[![NuGet Version][nuget-v-badge]][nuget-url] [![NuGet Downloads][nuget-dt-badge]][nuget-url] [![Unit Test][test-badge]][test-url] [![Target frameworks][dotnet-badge]][nuget-url] [![License][license-badge]][license-url]

Dapper mapping for [`PetToys.BigDecimal.Core`][core-url]: a ClickHouse
`Decimal32`, `Decimal64`, `Decimal128` or `Decimal256` column reads into a
`BigDecimal` and a `BigDecimal` parameter writes back, at any precision and
scale ClickHouse allows and with `System.Decimal` nowhere in the path.

Without it, `Query<BigDecimal>` over a decimal column returns a
default-constructed value and throws nothing, because Dapper falls through to
property-by-name mapping and the struct's own properties are not column names.
That silent answer is what this package replaces.

Bring your own connection and SQL; this package only deals with the value
mapping.

## Installation

```sh
dotnet add package PetToys.BigDecimal.ClickHouse.Dapper
```

[`PetToys.BigDecimal.ClickHouse`][clickhouse-package-url] and
[`PetToys.BigDecimal.Core`][core-url] come along as dependencies.

> **While the packages are in prerelease** they are published to
> [GitHub Packages][gh-packages-url] rather than to nuget.org, so the command
> above resolves nothing yet. Add the feed to your `nuget.config` first;
> GitHub Packages requires a personal access token with `read:packages` even
> for a public package. The `1.0.0` release goes to nuget.org, from which point
> the command above is all that is needed.

## Usage

There are two calls and neither works alone. One registers the handlers with
Dapper, for the process; the other configures a connection.

```csharp
using ClickHouse.Driver;
using ClickHouse.Driver.ADO;
using Dapper;
using PetToys.BigDecimal.Numerics;

// Once, at start-up: Dapper's registry is static and process-wide.
ClickHouseSqlMapperBigDecimalExtensions.UseBigDecimal();

await using var connection = new ClickHouseConnection(
    new ClickHouseClientSettings(connectionString).UseBigDecimalForDapper());
```

Reading is `Query<T>` and nothing else - no call of ours, no wrapper:

```csharp
public sealed class Invoice
{
    public int Id { get; set; }

    public BigDecimal Total { get; set; }
}

var invoices = await connection.QueryAsync<Invoice>(
    "SELECT id, total FROM invoices WHERE id = 1");
```

`Nullable(Decimal...)` reads as `BigDecimal?` and `Array(Decimal...)` as
`BigDecimal[]`. Every other column of the same row reads exactly as it would
without this package.

Writing needs the parameter's type named in the statement, in ClickHouse's own
syntax, and the parameters passed as `DynamicParameters`:

```csharp
var parameters = new DynamicParameters();
parameters.Add("total", BigDecimal.Parse("123456789012345678901234567890.123456789"));

await connection.ExecuteAsync(
    "INSERT INTO invoices (id, total) VALUES (1, {total:Decimal256(9)})",
    parameters);
```

Both halves of that are load bearing and [the next section](#why-writing-looks-like-that)
says why.

## Why writing looks like that

ClickHouse names a parameter's type in the statement rather than on the
parameter, and that name is the only way this package can learn the column's
scale and width. With it, the value is rescaled at the column's scale before it
is sent and a value the column cannot hold is refused by name; without it, there
is nothing to rescale against.

Dapper binds a parameter only when it finds its name in the statement as
`@total`, `:total` or `?total`. ClickHouse's `{total:Decimal256(9)}` is none of
those, so **a parameter passed as an anonymous object is dropped** and the
server answers:

```text
Code: 456. DB::Exception: Substitution `total` is not set.
```

`DynamicParameters` with `Add` is not filtered and is the shape to use. Note
that `new DynamicParameters(new { total = value })` - the template form - is
filtered like the anonymous object it wraps.

A parameter written as `@total` with no type named anywhere survives Dapper -
that syntax is Dapper's own - and is refused by this repository when it reaches
the driver:

```text
The parameter 'total' carries a BigDecimal and the statement does not name its
ClickHouse type, so the column's scale is unknown. Annotate it in the statement,
as in {total:Decimal256(6)}. Through Dapper, pass it with DynamicParameters.Add
as well - an anonymous object is stripped before the parameter reaches this
package.
```

`UseBigDecimalForDapper` installs that guard along with the formatter. A
parameter type resolver you had already put on the settings is kept and asked
about every type this repository does not map. Nothing is narrowed on any of
these routes.

## Registering it does not change anything else

`BigDecimal` is for the columns that need it, not a replacement for the driver's
own decimal across an application. After the registration, `Query<decimal>` over
a `Decimal64(4)` column holding `1.5` still answers `1.5000`, `Query<dynamic>`
still answers a `ClickHouseDecimal`, and a reader still reports
`ClickHouseDecimal` as the field type. Code written before this package was
referenced reads exactly what it read before, which the test suite asserts
rather than this README promising it.

That is the difference between this package and the connection-wide
`UseBigDecimal` in [`PetToys.BigDecimal.ClickHouse`][clickhouse-package-url],
which maps every decimal column on the connection whether or not anything asked.
Both are supported and they compose: a caller who has already used the wide form
has this package's formatter as well, and the handlers then receive values it
has already converted.

## What round-trips, and what does not

| Column | Coverage |
| ------ | -------- |
| `Decimal32(s)`, `Decimal64(s)`, `Decimal128(s)`, `Decimal256(s)` | Lossless in both directions, at every precision and scale ClickHouse allows. |
| `Nullable(Decimal...)` | As `BigDecimal?`. A NULL column read into a non-nullable member is left at its default, which is what Dapper does for `System.Decimal` too. |
| `Array(Decimal...)` | As `BigDecimal[]` when reading. Writing an array is not covered - use `InsertBigDecimalAsync` in [`PetToys.BigDecimal.ClickHouse`][clickhouse-package-url]. |
| `NaN`, `Infinity`, `-Infinity` | Refused when writing, with `NotSupportedException`. No ClickHouse decimal represents them. |

The type's own bounds are 77 significant digits, a largest magnitude of 2^256-1,
and a range of 1e-255 to approximately 1.157e77. Every ClickHouse decimal fits
inside that, so reading never overflows and writing is where the boundaries are.

## The scale you read back is the column's

ClickHouse keeps a decimal's scale in the column type and nowhere in the value,
so `1.5` written into a `Decimal64(4)` column reads back as `1.5000`.

Where a written value has more fractional digits than the column's scale, **this
package rescales it half to even** before the statement is sent. Both the driver
and the server truncate toward zero instead, so a value that reached either of
them unrescaled would be a different number: `1.00015` into a `Decimal64(4)`
column is stored as `1.0002` here and would be `1.0001` if the server did it.

A value whose mantissa at the column's scale is outside the column's payload
width or its declared precision raises `OverflowException` naming the parameter
and the type, before anything is sent. Where the server computes a value - an
aggregate, an expression, a cast - the arithmetic and the rounding are the
server's.

## Requirements

- **Dapper 2.1.79 up to but not including 3**, and
  **`ClickHouse.Driver` 1.4.0 up to but not including 2**.
- **`UseBigDecimalForDapper` on the connection settings**, or the wide
  `UseBigDecimal` from [`PetToys.BigDecimal.ClickHouse`][clickhouse-package-url].
  Without either, a read fails naming `UseCustomDecimals` and a write fails with
  `InvalidCastException` from the driver. Neither narrows a value.
- **Trimming and Native AOT: this package works under neither, and both of its
  dependencies are the reason.** Its own marking says only that this assembly's
  code produces no trim, single-file or AOT diagnostic.
  - **Dapper**, measured by publishing and running: under `PublishTrimmed`
    materialising an object throws `InvalidOperationException` about a missing
    constructor and an anonymous-object parameter stops binding; under
    `PublishAot` `SqlMapper.AddTypeHandler` throws `NotSupportedException` over
    `SqlMapper.TypeHandlerCache<T>` before any query runs. `Dapper` 2.1.79
    carries no `IsTrimmable` metadata and produces `IL2104` and `IL3053`.
  - **`ClickHouse.Driver`**, measured the same way: 1.4.0 publishes Native AOT
    and then throws `TypeInitializationException` on the first query, and
    carries no `IsTrimmable` metadata in either of its assemblies.

  If you publish trimmed, reach decimal columns through
  [`PetToys.BigDecimal.ClickHouse`][clickhouse-package-url] directly, which is
  what its own trimming probe covers.

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
[nuget-url]: https://www.nuget.org/packages/PetToys.BigDecimal.ClickHouse.Dapper/
[nuget-v-badge]: https://img.shields.io/nuget/v/PetToys.BigDecimal.ClickHouse.Dapper?style=flat-square&logo=nuget&label=version
[nuget-dt-badge]: https://img.shields.io/nuget/dt/PetToys.BigDecimal.ClickHouse.Dapper?style=flat-square&logo=nuget
[test-badge]: https://img.shields.io/github/actions/workflow/status/pet-toys/big-decimal/test.yml?branch=dev&style=flat-square&logo=github&label=test
[test-url]: https://github.com/pet-toys/big-decimal/actions?query=workflow%3Atest+branch%3Adev
[dotnet-badge]: https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet
[license-badge]: https://img.shields.io/github/license/pet-toys/big-decimal?style=flat-square&color=blue
[license-url]: https://www.apache.org/licenses/LICENSE-2.0
[core-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Core/
[clickhouse-package-url]: https://www.nuget.org/packages/PetToys.BigDecimal.ClickHouse/
