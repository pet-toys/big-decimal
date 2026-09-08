# Baseline

What the budgeted criteria last measured, and on what evidence. One row per
criterion: the ratio it was read at, the dispersion beside it, the operand shape
it came from, and the run that produced it.

This is not a reference. Durations are never compared across runs on this
machine, so this file carries none: a table of nanoseconds nothing may be
compared against is an invitation to compare it. The full reports live in the
git-ignored `BenchmarkDotNet.Artifacts/` directory beside the built assembly.
How a verdict is reached is in [`README.md`](README.md#grading-a-criterion).

Rows may come from different runs. A change measures the classes it touches, so
no single run covers everything, and each row says where it came from rather
than the file carrying one date for all of them.

## The machine

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700H 2.30GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.400
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
```

`DefaultJob` is part of the record. A run taken with `--job short` or `--job
dry` grades nothing, for the reason in the README.

## Runs behind these rows

| Run | Date       | Scope                    | Code                       | Cost   |
| --- | ---------- | ------------------------ | -------------------------- | ------ |
| A   | 2026-09-08 | `--anyCategories budget` | `formatting-parity`        | 66 min |
| B   | 2026-09-08 | `*ComparisonBenchmarks*` | `c33486b`                  | 16 min |
| C   | 2026-09-08 | `*Parse*`                | `parsing`, the set form    | 6 min  |
| D   | 2026-09-08 | `*Parse*`                | `parsing`, as it ships     | 13 min |

Run A was taken on the `formatting-parity` work before it merged, which differs
from `c33486b` only in the formatting path, so the rows below that are not
formatting measure code identical to run B's.

Runs C and D are the same four classes on two forms of the same fix. C measured the
trim as a six-character set, which the change then replaced with a predicate costing
about three nanoseconds less per parse; D measured what ships. **The parse rows are
recorded from C**, a disturbed measurement of the exact code being worse than a clean
measurement of a slower one: C is a ceiling, and what ships is a little faster than
the table says. What D was good for is below.

A third run of the same four classes sits between them and produced nothing: eleven
minutes, six of its eight baseline arms inflated the way D's five were. It is named
here because a discarded run that goes unnamed is a run somebody repeats.

## Verdicts

| Criterion                          | Budget | Ratio | Dispersion | Shape                 | Verdict | Run |
| ---------------------------------- | -----: | ----: | ---------: | --------------------- | ------- | --- |
| `Add`                              |   3.5x |  3.00 |       0.06 | one word, aligned     | met     | A   |
| `Subtract`                         |   3.5x |  2.57 |       0.05 | two words, misaligned | met     | A   |
| `Multiply`                         |   3.5x |  3.03 |       0.03 | one word, misaligned  | met     | A   |
| `Divide`                           |    10x |  5.52 |       0.08 | two words, aligned    | met     | A   |
| `Remainder`                        |    10x |  3.02 |       0.04 | one word, aligned     | met     | A   |
| `Parse`, `char`                    |     3x |  0.95 |       0.00 | one word              | met     | C   |
| `Parse`, UTF-8                     |     3x |  1.17 |       0.02 | one word              | met     | C   |
| `TryParse`, `char`                 |     3x |  0.97 |       0.00 | one word              | met     | C   |
| `TryParse`, UTF-8                  |     3x |  1.14 |       0.00 | one word              | met     | C   |
| `TryFormat`, `char`                |     3x |  2.67 |       0.02 | two words, `#,##0.00` | met     | A   |
| `TryFormat`, UTF-8                 |     3x |  2.80 |       0.03 | two words, `#,##0.00` | met     | A   |
| Exact division against inexact     |    1.0 |  0.35 |       0.00 | `100 / 10` vs `/ 3`   | met     | A   |
| Hashing, widened against narrow    |   2.5x |  2.13 |       0.05 | two words, misaligned | met     | B   |
| Hashing, nineteen zeros against one|   1.5x |  1.43 |       0.02 | one word, aligned     | met     | B   |
| Zero allocations                   | always |     - |          - | every row             | met     | A   |

## What the numbers do not say

**Run D is the clearest evidence yet that grading the ratio is right, and the
clearest case of where it still fails.** Five of its eight baseline rows measured
`System.Decimal` itself at 83% to 114% above what runs A and C cost for the same
unchanged BCL code, and the run took 13 minutes against C's 6. Every row still
reported a standard deviation inside 5% of its own mean, so the row rule saw
nothing: the disturbance outlasted whole cases and inflated their iterations
evenly.

Six of the eight shapes nevertheless came back within 0.06 of their run C ratio, and
five of those within 0.05, baselines twice as slow and all. That is interleaving
doing exactly what it is there for: both arms of a pair are measured under the same
disturbance and the shared part divides out.

The exception is the row that matters. `TryParse`, `char`, one word came back at
**0.46** in run D, against 0.97 in run C, because the disturbance covered the
baseline arm's iterations and not the measured arm's. Its two standard deviations
were 1.95% and 1.16%, so nothing in the report marks it, and read literally it says
this package parses twice as fast as the BCL. Interleaving cancels a disturbance
that spans a pair; it cannot cancel one that lands on a single arm.

What caught it was comparing the run's own `System.Decimal` arm against what that
same arm cost in an earlier run: 115.70 ns against 54.0. That is a per-row check
and not the whole-run canary the conformance gate carried, which change 8 retired
for discarding runs wholesale. Whether it becomes a rule is not decided here.

**Two rows were disqualified in run B** for a standard deviation above 5% of
their mean, which is what a disturbed case looks like when the disturbance shows:
`HashWidened` at one word aligned measured 39.27 ns with a deviation of 5.8%,
against 18.87 and 18.95 ns for the same row in the two runs either side, and
`HashWidenedOne` at two words misaligned came in at 32.0%. Read literally, the
first would have put the widened-against-narrow ceiling at 4.02 against 2.5 - a
regression in code nobody had touched. The verdicts above are from the shapes
that were readable.

**Addition's 3.00 is the worst of its four shapes and the least trustworthy of
them.** Its measured arm came in at 12.60 ns where the other three runs of the
same code read 10.06 and 10.07, and its deviation was 0.48%, so nothing in the
report marks it. It is recorded as measured. It is inside 3.5x either way, and
it is the case that moved that budget off 3x.

**The two `TryFormat` rows are the worst of nine format strings** at two
operand shapes on each overload, both from `#,##0.00` on a two-word mantissa.
The other eight sit between 1.25x and 2.49x, and the criterion is read per
format string rather than as an average over them. The run before this one put
the same two at 2.74x and 2.67x, so the verdict does not turn on which was read.

**Every `Measured` row allocated zero bytes.** The only non-zero allocation in
run A is `System.Decimal`'s own, 56 bytes formatting `#,##0.00` at two words.
Allocation is enforced by the test suite; this project only reports it.

## Not measured here

- **Division by a single word at every mantissa width.** Its class carries no
  budget category, so the budgeted subset leaves it out. Its result, taken on
  2026-09-07 in its own run: the primitive costs 0.54x to 0.32x of the `UInt128`
  form it replaced on net10.0 as the magnitude grows from one word to four, and
  0.59x to 0.04x on net8.0, where the runtime's software division of a `UInt128`
  is several times dearer. That is a two-rows-of-one-run claim, which is how an
  improvement is claimed here.
- **The conversions, the scale changes, and the wide operand widths.** They are
  measured so that a change in them is visible, and no criterion is stated over
  them.
