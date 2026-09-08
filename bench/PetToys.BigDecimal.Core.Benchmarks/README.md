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

A full run is 224 benchmarks. Extrapolated from the budgeted subset below, which
is 184 of them in 66 minutes, that is about 80 minutes; the two-hour figure this
file used to carry predates the current inventory and was never re-measured. You
almost never want a full run.

**Start from the classes your change touched.** A criterion is graded from the
rows it is read from and from nothing else in the report, so a change to
formatting is answered by the two formatting classes:

```bash
dotnet run -c Release -f net10.0 --project bench/PetToys.BigDecimal.Core.Benchmarks -- --filter "*Format*"
```

That is 24 minutes rather than 66. A single class of eight rows is under three
and a half minutes, which is what it costs to re-measure a criterion an earlier
run left undecided.

The benchmarks an acceptance criterion is read from carry the `budget` category
and run together:

```bash
dotnet run -c Release -f net10.0 --project bench/PetToys.BigDecimal.Core.Benchmarks -- --anyCategories budget
```

66 minutes for 184 rows. Take it when a change touches a path several criteria
are read from, or when the record is being re-taken - not as the reflex for
every change. It leaves out what is measured only so that a change in it stays
visible: the conversions, the scale changes, and the group that measures the
division primitive against the form it replaced. The subset is declared by an
attribute on each class, in `BenchmarkCategories.cs`, so it cannot drift from
this document.

Or one group:

```bash
dotnet run -c Release -f net10.0 --project bench/PetToys.BigDecimal.Core.Benchmarks -- --filter "*DivideBenchmarks*"
```

The filter matches on the full name, so `*Parse*` takes all four parsing
classes and `*.Measured` takes every measured method without its baseline —
though a run without the baselines has no ratio column, which is usually not
what you want.

**Nothing is graded from `--job short` or `--job dry`.** They are for a quick
look while working. A short job reports the disagreement between three
iterations, which is not the uncertainty of a mean taken from three, and a
disturbance outlasting a single case leaves that column narrow while moving the
ratio by a third: on 2026-09-08 a short run put subtraction at 3.48 with a
dispersion of 0.07, where two full runs of the same binary had it at 2.57 and
2.84. One of its cases had been measured through a disturbance that inflated
both arms, and no column of the report said so.

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
land there; the one that matters is `*-report-github.md`, which opens with the
processor, operating system, SDK and runtime of the run.

The location is pinned by the configuration rather than left at BenchmarkDotNet's
default, which is the working directory the run was launched from. Two runs
launched from two directories would otherwise leave two artifact sets that
neither overwrites nor mentions the other, and a week later the stale one looks
exactly like the fresh one. Pinning it to the assembly also keeps the target
frameworks apart, which is what you want: a `net8.0` run is not a `net10.0`
run's result.

The artifacts directory is git-ignored, as is everything under `bin/`.
[`BASELINE.md`](BASELINE.md) is kept outside it and is the only run output this
repository keeps; it records verdicts rather than durations.

A run overwrites only the reports of the classes it ran. A `--filter` on one
class leaves every other report exactly where the previous run left it, and
nothing in the directory says which run each file came from. So the directory
is not a run: it is whatever the last few runs happened to leave. Read the file
timestamps before assembling anything out of it. `BASELINE.md` was once
assembled with two of its sixteen sections belonging to an experiment that had
already been reverted, and the file contradicted its own summary for as long as
that lasted.

## Reading a run

| Column      | What it is                                                          |
| ----------- | ------------------------------------------------------------------- |
| `Mean`      | The average duration of one operation                                |
| `Ratio`     | `Mean` divided by the baseline's, the number the budgets are read in |
| `RatioSD`   | The dispersion of that ratio, which decides the verdict              |
| `StdDev`    | The dispersion of one row's own iterations, which decides whether that row is read at all |
| `Error`     | Half a 99.9% confidence interval on `Mean`; no rule reads it         |
| `Allocated` | Bytes allocated per operation, from the memory diagnoser             |

`RatioSD` and `StdDev` are both dispersions and they answer different questions.
`RatioSD` belongs to a pair of interleaved arms and says how well the ratio is
determined; `StdDev` belongs to one row and says how much its own iterations
disagreed. The first gives the verdict, the second decides whether the row is
admissible. `Error` is in the report and is read by nothing: the rule that used
it graded a run as a whole and is gone.

In the budgeted classes the `decimal` method is named `Baseline` and the
`BigDecimal` one `Measured`, and `Ratio` is `Measured / Baseline` within each
combination of parameters. The budgets are: 3.5x for addition, subtraction and
multiplication, 10x for division and remainder, 3x for parsing and for
formatting. The verdict is taken from the worst operand shape, by the rule in
[Grading a criterion](#grading-a-criterion).

Classes with no `Baseline` method carry no budget and print no ratio. Those are
the operations no criterion is stated over, and the three- and four-word widths,
which `decimal` cannot represent at all.

`Allocated` is reported here and enforced in the test suite, but only for the
operations the suite's allocation inventory covers, and that is not all of them.
The inventory is the authority on what is covered; read it rather than assuming
a member is in it because its name appears once. It grew a case per standard
format specifier on both overloads after a benchmark found grouped formatting
allocating 64 bytes with the whole suite green - the allocation was real, and no
test covered the path it was on.

So a non-zero `Allocated` row means one of two things, and they call for
opposite reactions. If the operation is in the inventory, the fix is not in this
project — a test should have failed first, and a benchmark finding it instead is
itself the more interesting result. If the operation is outside the inventory,
this project is the only thing measuring it, and closing the gap means adding
the case to the inventory as well as fixing the allocation.

## Grading a criterion

A budget is a ratio, so the verdict comes from the ratio and the dispersion the
report prints beside it, in the run that produced them, and from nothing else.
Writing `r` for `Ratio` and `s` for `RatioSD`, on the worst operand shape of the
class:

| Condition          | Verdict                |
| ------------------ | ---------------------- |
| `r + 2s` ≤ budget  | met                    |
| `r - 2s` > budget  | missed                 |
| otherwise          | undecided in that run  |

Undecided is an answer, not a failure. Re-measure that one class, or conclude
the budget is wrong. Do not run the whole subset again and keep whichever
verdict turns up: that is choosing the evidence after seeing it.

A row whose `StdDev` is more than 5% of its `Mean` is not read at all, and a
criterion left without a readable row for its worst shape is undecided. The
median row of a clean run here is 0.7% and the worst 3.6%, so the bound trips on
nothing ordinary; the two disturbed rows that prompted it sat at 5.8% and 32%,
one of them reading a hashing ceiling at 4.02 against 2.5 while the same ceiling
read 2.08 and 2.12 in the runs either side.

A criterion stated between two rows of this type rather than against `decimal` -
the hashing ceilings, the exact-against-inexact division - is computed from the
two means, with their relative deviations combined in quadrature. Those need a
second measurement more often, because the runner interleaves the two arms of
one group but not two different methods, so none of the noise cancels.

Each criterion answers for itself. A noisy row in one class says nothing about
another class, and there is no whole-run verdict to fail.

**Why the ratio and not the rows.** The runner interleaves the two arms of a
group, so both meet the same machine minute by minute and the shared part of
the noise divides out of the quotient. The rows are far more dispersed than the
ratio they produce: on 2026-09-08 addition's `decimal` row reported an `Error`
of 2.30% of its mean while the ratio built from it was 3.00 with a dispersion of
0.06. This file previously asked for `Error` under 2% of `Mean` on every row a
criterion was read from, and discarded two 66-minute runs on rows belonging to
operations neither run was commissioned for. That test, the `System.Decimal`
canary that stood beside it, and the whole-run discard are gone.

**What the rule cannot see.** Dispersion measures how much the iterations of one
case disagreed. A disturbance that outlasts the case inflates every iteration
equally and leaves the column narrow, so a disturbed case is reported as a
precise one. It happens on this machine: single cases have been seen inflated by
a quarter to a half while their neighbours in the same run were normal, at under
2% dispersion. There is no column that catches it. The defences are the ones
already in place - enough iterations for a transient to be diluted or removed as
an outlier, and budgets with margin over what a disturbed case can add - and a
budget that sits inside the spread of undisturbed runs is treated as wrongly set
rather than as a measurement to repeat.

This is a working developer machine, not a reserved rig. Durations taken days
apart do not measure the same machine: between 2026-09-02 and 2026-09-07,
`System.Decimal` itself, whose code nobody here touches, moved from 4.34 ns to
7.34 ns on addition. That is why a verdict is read inside one run and why no
duration is ever compared across runs. Allocation figures are the exception:
they are counts, not durations, and travel anywhere.

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

`ExactDivisionBenchmarks` carries three such rows rather than two, over one
dividend: a quotient that is exact at the difference of the operands' scales, one
that is exact only after the dividend is lifted by the divisor's own factors, and
one that is exact at neither. They are the three depths the division searches to,
so work taken out of the search is the difference between adjacent rows of that
table. Only the first against the last is a criterion; the middle row is there to
be read.

## What [`BASELINE.md`](BASELINE.md) is

A dated record of what the budgeted criteria last measured: the machine, the
date, the job, the classes that were run, and one row per criterion with its
ratio, dispersion and verdict. It is not a reference that later runs are
compared against, and it does not carry durations - a table of nanoseconds that
no rule permits comparing against anything is an invitation to compare it.

Re-take the affected rows when a change alters the performance of a measured
operation, from the run that change took. The full report stays in the
git-ignored artifacts directory, which is where a reader who wants nanoseconds
should look.

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
