# PetToys.BigDecimal.Npgsql.EntityFrameworkCore

[![NuGet Version][nuget-v-badge]][nuget-url] [![NuGet Downloads][nuget-dt-badge]][nuget-url] [![Unit Test][test-badge]][test-url] [![Target frameworks][dotnet-badge]][nuget-url] [![License][license-badge]][license-url]

Entity Framework Core mapping for [`PetToys.BigDecimal.Core`][core-url]: a
PostgreSQL `numeric` column becomes a `BigDecimal` property on an entity, read
and written exactly, with no value converter and no `System.Decimal` anywhere in
the path.

The route Entity Framework offers for an unknown numeric type is a
`ValueConverter<BigDecimal, decimal>`, and that is the precision loss this
project exists to prevent, applied silently at every property. This package is
the alternative.

Bring your own `DbContext` and data source; this package only deals with the
value mapping.

## Installation

```sh
dotnet add package PetToys.BigDecimal.Npgsql.EntityFrameworkCore
```

[`PetToys.BigDecimal.Npgsql`][npgsql-package-url] and
[`PetToys.BigDecimal.Core`][core-url] come along as dependencies.

> **Releases go to nuget.org**, so the command above is all that is needed.
> Prereleases are published to [GitHub Packages][gh-packages-url] instead: that
> feed has to be added to your `nuget.config`, and it requires a personal access
> token with `read:packages` even for a public package.

## Usage

There are two registrations and neither works alone. The adapter installs the
codec on the data source; this package tells Entity Framework which store type a
`BigDecimal` property has.

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PetToys.BigDecimal.Numerics;

await using var source = new NpgsqlDataSourceBuilder(connectionString)
    .UseBigDecimal()
    .Build();

var options = new DbContextOptionsBuilder<InvoiceContext>()
    .UseNpgsql(source, o => o.UseBigDecimal())
    .Options;
```

Then the property is an ordinary one:

```csharp
public sealed class Invoice
{
    public int Id { get; set; }

    public BigDecimal Total { get; set; }
}
```

Declare the column's precision where the schema has one, and it reaches both the
mapping and the generated DDL:

```csharp
modelBuilder.Entity<Invoice>().Property(x => x.Total).HasPrecision(38, 18);
```

`numeric[]` maps as well, to `BigDecimal[]`, `List<BigDecimal>` and their
nullable forms, and `BigDecimal?` needs nothing beyond the registration.

## Registering it does not change anything else

`BigDecimal` is for the columns that need it, not a replacement for
`System.Decimal` across an application. After the registration, `numeric` still
maps to `System.Decimal` by default and `numeric(p, s)` still maps to
`System.Decimal`; only a property whose CLR type is `BigDecimal` reaches this
mapping. Entities written before this package was referenced keep the types they
had, and scaffolding an existing database still produces `decimal` properties.

## A change of scale is a change

Numeric equality makes `1.0` equal `1.00`, which is right for the type and
useless for a change tracker: with Entity Framework's default comparer, moving a
property from `1.5` to `1.50` would leave the entity `Unchanged` and
`SaveChanges` would write nothing at all - no error, no wrong value, no update.
The mapping carries a comparer that compares the scale beside the value, so that
edit is saved like any other.

## What round-trips, and what does not

| Column | Coverage |
| ------ | -------- |
| `numeric(p, s)`, p up to 77 | Lossless. This covers `numeric(38, 18)`, the common money and blockchain precision, with room to spare. |
| `numeric` unconstrained, integer part within the magnitude | Accepted; fractional digits beyond what the magnitude leaves are rounded half to even. PostgreSQL allows 16383 of them, so a value read from such a column can lose digits silently. |
| `numeric` unconstrained, integer part beyond the magnitude | `OverflowException`. PostgreSQL allows 131072 integer digits. |
| `NaN`, `Infinity`, `-Infinity` | Lossless, as `BigDecimal.NaN`, `BigDecimal.PositiveInfinity` and `BigDecimal.NegativeInfinity`. PostgreSQL sorts `NaN` above every other `numeric` value where this type sorts it below every other value; both make `NaN` equal to itself. |

The type's own bounds are 77 significant digits, a largest magnitude of 2^256-1,
and a range of 1e-255 to approximately 1.157e77.

## A value the declared column cannot hold is refused here, not there

Where the model declares facets, a value that does not fit them is refused
before it is sent, as an `OverflowException` naming the value and the store
type. Two things do not fit: an integer part the column cannot hold, which
PostgreSQL itself rejects, and a fraction the column cannot keep, which
PostgreSQL silently rounds. Rounding a value you handed over whole is not
something this package does quietly.

Dropping trailing zeros is not rounding. `1.500` written into a `numeric(12,2)`
is `1.50` and loses nothing, so it is accepted like any other value, and comes
back at the column's scale.

The message cannot name the property. The mapping is handed the facets and a
parameter called `@p2`; it never learns which property the value came from. The
name is one level up, on the exception Entity Framework wraps ours in:

```csharp
try
{
    await context.SaveChangesAsync();
}
catch (DbUpdateException e) when (e.InnerException is OverflowException)
{
    foreach (var entry in e.Entries)
    {
        var name = entry.Metadata.DisplayName();

        foreach (var property in entry.Properties)
        {
            Console.WriteLine($"{name}.{property.Metadata.Name} = {property.CurrentValue}");
        }
    }
}
```

A column declared without facets is not checked at all. There is nothing to
violate: PostgreSQL's own maximum is far beyond this type's range.

## The scale you read back is the column's

A value goes out at its own scale and PostgreSQL applies the column's. `1.5`
written into a `numeric(12,4)` is stored and read back as `1.5000`; written into
an unconstrained `numeric` it stays `1.5`. The mapping does not rescale, and
within a declared scale the server is only padding with zeros, which is exact.
A property round-tripped through a faceted column therefore comes back at the
column's scale rather than at the value's, and a caller who wants the scale they
wrote has to declare a column that carries it.

## Queries run on the server, under the server's rules

Comparison, arithmetic, aggregation and a comparison against a constant all
translate to SQL, so a `BigDecimal` property is a first-class column rather than
something dragged to the client. What that means is that the values such a query
produces were computed by PostgreSQL, whose arithmetic is not this type's in two
places worth knowing:

- `round` is half away from zero on the server and half to even in this type, so
  `round(0.5)` is `1` there and `0` here.
- Division picks its own scale on the server: `1::numeric / 3::numeric` comes
  back with twenty fractional digits.

Nothing in this package reconciles the two. Where the server computes, the
server's rules apply.

## Requirements

- **Entity Framework Core 9 or 10**, through
  `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4 up to but not including 11. The
  package ships one assembly compiled against EF Core 9 for every target
  framework, which runs under both majors; the conventional shape - a `net10.0`
  asset built against EF Core 10 - cannot be consumed by an application on EF
  Core 9 at all, so it would lock a `net10.0` consumer to EF Core 10 before
  their build started. Both supported majors are covered by the test suite
  against a real server.
- **Entity Framework Core 8 is not supported**, and the reason is measured
  rather than a preference: the 8.0.x provider restores against this
  repository's Npgsql floor and compiles, then throws `TypeLoadException` on an
  internal driver type at first use, because it is bound to something `Npgsql`
  10 no longer has.
- **PostgreSQL 14 or later for the infinities**, which is where `numeric` gained
  the sign codes that carry them.
- **Trimming and Native AOT: this assembly is marked, and that is the whole of
  what it claims.** `IsAotCompatible` is set and the mapping code produces no
  trim, single-file or AOT diagnostic. The closure is another matter, measured
  rather than inferred: `Microsoft.EntityFrameworkCore` 9.0.1,
  `Microsoft.EntityFrameworkCore.Relational` 9.0.1 and
  `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4 carry no `IsTrimmable`, and of
  the four assemblies in this package's graph only `Npgsql` 10.0.3 does. An
  assembly attribute cannot vouch for Entity Framework's query pipeline, which
  is the subject of Microsoft's own compiled-model work rather than of this
  package.

## Forgetting a registration

Building a model with a `BigDecimal` property and no `UseBigDecimal` on the
options fails at model building rather than silently, with Entity Framework's
own message about the property type. Registering this package over a data source
that never had the adapter's `UseBigDecimal` applied fails at the first save,
with a
`DbUpdateException` wrapping an `InvalidCastException` from the driver. Neither
case narrows a value.

## Links

- [Source and documentation][repo-url]
- [Report an issue][issues-url]

## License

Provided under the [Apache License, Version 2.0][license-url].

[repo-url]: https://github.com/pet-toys/big-decimal
[gh-packages-url]: https://github.com/orgs/pet-toys/packages?repo_name=big-decimal
[issues-url]: https://github.com/pet-toys/big-decimal/issues
[nuget-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Npgsql.EntityFrameworkCore/
[nuget-v-badge]: https://img.shields.io/nuget/v/PetToys.BigDecimal.Npgsql.EntityFrameworkCore?style=flat-square&logo=nuget&label=version
[nuget-dt-badge]: https://img.shields.io/nuget/dt/PetToys.BigDecimal.Npgsql.EntityFrameworkCore?style=flat-square&logo=nuget
[test-badge]: https://img.shields.io/github/actions/workflow/status/pet-toys/big-decimal/test.yml?branch=dev&style=flat-square&logo=github&label=test
[test-url]: https://github.com/pet-toys/big-decimal/actions?query=workflow%3Atest+branch%3Adev
[dotnet-badge]: https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?style=flat-square&logo=dotnet
[license-badge]: https://img.shields.io/github/license/pet-toys/big-decimal?style=flat-square&color=blue
[license-url]: https://www.apache.org/licenses/LICENSE-2.0
[core-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Core/
[npgsql-package-url]: https://www.nuget.org/packages/PetToys.BigDecimal.Npgsql/
