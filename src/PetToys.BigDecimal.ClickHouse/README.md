# PetToys.BigDecimal.ClickHouse

[![NuGet Version][nuget-v-badge]][nuget-url] [![NuGet Downloads][nuget-dt-badge]][nuget-url] [![Unit Test][test-badge]][test-url] [![Target frameworks][dotnet-badge]][nuget-url] [![License][license-badge]][license-url]

ClickHouse helpers for [`PetToys.BigDecimal.Core`][core-url]: they map the
`Decimal32`, `Decimal64`, `Decimal128`, and `Decimal256` column types - whose
range and scale go well past `decimal` - onto `BigDecimal` when reading and
writing through [ClickHouse.Driver][ch-driver].

Bring your own configured connection; this package only deals with the value
mapping.

## Installation

```sh
dotnet add package PetToys.BigDecimal.ClickHouse
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

Ask for the mapping on the query that needs it:

```csharp
using ClickHouse.Driver;
using PetToys.BigDecimal.Numerics;

using var client = new ClickHouseClient(connectionString);

await using var reader = await client.ExecuteReaderAsync(
    "SELECT total FROM invoices WHERE id = 1",
    null,
    ClickHouseBigDecimal.CreateQueryOptions(),
    cancellationToken);

await reader.ReadAsync(cancellationToken);

BigDecimal total = reader.GetBigDecimal("total");
```

`Nullable(Decimal...)` and `Array(Decimal...)` come with it, as `BigDecimal?`
and `BigDecimal[]`. Every other column of the same row reads exactly as it would
without this package.

To write in bulk:

```csharp
await client.InsertBigDecimalAsync(
    "invoices",
    ["id", "total"],
    rows,
    options: null,
    cancellationToken);
```

`InsertBigDecimalAsync` reads the destination's column types from the server
once per call, because a width that disagrees with the column does not fail, it
stores a different number. A loader that inserts many batches should declare them
instead, through `InsertOptions.ColumnTypes`, which skips the lookup.

To write one value as a query parameter, annotate its type in the statement, as
ClickHouse requires, and build the connection from settings carrying the mapping:

```csharp
using ClickHouse.Driver;
using ClickHouse.Driver.ADO;

var settings = new ClickHouseClientSettings(connectionString).UseBigDecimal();

await using var connection = new ClickHouseConnection(settings);
await connection.OpenAsync(cancellationToken);

await using var command = connection.CreateCommand(
    "INSERT INTO invoices (id, total) VALUES (1, {total:Decimal256(6)})");
command.AddParameter("total", value);
await command.ExecuteNonQueryAsync(cancellationToken);
```

## The two scopes, and what the wide one changes

`CreateQueryOptions` maps one query. `UseBigDecimal` on
`ClickHouseClientSettings` maps every query on the connection, and the two are
not equivalent:

- Every decimal column read on that connection becomes a `BigDecimal`, including
  in code written before this package was referenced.
- `GetFieldType` keeps reporting the driver's own decimal type while `GetValue`
  returns a `BigDecimal`. The reader disagrees with itself, and nothing outside
  the driver can change it, so anything that builds a schema from the reader and
  then fills it - a `DataTable` above all - sees a column typed for one type
  receiving another.
- The read hook is consulted once per **value**, not once per column, so a wide
  result set pays for every column of every row.

It exists because `ClickHouseCommand` has no per-query hook: an ADO caller, or
anything layered on one, has no narrower option. Where `ClickHouseClient` is in
reach, prefer the per-query form.

`GetFieldValue<BigDecimal>` does not work in either scope and cannot be made to:
the driver casts its own value to the requested type before consulting the hook
that would have changed it. Use the accessors above.

Everything this package adds lives in the `ClickHouse.Driver` namespace, the one
a caller already imports, including the extensions on types that live below it:
`ClickHouse.Driver.ADO` is needed only where the snippet names
`ClickHouseClientSettings` or `ClickHouseConnection` itself.

## Requirements

- **`UseCustomDecimals=true` on the connection.** The mapping needs the driver's
  own arbitrary-precision decimals. With the option off, a value wider than
  `decimal` raises inside the driver before this package is reached, which
  nothing here can rescue. `UseBigDecimal` on the settings switches it on; the
  per-query form cannot, and says so by name if it meets a column the driver
  decoded through `decimal`.
- **`ClickHouse.Driver` 1.4.0, up to but not including 2.** That is the version
  this package's use of `IReadValueConverter` and `IParameterFormatter` was
  measured against, and the range is closed at the major because the driver
  passes the formatter its arguments in an order the interface does not declare.
  A driver major that reshapes that therefore fails at restore rather than at
  the first write; the ceiling moves once the new major has been tested against.

## Trimming yes, Native AOT no, and the reason is the driver

This assembly is marked `IsAotCompatible`, and its own code is gated by the trim,
single-file and AOT analyzers. That marking is per assembly and does not reach
`ClickHouse.Driver`, which is what decides the answer here.

**Trimmed publishing works.** It is verified by publishing a probe over this
package and running it against a real server, on every supported framework. The
publish does warn: `ClickHouse.Driver` 1.4.0 carries no `IsTrimmable` in either
`ClickHouse.Driver.dll` or `ClickHouse.Driver.Common.dll`, and neither does its
`Microsoft.IO.RecyclableMemoryStream` dependency, so you get `IL2104` for both -
"assembly produced trim warnings". That is expected, and it is the reason to
test your own application rather than to trust this paragraph.

**Native AOT does not work, and it fails at runtime rather than at publish.**
The driver compiles: the failure arrives on the first query, as a
`TypeInitializationException` out of `ClickHouse.Driver.Types.TypeConverter`,
whose static constructor calls `TupleType.BuildTupleFactory` to look up a
constructor of `System.Tuple<double, double>` by reflection, which ILC has
removed. Nothing in this package is on that path, and nothing in it can rescue
the call. If you need Native AOT against ClickHouse today, the way through is
the driver's own issue tracker, not a workaround here.

## What round-trips, and what does not

| Column | Coverage |
| ------ | -------- |
| `Decimal32(s)`, `Decimal64(s)`, `Decimal128(s)`, `Decimal256(s)` | Read losslessly, at every precision and scale ClickHouse allows. |
| The same, when writing | Lossless while the value fits the column's precision and width. Fractional digits beyond the column's scale are rounded half to even. |
| A value whose integer part exceeds the column | `OverflowException`, naming the column, its declared type, and which of the two bounds it crossed. |
| `NaN`, `Infinity`, `-Infinity` | Refused with `NotSupportedException` before anything is sent, naming the value and the width. No ClickHouse decimal type represents them. |

The type's own bounds are 77 significant digits, a largest magnitude of 2^256-1,
and a range of 1e-255 to approximately 1.157e77. Every ClickHouse decimal fits
inside them, which is why reading never overflows and never rounds, and writing
is the direction with a boundary. That is the opposite of the PostgreSQL adapter.

## Two things ClickHouse does that this package does not hide

**A round trip returns the column's scale, not the value's.** Writing 1.5 into a
`Decimal64(4)` reads back 1.5000. ClickHouse keeps the scale in the column type
and nowhere in the value, so there is nothing to restore it from; the values
remain numerically equal.

**Rounding is this package's, not the server's.** ClickHouse truncates toward
zero when it parses a decimal literal into a narrower column, and so does the
driver when it lowers a scale: both turn 0.135 into 0.13 at scale 2. This type
rounds half to even, giving 0.14, and every write path here rescales the value
itself so that neither of the other two is ever asked to. One rule, whichever
path a value takes.

## Links

- [Source and documentation][repo-url]
- [Report an issue][issues-url]

## License

Provided under the [Apache License, Version 2.0][license-url].

[repo-url]: https://github.com/pet-toys/big-decimal
[gh-packages-url]: https://github.com/orgs/pet-toys/packages?repo_name=big-decimal
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
