# Probes

Three console applications, one per published package, that are published
trimmed and Native AOT and then run. They exist because the analyzers cannot
answer the question they are here for: a build in which the trim, single-file
and AOT analyzers report nothing and a binary that throws on first use look
identical from inside the build.

They are not tests. Nothing here is discovered by `dotnet test`, and the
projects are in neither `big-decimal.build.slnf` nor `big-decimal.tests.slnf`;
`big-decimal.aot.slnf` selects the three of them. The leg that runs them is
`.github/workflows/aot.yml`.

Each probe runs a fixed list of checks against values captured from the jitted
build, exits non-zero on the first mismatch, and compares the number of checks
it actually ran against a constant it declares. A binary whose checks were
themselves trimmed away fails rather than exiting zero quickly.

## Running them

From the repository root. The projects multi-target, so a framework has to be
named.

Trimmed, which needs no toolchain beyond the SDK:

```bash
dotnet publish probe/PetToys.BigDecimal.Core.Probe -c Release -f net10.0 -r win-x64 -p:PublishTrimmed=true -o out/trim-core
```

Native AOT, which needs a C++ toolchain:

```bash
dotnet publish probe/PetToys.BigDecimal.Core.Probe -c Release -f net10.0 -r win-x64 -p:PublishAot=true -o out/aot-core
```

Then run the executable in the output directory. The exit code is the result;
the output says what was checked.

## On Windows, put the Visual Studio Installer directory on PATH

Native AOT needs `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on
`PATH`. Neither the Visual Studio installer nor the .NET SDK puts it there, so
this is a step rather than a broken installation.

Without it the publish fails as `MSB3073 ... exited with code 123`, with a
linker command that has an error message glued to the front of it. The cause is
worth knowing, because the message names neither `PATH` nor a missing toolchain:
`vcvarsall.bat` calls `vswhere.exe` by bare name, the `'vswhere.exe' is not
recognized` failure goes to stderr, the `> NUL` inside ILCompiler's
`findvcvarsall.bat` redirects only stdout, and the `Exec` that calls that script
reads the first line of its console output as the linker directory. The script
still exits 0, which is why this surfaces as a corrupt path rather than as a
missing toolchain.

## The two adapter probes need a server

They read a connection string from the environment and fail when it is absent.
They do not skip: a leg that can report success over a probe that connected to
nothing is not a check.

| Variable | Server |
| -------- | ------ |
| `BIGDECIMAL_PROBE_POSTGRES` | PostgreSQL, `Host=...;Port=...;Username=...;Password=...;Database=...` |
| `BIGDECIMAL_PROBE_CLICKHOUSE` | ClickHouse over HTTP, `Host=...;Port=...;Username=...;Password=...;Database=...` |

The server is started by whoever runs the probe, not by the probe. A container
library inside the binary would bring a reflection-heavy closure into the thing
being measured, and its own trim warnings would be read as the package's. The
leg starts the same images the integration fixtures pin, tag and digest both.

## What each probe answers, and the two limits found

The core probe covers formatting and parsing at both ends of the range, a
culture whose separators differ from the invariant one, and the JSON converter
in three forms. Two of those forms go through reflection-based serialization,
which is disabled under `PublishTrimmed` as well as under Native AOT, so there
the expectation is the refusal and the source-generated `JsonSerializerContext`
is the form that must work.

The Npgsql probe writes and reads a value `System.Decimal` cannot represent,
plus a `numeric[]`. It chooses its builder by `RuntimeFeature.IsDynamicCodeSupported`:
`NpgsqlSlimDataSourceBuilder` where dynamic code is gone, the full builder
otherwise, so one leg exercises both overloads of `UseBigDecimal`.

The ClickHouse probe is published trimmed only, and the reason is in the driver
rather than in the package. `ClickHouse.Driver` 1.4.0 publishes Native AOT and
then throws on first use: the static constructor of its `TypeConverter` calls
`TupleType.BuildTupleFactory`, which looks for a constructor of
`System.Tuple<double, double>` by reflection and does not find it after ILC, so
the first `ExecuteReaderAsync` fails with a `TypeInitializationException` before
any of this package's code runs. Trimmed is unaffected.

That project also silences `IL2104` and `IL3053`, the per-assembly aggregates
saying a dependency produced warnings, because `ClickHouse.Driver` and its
`Microsoft.IO.RecyclableMemoryStream` dependency both do and warnings are errors
here. A consumer sees them as warnings and publishes; without the suppression
the probe would never be built and the question would go unanswered. Nothing
about this repository's own assemblies is silenced. To see what the driver was
actually warned about, publish with `-p:TrimmerSingleWarn=false`.
