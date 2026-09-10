# Contributing

Thanks for taking the time to contribute! This project is a small, focused
arbitrary-precision decimal type plus the database helpers around it, so
contributions of any size are welcome.

## Ways to contribute

- [Report a bug](https://github.com/pet-toys/big-decimal/issues/new?template=bug_report.yml).
- [Request a feature](https://github.com/pet-toys/big-decimal/issues/new?template=feature_request.yml).
- Improve the documentation.
- Open a pull request against the `dev` branch.

For anything beyond a small fix, please open an issue first so the approach can
be discussed before you invest time in a pull request.

## Repository layout

The solution ([`big-decimal.slnx`](../big-decimal.slnx)) holds six projects
under `src/` and one test project each:

| Project | Contents |
| ------- | -------- |
| `src/PetToys.BigDecimal.Core` | The `BigDecimal` type. No runtime dependencies. |
| `src/PetToys.BigDecimal.Npgsql` | PostgreSQL `numeric` helpers, on top of Npgsql. |
| `src/PetToys.BigDecimal.ClickHouse` | ClickHouse `Decimal*` helpers, on top of ClickHouse.Driver. |
| `src/PetToys.BigDecimal.Npgsql.EntityFrameworkCore` | `numeric` columns as `BigDecimal` properties, on top of the Npgsql helpers. |
| `src/PetToys.BigDecimal.Npgsql.Dapper` | The same columns through Dapper, on top of the Npgsql helpers. |
| `src/PetToys.BigDecimal.ClickHouse.Dapper` | The ClickHouse decimal family through Dapper, on top of the ClickHouse helpers. |

All six are published, in lockstep: one release tag versions every package, so
a change to one of them ships a new version of the other five as well. Each
adapter owns only value mapping - the caller supplies an already configured
connection or data source - and reaches the core's internal wire codecs through
`InternalsVisibleTo` rather than through public API. Those codecs are internal
on purpose; the supported surface is the mapping each adapter exposes. The three
projects above the adapters map no value of their own: each carries a
framework's registration over the handler the adapter beneath it installs.

None of the six project folders is its namespace root: all of them, and their
test projects, pin `RootNamespace` to `PetToys.BigDecimal`. In the core that is
because the `.Core` suffix distinguishes the package and would be noise in the
API. In the adapters it is load bearing for a different reason: from any
namespace under `PetToys.BigDecimal` that does not hold the type, the identifier
`BigDecimal` binds to the namespace instead, every mention of the type is
CS0118, and a `using` alias does not override it. A new file belongs in the
namespace its folder implies below that root - `Numerics/Foo.cs` in
`PetToys.BigDecimal.Numerics` - and a `Release` build fails on IDE0130 if it is
not.

Four solution filters narrow the build: `big-decimal.build.slnf` (the packages
only, which is what the release pipeline packs), `big-decimal.tests.slnf` (the
test projects, which is what CI runs), `big-decimal.integration.slnf` (the tests
that need a database server) and `big-decimal.aot.slnf` (the probe applications
under `probe/`).

Each package has its own `README.md` next to the project file - that file is the
one shipped inside the `.nupkg`. The repository-root `README.md` is the landing
page and is not packed.

## Getting started

The repository uses the .NET SDK version pinned in [`global.json`](../global.json)
and multi-targets `net8.0`, `net9.0`, and `net10.0`.

```bash
git clone https://github.com/pet-toys/big-decimal.git
cd big-decimal

dotnet restore
dotnet build -c Release
dotnet test
```

`Release` builds treat warnings as errors and enforce code-style and analyzer
rules, so build with `-c Release` before opening a pull request to catch the
same issues CI will. `Debug` builds additionally enable
`CheckForOverflowUnderflow`, so run the tests in `Debug` too when you touch
arithmetic.

The integration tests check both database wire formats against the servers that
define them, and they spin up real PostgreSQL and ClickHouse instances with
[Testcontainers](https://testcontainers.com/) to do it. A running Docker engine
executes them; without one they skip, with a reason naming the server, and the
rest of the suite still passes, so an offline `dotnet test` is green rather than
red. On a continuous integration runner the same state is a failure instead - a
leg that skipped every server test would otherwise report success over a suite
that ran nothing - and the switch is the `CI` environment variable, which GitHub
Actions always sets.

They are tagged `Category=Integration`, which the test legs of `test.yml`
exclude with `--filter Category!=Integration`; you can do the same for a run
without Docker, though skipping does it for you. They have a leg of their own,
`integration.yml`, over `big-decimal.integration.slnf`. It is deliberately not a
job in `test.yml`: that workflow is called by the release pipeline, so a
container that fails to pull would block publishing a package.

Trimming and Native AOT are covered the same way, by `aot.yml` over
`big-decimal.aot.slnf`. Every package is marked `IsAotCompatible`, and a clean
analyzer pass is not evidence that a marked package still works, so the workflow
publishes a probe application per package that has one - trimmed on every target
framework, and Native AOT on the newest where the driver allows it - and runs
it, the two adapter probes against a real server. It is out of `test.yml` for the same reason as the
integration leg, and out of the required checks on top of it: it needs a C++
toolchain and two containers.

Arithmetic, formatting and parsing are also covered by a randomised suite that
checks every result against a `BigInteger` or `System.Decimal` oracle. It is
deterministic by default, so an unconfigured run executes the same cases
everywhere and a failure reports the seed that produced it. Two environment
variables turn it into a soak: `BIGDECIMAL_FUZZ_CASES` raises the case count per
test from 2000, and `BIGDECIMAL_FUZZ_SEED` moves it onto ground the default run
never visits.

```bash
BIGDECIMAL_FUZZ_SEED=305441741 BIGDECIMAL_FUZZ_CASES=100000 dotnet test big-decimal.tests.slnf
```

The harness has a README of its own next to it, in
`test/PetToys.BigDecimal.Core.Tests/Numerics/Harness`. Read it before adding an
oracle: an oracle that reads the implementation it checks agrees with it by
construction, including where it is wrong.

Package versions are managed centrally
([`Directory.Packages.props`](../Directory.Packages.props) for the packages,
[`test/Directory.Packages.props`](../test/Directory.Packages.props) for the
tests), so add a `PackageReference` without a version and pin the version
there, as a `[x.y.z,)` range.

## Pull requests

- Branch off `dev` and target `dev`.
- Keep each pull request focused on a single change.
- Link the related issue (for example, `Closes #123`).
- Add or update tests for any behavioral change.
- Make sure `dotnet build -c Release` and `dotnet test` both pass locally.

Commit messages and pull request descriptions should be written in English and
describe the change in plain, neutral terms.

## Code style

Most conventions are enforced automatically by the analyzers and
`.editorconfig`, so a clean `Release` build is the source of truth. The
guidelines below capture the conventions that are not fully machine-checked:

- Use `PascalCase` for type, method, property, and constant names.
- Use `camelCase` for parameters and local variables.
- Prefix private fields with an underscore (`_field`).
- Prefix interfaces with `I`.
- Use language keywords (`int`, `string`) rather than framework type names
  (`Int32`, `String`).
- Use boolean-style prefixes (`Is`, `Has`, `Can`, `Any`) for boolean members.
- Use braces around any statement that spans more than one line; a statement
  written on the same line as its controlling keyword (`if`, `while`, `using`,
  `lock`, and the rest) needs none.
- Do not use Hungarian notation.

Nullable reference types are enabled project-wide, so do not add `#nullable`
directives to individual files. `ImplicitUsings` is disabled, so every file
spells out its `using` directives.

### Tests

Tests use xUnit and follow the `Method_State_ExpectedResult` naming pattern
(for example, `Parse_MoreFractionalDigitsThanScale_RoundsHalfToEven`). Keep test
data close to the tests that use it, and prefer deterministic tests over ones
that depend on a container, the network, or timing - cover the arithmetic and
formatting rules with plain in-memory cases and reserve the Testcontainers-based
tests for the actual database round trip.

Arithmetic correctness is the core concern of this repository: a behavioral
change without a test that pins it down will not be merged.
