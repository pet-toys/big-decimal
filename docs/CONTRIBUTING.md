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

The solution ([`big-decimal.slnx`](../big-decimal.slnx)) holds three projects
under `src/` and one test project each:

| Project | Contents |
| ------- | -------- |
| `src/PetToys.BigDecimal.Core` | The `BigDecimal` type. No runtime dependencies. Published. |
| `src/PetToys.BigDecimal.Npgsql` | PostgreSQL `numeric` helpers, on top of Npgsql. Not implemented yet. |
| `src/PetToys.BigDecimal.ClickHouse` | ClickHouse `Decimal*` helpers, on top of ClickHouse.Driver. Not implemented yet. |

The two integration projects are placeholders: they carry a project file, a
README and a test project, and no code. Both set `IsPackable=false` so that the
release pipeline keeps compiling them without publishing an empty assembly. The
change that adds the first public type to one of them sets that back to `true`.

`PetToys.BigDecimal.Core` is the only project whose folder name is not its
namespace root: it and its test project pin `RootNamespace` to
`PetToys.BigDecimal`, because the `.Core` suffix distinguishes the package and
would be noise in the API. A new file there belongs in the namespace its folder
implies below that root - `Numerics/Foo.cs` in `PetToys.BigDecimal.Numerics` -
and a `Release` build fails on IDE0130 if it is not.

Two solution filters narrow the build: `big-decimal.build.slnf` (the packages
only, which is what the release pipeline packs) and `big-decimal.tests.slnf`
(the test projects, which is what CI runs).

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

They are tagged `Category=Integration`, which the three legs of `test.yml`
exclude with `--filter Category!=Integration`; you can do the same for a run
without Docker, though skipping does it for you. They have a leg of their own,
`integration.yml`, over `big-decimal.integration.slnf`. It is deliberately not a
job in `test.yml`: that workflow is called by the release pipeline, so a
container that fails to pull would block publishing a package.

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
