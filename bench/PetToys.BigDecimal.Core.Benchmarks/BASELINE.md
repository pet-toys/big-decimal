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

Runs Q and R were taken on the same machine after a Windows update, at build
`10.0.26200.9445` instead of the `10.0.26200.9168` above. The block is left as
recorded rather than rewritten, because it is the record for runs A to P and
those runs did not happen on the newer build. Nothing else in it moved.

Run AB was taken at the same `10.0.26200.9445`, on SDK 10.0.401 with the host and
job at .NET 10.0.12 rather than the 10.0.400 and 10.0.11 above. Its own ratios are
between rows of that run, so the runtime it was taken on decides nothing in them;
what it does decide is that AB's nanoseconds and run N's are not each other's.

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
| O   | 2026-09-08 | `*Power*`, `*Multiply*`, `*Divide*` | `integer-power`, as it ships | 12 min |
| P   | 2026-09-08 | `*PowerLoop*`            | `integer-power`, as it ships | 3 min  |
| Q   | 2026-09-08 | `*Wire*`                 | `db-interop`, as it ships  | 17 min |
| R   | 2026-09-09 | `*WireWrite*`            | `db-interop`, as it ships  | 4 min  |
| S   | 2026-09-09 | `*Wire*`                 | `db-interop`, as it ships  | 9 min  |
| T   | 2026-09-09 | `*PowerUnderOne*`        | `pow-rounds-once`, as it ships | 1 min  |
| U   | 2026-09-09 | `*PowerUnderOne*`        | `pow-rounds-once`, as it ships | 1 min  |
| V   | 2026-09-09 | `*PowerUnderOne*`        | `pow-rounds-once`, as it ships | 1 min  |
| W   | 2026-09-09 | `*PowerUnderOne*`        | `ab80267`, the code being replaced | 1 min  |
| X   | 2026-09-09 | `*PowerUnderOne*`        | `ab80267`, the code being replaced | 1 min  |
| Y   | 2026-09-09 | `*PowerUnderOne*`        | `ab80267`, the code being replaced | 1 min  |
| Z   | 2026-09-09 | `*MultiplyBenchmarks*`   | `pow-rounds-once`, as it ships | 3 min  |
| AA  | 2026-09-09 | `*DivideBenchmarks*`     | `pow-rounds-once`, as it ships | 3 min  |
| AB  | 2026-09-13 | `*.ParseExponentBenchmarks.*`, `*.ParseBenchmarks.*`, `*.FormatBenchmarks.*` | `stop-divpow10round-when-exhausted`, as it ships | 15 min |

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

Runs T through AA are `pow-rounds-once`'s: one comparison measured three times on each
arm, and a control. The change moves the power chain's scale cap off `MaxScale` onto a
wider one of its own, so that the chain stops rounding an intermediate to the result's
scale.

`PowerBenchmarks` cannot answer it and never could. Its three bases are all a little above
one, and a working value of at least one carries at least as many digits as its scale, so
the digit term of the reduction always dominates the scale term and neither cap decides
anything: simulating the chain over those three bases puts the widest working scale they
reach at 153 against the old cap of 255, and both arms execute identically. A class named
for an operation is not a class that covers the operation's regimes.
`PowerUnderOneBenchmarks` is added for the regime the cap does govern, and is deliberately
outside the budget subset because no acceptance criterion is read from it.

The pair, in nanoseconds, three readings each, every row's StdDev under 1% of its mean:

| Exponent | cap 255 (W, X, Y)      | cap 411 (T, U, V)         | means            |
| -------- | ---------------------- | ------------------------- | ---------------- |
| 4        | 30.48, 30.44, 30.70    | 29.39, 30.04, 31.29       | 30.54 -> 30.24   |
| 600      | 824.14, 894.26, 865.38 | 981.97, 1044.69, 1015.83  | 861.3 -> 1014.2  |

At the fourth power the arms are the same code and measure as such: the ranges overlap and
the new one's mean is 1% lower. At the six hundredth they do not overlap at all - the
slowest old reading is 894 and the fastest new one 982 - so the direction is not in
question even though the row is noisier than the 5% a repeated run usually holds to.
**+17.8% on the means, and at least +9.8% taken as the worst new against the best old.**

The cause is not that the chain reduces more; it reduces less. The old cap was also what
kept the accumulator narrow: for a value near 1e-181 it cut back to about 74 digits and
four words on every step, where the new one leaves the full 154 digits and eight, and a
full-width multiplication there is 64 word products against 16. That is the price of the
correctness rather than a regression to fix, because the digits the old cap discarded
early are exactly the ones whose loss misrounded the result. No criterion is stated over
this class, so the number is a cost recorded rather than a verdict passed.

Runs Z and AA are the control on the shared line, and they are `Multiply` and `Divide` for
the same reason run O used them: the change alters the signature of the reduction helper
that `TryPack` calls on the path of every operation that produces a value, so an argument
was added to a call every value pays for. Worst shapes came back at 3.15 (RatioSD 0.05)
and 5.28 (0.04) against run O's 3.21 (0.03) and 5.33 (0.09), both met with `r + 2s` at
3.25 against 3.5x and 5.36 against 10x, and zero allocations throughout. The rows below
keep citing O for those two criteria; Z and AA confirm them rather than replace them.

**One earlier attempt at all of this is not in the table, and is worth a sentence.** The
same six runs were taken while the machine was not quiet, and the first of them read the
six hundredth power at 1,943 ns - a 2.2x regression that is not there - with its own
StdDev at 1.07% of its mean. The tell was beside it rather than in it: the same process
measured the fourth power at 58.63 ns with a StdDev of 22.6%, and a process disturbed
enough to double one case is not to be read for another. All six were discarded and
retaken on a quiet machine, which is what T through AA are. Announce a run and wait for
the machine before starting it; the estimate is not the point, the quiet is.

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

Run O is `integer-power`, and it is four classes rather than a full run because the
change adds an operation and touches one shared line: the reduction loop moved out of
`TryPack` into a helper the power's accumulator also calls. `Multiply` and `Divide` are
in the run as the control on that line, and they came back at 3.21x and 5.33x against
3.13x and 5.34x in N, which is the same code costing the same thing. The rows below
that name O are the ones O measured; the rest still come from N.

Run P is the same code as O and exists because O's loop comparison had two exponents
three orders of magnitude apart, which locates a crossover by interpolation rather than
by reading it. P adds a twentieth and a fortieth power to that one class.

Runs Q, R and S are `db-interop`'s, and they are the same code three times. Q is the
change's own measurement - three new classes, twenty-six rows, no shared helper touched,
so a targeted run rather than a full budget one - and its write class came back
disturbed. R re-ran that one class alone and was clean. S then repeated Q's exact
command, to settle whether the disturbance was external or was a property of running
three classes in one process with the write class third.

It was external. In S the write class ran third, as in Q, and was clean: 3 minutes 46
seconds against Q's 11 minutes 50 for the same twelve rows, and its PostgreSQL means
agree with the standalone run R to within 0.4%. **Every wire row below cites S**, which
is one clean run covering all five criteria with the rows genuinely interleaved; R is
kept in the table because it is what the verdict rested on until S confirmed it, and Q
because a discarded run that goes unnamed is a run somebody repeats.

## Verdicts

| Criterion                          | Budget | Ratio | Dispersion | Shape                 | Verdict | Run |
| ---------------------------------- | -----: | ----: | ---------: | --------------------- | ------- | --- |
| `Add`                              |   3.5x |  2.38 |       0.04 | two words, misaligned | met     | N   |
| `Subtract`                         |   3.5x |  2.61 |       0.02 | two words, misaligned | met     | N   |
| `Multiply`                         |   3.5x |  3.21 |       0.03 | one word, aligned     | met     | O   |
| `Divide`                           |    10x |  5.33 |       0.09 | two words, aligned    | met     | O   |
| `Remainder`                        |    10x |  2.80 |       0.03 | one word, aligned     | met     | N   |
| `Parse`, `char`                    |     3x |  1.12 |          - | one word              | met     | AB  |
| `Parse`, UTF-8                     |     3x |  1.28 |          - | one word              | met     | N   |
| `TryParse`, `char`                 |     3x |  1.08 |       0.01 | one word              | met     | N   |
| `TryParse`, UTF-8                  |     3x |  1.25 |          - | one word              | met     | N   |
| `TryFormat`, `char`                |     3x |  2.78 |       0.03 | two words, `#,##0.00` | met     | AB  |
| `TryFormat`, UTF-8                 |     3x |  2.66 |       0.02 | two words, `#,##0.00` | met     | N   |
| Exact division against inexact     |    1.0 |  0.25 |       0.00 | `100 / 10` vs `/ 3`   | met     | N   |
| Hashing, widened against narrow    |   2.5x |  2.17 |          - | two words, aligned    | met     | N   |
| Hashing, nineteen zeros against one|   1.5x |  1.38 |          - | one word, misaligned  | met     | N   |
| Power, thousandth against fourth   |    40x | 12.68 |          - | four words            | met     | O   |
| PostgreSQL write against `TryFormat`|  1.5x |  0.87 |          - | four words            | met     | S   |
| ClickHouse write against `TryFormat`|  1.5x |  0.20 |          - | one word              | met     | S   |
| PostgreSQL read against `Parse`    |   1.5x |  0.67 |       0.01 | one word              | met     | S   |
| ClickHouse read against `Parse`    |   1.5x |  0.18 |       0.00 | one word              | met     | S   |
| PostgreSQL write, scale 255 over 0 |   1.5x |  1.07 |          - | one word              | met     | S   |
| Parse at the exponent ceiling      |     2x |  1.20 |          - | `1e-99999` over `1e-200` | met     | AB  |
| Zero allocations                   | always |     - |          - | every row             | met     | N, O, S, AB |

Three of the four parsing classes carry no `RatioSD` in run N: BenchmarkDotNet dropped the
column. Their rows' own standard deviations are between 0.5% and 1.4% of their means, which
bounds `r + 2s` at 1.32 against a 3x budget, so the verdict does not turn on the missing
column.

The two hashing rows carry no dispersion because their class has no `[Baseline]` method:
the ratio is computed here from two of its rows, so BenchmarkDotNet reports no `RatioSD`
for it. Both rows' own standard deviations are under 1% of their means.

The power row is the same shape and for the same reason: its class declares no baseline,
because `System.Decimal` has no power for one to be declared against. The two rows it is
computed from carry standard deviations of 0.7% and 1.0% of their means.

The two write rows carry no `RatioSD` because BenchmarkDotNet dropped the column from the
write class's report in both R and S. Every row in S has a standard deviation between
0.4% and 1.4% of its own mean, which bounds `r + 2s` for the worst of them at 0.89
against a 1.5x budget, so no verdict turns on the missing column. The scale row carries
none for the other reason: its class declares its own baseline but reports a bare
`Ratio`, and its two rows have standard deviations of 0.82% and 0.22%.

The four wire rows are each the worst shape of the four measured, not the mean of them.
Both codecs get *cheaper* against the text path as the mantissa widens - the text path
pays per character and a codec pays per digit group - so the tightest ratio is at the
narrow end for the readers and at the wide end for the PostgreSQL writer, where the
group divisions accumulate.

Run AB grades one added criterion and carries a control. The parse rows of
`ParseExponentBenchmarks`, against its own non-dropping baseline of 48.03 ns: 1.04 at
forty-five dropped positions, 1.09 at 2745 and 1.20 at 99744, the last being what the
parser's exponent ceiling allows. BenchmarkDotNet dropped the `RatioSD` column; every
row's own standard deviation is between 0.6% and 0.9% of its mean, which bounds
`r + 2s` for the worst of them at 1.23 against a 2x budget.

The requirement carries a second bound, over the dropping rows alone: the most distant of
them against the nearest, which is 2745 positions at 1.05 and 99744 at **1.15** against
the 49.84 ns of the 45-position row. Both bounds are 2x and both are met; the table
carries the larger of the two ratios, which is the one nearer the budget.

AB dropped the `RatioSD` column for its two parse classes the way run N did. The rows
behind `Parse`, `char` have standard deviations of 0.58% and 1.15% of their means, which
bounds its `r + 2s` at 1.15 against a 3x budget. `TryFormat`, `char` reported its
`RatioSD` and it is in the table.

The criterion is a relationship between rows of one run rather than a measurement of the
old implementation beside the new one, and deliberately: what it replaces is a chunk loop
inside an internal helper that only the public parse reaches, so carrying both would mean
two parse chains in one process. That is the case the requirement on measuring an
improvement names, and the answer it prescribes. A stopwatch probe outside the suite read
the same literal at 48 us before and 0.5 us after; that is an indication of the size, not
evidence under that requirement, and no verdict here rests on it.

`FormatBenchmarks` is the control: formatting drops no digits and reaches the helper the
change edits at no point. Its worst row is 2.78 against the 2.77 run N recorded, which is
0.4% and inside the band where no direction may be claimed. `Parse`, `char` moved from
1.10 to 1.12 on the same footing. The comparison the change adds to the chunk loop is
therefore not measurable on the callers whose positions are bounded by the scale ceiling.

`Parse`, UTF-8 is still run N's. AB filtered to the `char` class, and the guard sits in a
helper below both paths, so the char row is what says whether it costs anything.

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

**Run Q reproduced run D's lesson exactly, and runs R and S then closed it.** Q's write
class ran last of its three, took 11 minutes 50 seconds against the reading class's 4
minutes for the same twelve rows, and reported 12 to 22 outliers per PostgreSQL row
against the 1 or 2 its neighbours reported. Its `TryFormat` baseline came back at 180.90,
254.43 and 372.37 ns for the three wider shapes; R and S put the same three at 88.75,
130.86, 191.26 and 94.09, 128.54, 190.92. The baseline arm was inflated by about a factor
of two, exactly as five of run D's eight were.

Three of the four PostgreSQL ratios survived it anyway - 0.68, 0.77 and 0.88 in Q against
0.73, 0.80 and 0.87 in S - because both arms met the same disturbance and it divided out.
The fourth did not: **one word came back at 1.20 in Q against 0.62 in both R and S**, and
1.20 with a `RatioSD` of 0.19 grades as `r + 2s` = 1.58, a miss against a 1.5x budget on
code that is comfortably inside it. That is a disturbance landing on a single arm, which
interleaving cannot cancel, and it is the second time this file has had to record one.

What marked it was not a cross-run comparison. Q's own report named 13 outliers on that
row spanning 51.87 to 97.05 ns against a reported mean of 97.25, which says the row is
bimodal and that the mean sits in the slow mode. `TryWrite` has no data-dependent branch
and the operand is fixed, so a fixed input producing two modes is the machine talking. R
then measured the fast mode at 50.06 ns, within a nanosecond of Q's own lowest outlier,
and S measured it at 50.27. **A row whose outlier span reaches well below its own mean is
reporting a disturbance, whatever its standard deviation says** - a check that needs
nothing but the run itself, like the neighbour check in the run E note and unlike the
recorded-duration check the run D note describes.

**S was taken to rule out the other explanation, and it is the reason the write rows can
be cited at all.** R answered whether the code is fast; it could not answer why Q was
slow, because it changed two things at once - it re-ran the class *and* ran it alone. That
left a live alternative: not an external disturbance but a property of the run itself, the
write class suffering because it went third, five minutes into a process. Those two have
different consequences. An external disturbance means re-run and record. A positional
effect would mean the three classes cannot be graded from one invocation at all, and the
structure of every targeted run in this repository would need revisiting.

S repeated Q's exact command. The write class ran third again and came back clean in 3
minutes 46 seconds, its PostgreSQL means inside 0.4% of standalone R's. So the effect is
external and transient, the run structure is sound, and one clean invocation covers all
five criteria with the rows interleaved as the budget requires.

The cheap part of this is worth keeping: **a clean run's duration was predictable before
it started.** Q's own per-class timings gave 4:00 + 0:36 + 3:53 = about nine minutes for a
clean repeat against Q's seventeen, and S came in at 8:34. The duration is the first
reading of a run, available before any number in the report is looked at, and it cost
nothing to state in advance.

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

**The power's cost follows the exponent's bit length, and the shape it is read at
matters.** Run O, in nanoseconds:

| Base       | `^4`   | `^1000`  | ratio | `^-4`  | `^-1000` |
| ---------- | -----: | -------: | ----: | -----: | -------: |
| one word   |  29.71 | 2 789.25 | 93.9  |  70.74 | 2 899.34 |
| two words  |  84.59 | 4 616.68 | 54.6  | 194.92 | 4 984.88 |
| four words | 395.35 | 5 012.54 | 12.7  | 520.12 | 5 363.88 |

The criterion is read from the last row and the other two are recorded without a
ceiling, because only the last one measures what the criterion is about. Fifteen
multiplications against three is a factor of five; what the first two rows add on top of
it is the accumulator, which a narrow base never fills at the fourth power and fills
completely at the thousandth, paying a reduction on almost every step once it does. The
four-word base is at full width in both, so its 12.7x is the multiplication count and
the reductions that go with it. A fold over multiplication would put the larger row
several hundred times above the smaller one at every shape, which is the separation the
ceiling exists to detect.

**The reciprocal costs a division on top of the chain, and the division grows far more
slowly than the chain does.** It adds 41 ns at one word and 125 ns at four words over
the fourth power, and 110 ns and 351 ns over the thousandth. Between those two exponents
the chain itself grows by 94x and 12.7x while the division it pays for grows by 2.7x and
2.8x: what the division answers to is the width of the power and the length of the lift,
not the number of multiplications that produced it.

**The multiplication loop a caller writes instead wins at a small exponent and loses
from a small one.** Run P, in nanoseconds, on a one-word base:

| Exponent | `Pow`    | `decimal` loop |
| -------: | -------: | -------------: |
|        4 |    30.13 |           6.45 |
|       20 |    50.17 |         153.81 |
|       40 |   110.22 |         542.43 |
|     1000 | 2 915.68 |      17 557.50 |

The crossover is between the fourth power and the twentieth, and by the twentieth the
loop is already three times dearer. Two things are moving at once: the loop performs
the exponent's own number of multiplications where the power performs its bit length's,
and the loop's own multiplications get dearer as it goes, from 1.6 ns each at the
fourth power to 17.6 ns at the thousandth, because a `decimal` whose scale has run past
28 rounds on every step. No ratio is quoted from this pair as a criterion: past the
point where the exact result leaves 96 bits the two are not computing the same thing,
and the loop's answer has been rounded a thousand times.

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
