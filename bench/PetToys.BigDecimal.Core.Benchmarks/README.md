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

A full run is 158 benchmarks and took about an hour and a half when the
baseline was taken. Most of the time you want one group:

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

## Comparing against the baseline

`BASELINE.md` was taken on one machine, and it says which. Two rules follow:

- **Ratios travel.** Comparing `Ratio` against the baseline's `Ratio` is valid
  from any machine — that is the whole reason the budgets are written as ratios.
- **Durations do not.** `Mean` is only comparable within the environment the
  baseline was recorded in. A different processor, a laptop on battery or a
  power plan that scales frequency, a virtual machine, a machine doing anything
  else at the same time — any of these moves `Mean` without anything in the
  package changing.

A result is not comparable against the baseline at all if the run used a
different job. `--job short` and `--job dry` trade iterations for wall-clock;
they are for a quick look while working, not for a number anyone quotes.
`BASELINE.md` is taken with the default job, and a comparison has to be too.

When a change alters the performance of a measured operation, re-take the
baseline and commit it with that change. Nothing enforces this: the value of a
recorded baseline is exactly the discipline of keeping it current.

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
