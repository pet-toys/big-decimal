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
| E   | 2026-09-08 | `--anyCategories budget` | `nan-infinity`, as first written | 71 min |
| F   | 2026-09-08 | arithmetic and comparison | `nan-infinity`, as first written | 29 min |
| G   | 2026-09-08 | `*Add*`, `*Subtract*`, `*Multiply*` | `nan-infinity` + `NoInlining` | 10 min |
| H   | 2026-09-08 | `*Add*`, `*Subtract*`    | `cb8bd99`, the code being replaced | 7 min |
| I   | 2026-09-08 | `*Add*`, `*Subtract*`    | `nan-infinity`, as it ships | 7 min |

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

Runs E through I are one change's measurement and are worth reading as a sequence. E was
the full budget run and came back disturbed on the arithmetic and comparison classes and
clean on parsing and formatting, which is why the rows below cite E for the second group
and F for the first. F re-ran the disturbed classes and found that `Add` and `Subtract`
missed their budget. G tested one explanation and refuted it. H measured the code being
replaced, on the same machine in the same session, and established that the miss was a
real regression rather than an old cost nobody had recorded. I measured the fix.

## Verdicts

| Criterion                          | Budget | Ratio | Dispersion | Shape                 | Verdict | Run |
| ---------------------------------- | -----: | ----: | ---------: | --------------------- | ------- | --- |
| `Add`                              |   3.5x |  3.26 |       0.02 | two words, aligned    | met     | I   |
| `Subtract`                         |   3.5x |  2.85 |       0.02 | two words, aligned    | met     | I   |
| `Multiply`                         |   3.5x |  3.05 |       0.02 | one word, either      | met     | F   |
| `Divide`                           |    10x |  5.76 |       0.05 | one word, aligned     | met     | F   |
| `Remainder`                        |    10x |  3.59 |       0.02 | one word, misaligned  | met     | F   |
| `Parse`, `char`                    |     3x |  1.17 |       0.00 | one word              | met     | E   |
| `Parse`, UTF-8                     |     3x |  1.31 |       0.00 | one word              | met     | E   |
| `TryParse`, `char`                 |     3x |  1.24 |       0.00 | one word              | met     | E   |
| `TryParse`, UTF-8                  |     3x |  1.28 |       0.01 | one word              | met     | E   |
| `TryFormat`, `char`                |     3x |  2.79 |       0.02 | two words, `#,##0.00` | met     | E   |
| `TryFormat`, UTF-8                 |     3x |  2.83 |       0.03 | two words, `#,##0.00` | met     | E   |
| Exact division against inexact     |    1.0 |  0.34 |       0.00 | `100 / 10` vs `/ 3`   | met     | F   |
| Hashing, widened against narrow    |   2.5x |  2.15 |          - | two words, aligned    | met     | F   |
| Hashing, nineteen zeros against one|   1.5x |  1.40 |          - | one word, misaligned  | met     | F   |
| Zero allocations                   | always |     - |          - | every row             | met     | E   |

The two hashing rows carry no dispersion because their class has no `[Baseline]` method:
the ratio is computed here from two of its rows, so BenchmarkDotNet reports no `RatioSD`
for it. Both rows' own standard deviations are under 1% of their means.

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

**A disturbed run can be caught inside itself.** Run E was read as unusable for the
arithmetic classes without comparing anything to an earlier run, because two of its rows
were impossible against their own neighbours: `Add` measured *aligned* one-word operands
at 17.10 ns and *misaligned* ones at 14.66 ns, and `Remainder` measured one word at
40.39 ns against two words at 21.64 ns. Alignment and a narrower magnitude are strictly
less work on the same code path, so a run that reports them as dearer is reporting its own
disturbance. Every one of those rows had a standard deviation under 2% of its mean, so the
row rule saw nothing, exactly as in run D.

This is worth having beside the cross-run check the D notes describe. That one needs a
recorded duration to compare against and this file deliberately carries none; this one
needs nothing but the run itself, and it is the reason run F was taken. F put `Remainder`
at one word aligned back at 3.01 against the 3.02 in the row above, on code that differs
from A's by two flag tests.

**A cold branch in a hot forwarder cost 4.3 nanoseconds, and the fix was where the branch
sat rather than what it did.** `nan-infinity` first guarded `Add` and `Subtract` by turning
each from a one-line forwarder into a conditional expression with two calls. Run F put
`Add` at 4.21 and `Subtract` at 3.93 against a 3.5x budget, both missed, and run H measured
the code being replaced at 3.14 and 2.88 on the same shapes: a real regression, not an
unrecorded old cost. Marking the cold helper `NoInlining` (run G) recovered three of
`Add`'s four shapes and left the worst one at 4.21 exactly, which is what said the cost was
not the helper's size but the forwarder's shape. Moving the guard into `AddCore`, which has
a `stackalloc` body and was never inlined anyway, put `Add` at 3.26 and `Subtract` at 2.85.
The mechanism: `operator +` inlined the forwarder and called `AddCore` directly; a branch
and a second call stopped that, and the operator then called the forwarder, which called
`AddCore`, copying two forty-byte structs across a frame that had not existed.

The reading for next time is that a guard added to the head of an operation is not free by
inspection, and where it is written decides what it costs. `Multiply`, `Divide` and
`Remainder` took the same guard as a statement at the top of bodies that were already too
large to inline and moved by 0.02, 0.24 and 0.57 - inside their budgets and inside the
run-to-run spread.

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
