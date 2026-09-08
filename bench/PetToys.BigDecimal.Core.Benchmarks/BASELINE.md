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
| J   | 2026-09-08 | `*Divide*`, `*ExactDivision*` | `99aa01c`, the code being replaced | ~6 min |
| K   | 2026-09-08 | `*Divide*`, `*ExactDivision*` | `divide-and-copy-cost`, continuing from the trial remainder | ~6 min |
| L   | 2026-09-08 | `*Divide*`, `*ExactDivision*` | `divide-and-copy-cost`, as it ships | ~6 min |
| M   | 2026-09-08 | arithmetic, comparison, parsing | `99aa01c`, the code being replaced | 30 min |
| N   | 2026-09-08 | `--anyCategories budget` | `divide-and-copy-cost`, as it ships | 66 min |

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

Runs J through N are the next change's, and every row below now comes from N. J, K and L
are the same two classes on three versions of the same code, taken so that a decision could
be made on a reading rather than on the design that proposed it: J is what was there, K is
the full-precision division continuing from the trial remainder, L is what shipped once K
was refused. J and M were taken in a detached worktree at `99aa01c` on a short path, since
BenchmarkDotNet's generated project directories exceed the Windows path limit under a
deeper one. N is the record run; it is a full budget run because the change took a fixed
cost out of every operation that copies a magnitude, which is most of them.

## Verdicts

| Criterion                          | Budget | Ratio | Dispersion | Shape                 | Verdict | Run |
| ---------------------------------- | -----: | ----: | ---------: | --------------------- | ------- | --- |
| `Add`                              |   3.5x |  2.38 |       0.04 | two words, misaligned | met     | N   |
| `Subtract`                         |   3.5x |  2.61 |       0.02 | two words, misaligned | met     | N   |
| `Multiply`                         |   3.5x |  3.13 |       0.01 | one word, misaligned  | met     | N   |
| `Divide`                           |    10x |  5.34 |       0.05 | two words, aligned    | met     | N   |
| `Remainder`                        |    10x |  2.80 |       0.03 | one word, aligned     | met     | N   |
| `Parse`, `char`                    |     3x |  1.10 |          - | one word              | met     | N   |
| `Parse`, UTF-8                     |     3x |  1.28 |          - | one word              | met     | N   |
| `TryParse`, `char`                 |     3x |  1.08 |       0.01 | one word              | met     | N   |
| `TryParse`, UTF-8                  |     3x |  1.25 |          - | one word              | met     | N   |
| `TryFormat`, `char`                |     3x |  2.77 |       0.02 | two words, `#,##0.00` | met     | N   |
| `TryFormat`, UTF-8                 |     3x |  2.66 |       0.02 | two words, `#,##0.00` | met     | N   |
| Exact division against inexact     |    1.0 |  0.25 |       0.00 | `100 / 10` vs `/ 3`   | met     | N   |
| Hashing, widened against narrow    |   2.5x |  2.17 |          - | two words, aligned    | met     | N   |
| Hashing, nineteen zeros against one|   1.5x |  1.38 |          - | one word, misaligned  | met     | N   |
| Zero allocations                   | always |     - |          - | every row             | met     | N   |

Three of the four parsing classes carry no `RatioSD` in run N: BenchmarkDotNet dropped the
column. Their rows' own standard deviations are between 0.5% and 1.4% of their means, which
bounds `r + 2s` at 1.32 against a 3x budget, so the verdict does not turn on the missing
column.

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

**Continuing a division from the trial remainder is dearer, and two lines of arithmetic
said so before the code did.** `Divide` searches for an exact quotient before lifting the
dividend to full precision, and the full-precision pass restarts from the dividend rather
than continuing from the trial's remainder. Continuing is an identity - with
`N * 10^f = q * D + r`, the quotient at `f + k` is `q * 10^k + (r * 10^k) / D` - so it was
written, verified against the form it replaces over the randomised corpus, and measured as
run K against run J:

| Row                         |      J |      K |     L |
| --------------------------- | -----: | -----: | ----: |
| `Divide`, one word aligned  |  92.23 | 103.50 | 84.29 |
| `Divide`, two words aligned | 101.84 | 106.45 | 99.64 |
| Exact division, `100 / 3`   |  70.08 |  78.46 | 66.38 |
| Exact division, `100 / 10`  |  19.05 |  17.15 | 16.12 |

Dearer on every division row, by 1.5% to 12%, against `System.Decimal` arms that agreed to
within one per cent across the three runs. Run N measured the same two classes again inside
the full budget scope and agrees with L to within 5%, and to within 2.5% on six of its seven
rows, which is what one session's spread on identical code looks like at this width. What continuing removes is one lift of the
dividend; what it adds is a lift of the quotient, a lift of the remainder and an addition.
The trial division it reuses divides the *unlifted* dividend by a single-word divisor - one
hardware divide - so the work it saves was never the expensive part. That count is two
sentences long and cost nothing; it was not made until after the code was written. A cost
argument that can be settled on paper should be settled on paper, and the run kept for the
part that cannot.

**Twelve redundant zeroings came out, and what they were worth splits by how much other work
the operation does.** Nothing in the package reads a work buffer above the length it is
given, and nothing sets `SkipLocalsInit`, so the runtime had already zeroed each
`stackalloc` before any of these ran. Run M against run N, on the measured arm:

| Class                      | Moved by |
| -------------------------- | -------- |
| `Subtract`                 | -3.0% to -15.8% |
| `Remainder`                | -4.4% to -24.7% |
| `Add`                      | -2.2% to -10.0% |
| `Divide` (J against N)     | -3.6% to -7.1% |
| Exact division, `100 / 10` | -14.2% |
| `CompareTo`, two words misaligned | -18.4% |
| `Multiply`                 | +1.9% to +3.1% |
| Parsing, one word          | -4.6% to -10.8% |
| Parsing, two words         | +0.9% to +5.9% |

`Multiply` is the control and it moved the wrong way: its buffers are four words wide, so no
clear was ever removed from its path, and the two to three per cent it lost is what code
moving around in the assembly costs. Read the parsing rows the same way - the one-word rows
gained what a fixed twenty-four word fill is worth against a sixty-nanosecond parse, the
two-word rows did not, and no direction is claimed from them. The largest single win is the
comparison of two misaligned operands, which is the one path that carried three clears at
once: two full work buffers cleared by the caller and then twenty words of each cleared
again by `CopyMagnitude`.

**Run M's one-word misaligned comparison is disqualified by its own neighbour.** It measured
15.11 ns against 10.68 ns for the same pairing at two words, and a one-word magnitude is
strictly less work on that path. Run N put the same two rows at 8.68 and 8.72. So the -42%
that row appears to show is not claimed; the comparison claim above rests on the two-word
row, which is readable in both runs.

**The same code, the same machine, eight hours apart, gave `Add` a ratio of 3.26 and then
2.61.** Run M measured `99aa01c` - exactly what runs F and I measured that morning - and
three criteria came back well outside anything the dispersion within either run suggests:
`Add` at two words aligned 3.26 against 2.61, `Subtract` 2.85 against 2.50, `Remainder` at
one word misaligned 3.59 against 2.73. Two others were unmoved: `Multiply` at 3.05 against
3.03, `Parse` at 1.17 against 1.18. Which arm moved cannot be recovered, because this file
deliberately keeps no durations, and that is the point rather than a gap: a ratio is a
reading of one run and a verdict within a tenth of its budget is not reproducible across
sessions. The additive budget was moved from 3x to 3.5x in change 8 for a spread measured
inside single runs; this is the same spread seen between them, and it is wider.

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
