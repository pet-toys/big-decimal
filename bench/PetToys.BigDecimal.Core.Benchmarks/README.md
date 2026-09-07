# Benchmarks

The performance measurement for `PetToys.BigDecimal.Core`. Four of the
package's acceptance criteria are ratios against `System.Decimal`, and this
project is where those ratios come from.

Nothing here is a gate. No build, pull request or release fails because of a
number in this project — see [Why this is not in CI](#why-this-is-not-in-ci).

## Running it

From the repository root. The project multi-targets, so a framework has to be
named:

```bash
dotnet run -c Release -f net10.0 --project bench/PetToys.BigDecimal.Core.Benchmarks -- --filter "*"
```

A full run is over 150 benchmarks and takes about two hours on the machine this
is measured on. Most of the time you do not want one.

The benchmarks an acceptance criterion is actually read from carry the `budget`
category, and run on their own:

```bash
dotnet run -c Release -f net10.0 --project bench/PetToys.BigDecimal.Core.Benchmarks -- --anyCategories budget
```

That is the run to take when a change has to answer to the criteria. It leaves
out what is measured only so that a change in it stays visible: the conversions,
the scale changes, and the group that measures the division primitive against
the form it replaced. The subset is declared by an attribute on each class, in
`BenchmarkCategories.cs`, so it cannot drift from this document.

Or one group:

```bash
dotnet run -c Release -f net10.0 --project bench/PetToys.BigDecimal.Core.Benchmarks -- --filter "*DivideBenchmarks*"
```

The filter matches on the full name, so `*Parse*` takes all four parsing
classes and `*.Measured` takes every measured method without its baseline —
though a run without the baselines has no ratio column, which is usually not
what you want.

To measure every supported runtime in one report:

```bash
dotnet run -c Release -f net10.0 --project bench/PetToys.BigDecimal.Core.Benchmarks -- --filter "*" --runtimes net8.0 net9.0 net10.0
```

`--list flat` prints the benchmark names without running anything, which is the
quickest way to write a filter that matches what you meant.

## Where the output lands

`BenchmarkDotNet.Artifacts/results/` beside the built benchmark assembly, which
for the command above is
`bench/PetToys.BigDecimal.Core.Benchmarks/bin/Release/net10.0/`. Several formats
land there; the one that matters is `*-report-github.md`, the format
[`BASELINE.md`](BASELINE.md) is a copy of, and it opens with the processor,
operating system, SDK and runtime of the run.

The location is pinned by the configuration rather than left at BenchmarkDotNet's
default, which is the working directory the run was launched from. Two runs
launched from two directories would otherwise leave two artifact sets that
neither overwrites nor mentions the other, and a week later the stale one looks
exactly like the fresh one. Pinning it to the assembly also keeps the target
frameworks apart, which is what you want: a `net8.0` run is not a `net10.0`
run's result.

The artifacts directory is git-ignored, as is everything under `bin/`.
`BASELINE.md` is a deliberate copy kept outside it, and it is the only run
output this repository keeps.

A run overwrites only the reports of the classes it ran. A `--filter` on one
class leaves every other report exactly where the previous run left it, and
nothing in the directory says which run each file came from. So the directory
is not a run: it is whatever the last few runs happened to leave. Take a
baseline from one full run, and read the file timestamps before assembling
anything out of it. `BASELINE.md` was once assembled with two of its sixteen
sections belonging to an experiment that had already been reverted, and the
file contradicted its own summary for as long as that lasted.

## Reading a run

| Column      | What it is                                                     |
| ----------- | -------------------------------------------------------------- |
| `Mean`      | The average duration of one operation                           |
| `Ratio`     | `Mean` divided by the baseline's — the number the budgets use   |
| `Allocated` | Bytes allocated per operation, from the memory diagnoser        |

In the budgeted classes the `decimal` method is named `Baseline` and the
`BigDecimal` one `Measured`, and `Ratio` is `Measured / Baseline` within each
combination of parameters. The budgets are: 3x for addition, subtraction and
multiplication, 10x for division and remainder, 3x for parsing and for
formatting. A budget is met only if the worst ratio across the operand shapes
is inside it.

Classes with no `Baseline` method carry no budget and print no ratio. Those are
the operations no criterion is stated over, and the three- and four-word widths,
which `decimal` cannot represent at all.

`Allocated` is reported here and enforced in the test suite, but only for the
operations the suite's allocation inventory covers, and that is not all of them.
`TryFormat` appears in the inventory three times and every one of them passes
the default format specifier; one further case elsewhere in the suite uses
`F250`. No grouped format is measured anywhere. That is why the recorded
baseline shows 64 bytes on the `N` specifier with a green suite behind it: the
allocation is real, it is a known defect, and no test covers the path it is on.

So a non-zero `Allocated` row means one of two things, and they call for
opposite reactions. If the operation is in the inventory, the fix is not in this
project — a test should have failed first, and a benchmark finding it instead is
itself the more interesting result. If the operation is outside the inventory,
this project is the only thing measuring it, and closing the gap means adding
the case to the inventory as well as fixing the allocation.

## Whether a run may be used at all

This runs on a working developer machine, not on a reserved rig, and two runs
days apart do not measure the same machine. Between a run recorded on
2026-09-02 and one taken on 2026-09-07, `System.Decimal` itself, whose code
nobody here touches, moved from 4.34 ns to 7.34 ns on addition and from 15.68 to
33.42 on division. A run taken in that state reported multiplication at 4.15x
against a 3x budget, on a path that had not been modified at all.

So a run declares itself usable, or not, before anything is quoted from it. Two
tests, both read off the report the run just produced:

- **Precision.** For every row a criterion is read from, `Error` must be at most
  2% of that row's `Mean`. A good run sits near a tenth of that. The discarded
  run above had hashing rows at 5.5%.
- **The canary.** The `System.Decimal` rows are code this package does not
  change, so the ratios between them belong to the machine and the runtime
  rather than to the package. `BASELINE.md` records those ratios. If any of them
  differs by more than 15% from the recorded value, the machine is not the
  machine the budgets were drawn on and no budget is graded from that run,
  however reasonable its own rows look. On the discarded run, divide over add
  had moved 26%, parse over add 24% and hash over add 39%.

A run that fails either test is discarded whole, not in part, and what it
measured is written down so the next attempt does not rediscover it. Allocation
figures survive: they are counts, not durations, and travel anywhere.

A result is not usable either if the run used a different job. `--job short` and
`--job dry` trade iterations for wall-clock; they are for a quick look while
working, not for a number anyone quotes. The record is taken with the default
job.

## Claiming that something got faster

Not by comparing a run taken after the change against `BASELINE.md`. That
comparison is between two machines that happen to share a case.

Instead, carry the implementation being replaced into this project and measure
it beside the new one, in one group, in one run. The runner interleaves them on
the same operands minutes apart, and the `Ratio` column is then the claim.
`DivisionPrimitiveBenchmarks` is the worked example: the `UInt128` division the
package used until 2026-09-07 is its baseline arm, and the primitive that
replaced it is measured against it.

Where the replaced implementation cannot be carried, because that would mean two
versions of a public type in one process, state the claim as a relationship
between rows of the same run instead. An exact division against an inexact one
over the same dividend, or a hash of a value carrying trailing zeros against the
same value without them, are both such relationships, and both are criteria in
their own right.

## What `BASELINE.md` is

A dated account of one run that passed the tests above, carrying the machine it
ran on and the canary ratios read off it. It is not a reference that later runs
are compared against, and durations are never compared across runs.

Re-take it when a change alters the performance of a measured operation, from
one conforming run rather than by pasting sections from several. A targeted
re-run overwrites only the classes it names, so a file assembled out of the
artifacts directory can mix two states of the code and read as one.

## The operand set

`Operands.cs` holds every value the benchmarks use, as literals. It is
deliberately **not** shared with the verification harness in
`test/PetToys.BigDecimal.Core.Tests/Numerics/Harness/`, and unifying the two
would be a mistake in both directions:

- That generator's contract is to randomise. A benchmark must not: two runs
  have to do identical work or their numbers cannot be compared. Drawing from
  the generator with a fixed seed would only move the problem — comparability
  would then depend on the generator never changing.
- A project reference from here to the test project would pull xunit into this
  assembly, and BenchmarkDotNet copies an assembly's dependencies into every
  job it generates.

The duplication is about a dozen string literals. It is the cheaper of the two
costs.

The values are chosen so that the `decimal` baseline computes its result
exactly, without rounding to fit its 96-bit mantissa. A baseline that silently
rounds is measuring less work than the benchmark it anchors.

## Why this is not in CI

Neither `big-decimal.build.slnf` nor `big-decimal.tests.slnf` names this
project, so `dotnet pack` and `dotnet test` never see it, and no workflow runs
it. It is in `big-decimal.slnx`, so it is still compiled and analysed with
everything else.

GitHub's hosted runners are shared, virtualised and subject to noisy
neighbours. Their run-to-run variance on microbenchmarks is wider than the
margin between 3x and 4x, so a gate there would fail on noise — and a gate that
fails on noise gets switched off within a week, leaving the repository with a
disabled gate instead of an honest manual measurement.
