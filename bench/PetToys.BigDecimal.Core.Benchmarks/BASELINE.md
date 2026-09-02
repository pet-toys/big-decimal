# Baseline

Taken on 2026-09-02, replacing the run of 2026-08-26 in full. This file is a
copy of the run's own GitHub-markdown export, one section per benchmark class,
with this note prefixed. The environment block below is the export's own and
was identical in all sixteen sections, so it is stated once rather than sixteen
times. It is the same machine the previous baseline was taken on, so the
durations here are comparable against that one and not only the ratios.

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700H 2.30GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.400
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
```

`DefaultJob` is the point: a run taken with `--job short` or `--job dry` is not
comparable against this file, and neither is one taken on another machine when
the comparison is of `Mean`. **Ratios travel, durations do not.** `Ratio` is
`Measured / Baseline` within one combination of parameters, and it is the only
column a budget is read against. `Allocated` is comparable anywhere, being a
count rather than a duration.

Classes with no `Baseline` method carry no budget and print no ratio: those are
the operations no criterion is stated over, and the three- and four-word widths
`decimal` cannot represent at all.

## What the budgets say, and what this run measured

Four criteria are stated over these operations. Two are now met that were not,
one was already met, and the numbers below are as measured: no operand was
chosen after the fact to make one pass, and no favourable repeat run was
substituted for the one this file records.

| Criterion | Budget | Worst measured | Verdict |
| --------- | ------ | -------------- | ------- |
| Add, subtract, multiply within 3x, one or two words | 3x | Add 3.16x, Subtract 2.87x, Multiply 2.92x | subtract and multiply met, add on the boundary |
| Divide within 10x | 10x | 6.98x | met |
| An exact division costs no more than an inexact one | ratio 1 | 21.6 ns against 84.2 ns, **0.26x** | met |
| Parse and TryFormat within 3x | 3x | Parse 1.20x; TryFormat 3.87x char, 4.62x UTF-8 | **missed** by TryFormat |
| Zero allocations on every operation | 0 B | 64 B on the `N` specifier, both overloads | **missed** |

Read against the ratios rather than the headline:

- **Exactness is settled, and by a distance.** The same dividend divided
  exactly costs 21.6 ns against 84.2 ns inexact, where the previous baseline had
  it at 485.3 ns against 56.6 ns and the criterion inverted by 8.6x. The
  quotient is no longer manufactured at full precision and then stripped of the
  trailing zeros that lifting produced: an exact quotient is looked for first,
  from the divisor's factors and from a trial division at the scale difference.
- **Subtract is inside at 2.87x** at its worst, against 3.73x before. Multiply
  is inside at 2.92x and was not touched by the change that produced this run.
- **Add sits on the boundary.** It measures 2.73x to 3.16x here, against 3.87x
  before, and the worst shapes are the two aligned ones. Repeat runs of the
  same binary put those same shapes anywhere between 2.36x and 3.16x, so the
  criterion is neither comfortably met nor clearly missed. See the caveat below.
- **Division is inside at 6.98x** against a 10x budget, and it is dearer than
  the 4.61x of the previous baseline. That is the price of the two checks for
  an exact quotient: an inexact division runs both and benefits from neither.
  The budget has the room, and the alternative, continuing the long division
  from the trial's remainder rather than restarting it, is written down as an
  open question rather than attempted here.
- **Remainder runs 1.06x to 4.25x**, better at every shape than the 1.11x to
  5.87x before it.
- **Parsing is the comfortable result**, 0.89x to 1.20x across all four
  overloads, with three of the eight rows faster than `decimal`. The two-word
  `Parse` baseline was bimodal in the previous run and is not in this one.
- **Formatting misses on the `N` specifier alone.** `F9` and `G` run 1.52x to
  2.53x, inside the budget; `N2` runs 3.13x to 4.62x and is also the only
  format that allocates.
- **The allocation is 64 bytes on `N`, in both overloads**, unchanged and
  untouched. It is two reads of `NumberFormatInfo.NumberGroupSizes` on a single
  line, a property whose getter clones its array on every read. It belongs to
  the formatting work, along with the miss above.

Outside the budgets, hashing is the row that moved. `GetHashCode` costs 9.4 ns
to 11.2 ns on a value with no trailing zeros, against 12.3 ns to 19.0 ns
before, because such a value now performs no division of its magnitude at all.
On a value carrying eleven trailing zeros, which is what `WithScale` produces
when a caller widens to a column's scale, it costs 15.8 ns to 23.4 ns against
96.4 ns to 109.4 ns before. Four widening rows are new to this run and they are
the shape of the curve rather than one point on it: one zero, eleven, nineteen
and twenty-five. One to nineteen costs 1.34x to 1.46x, where removing a zero at
a time would cost about nineteen times; twenty-five crosses into a second
division and costs one step more, roughly double.

`ToBinaryFloat` remains the dearest operation in the suite at 141 ns to 171 ns,
down from 169 ns to 219 ns, and no criterion is stated over it.

## A caveat on the addition rows

`AddBenchmarks` at the aligned shapes is the measurement to distrust here. The
same binary, run three times on this machine under `DefaultJob`, reports one
word aligned at 2.25x, 3.02x and 3.14x, and two words aligned at 2.36x, 2.51x
and 3.16x. The error bars within each run are a fraction of a percent, so the
spread is between runs rather than inside them: it is code layout and JIT
tiering, not the operands and not the machine being busy.

The number recorded above is this run's, which is the worst of the three. Read
it as an operation that has moved from 3.87x to the region of 3x and now sits
on the line, rather than as one that is inside or outside the budget. What
remains of its cost is fixed rather than proportional: `CopyMagnitude` clears
every word of the working buffer above the four it writes, and the aligned
shape is where `decimal` is fastest and so where any fixed cost reads worst.
Narrowing that buffer to what an aligned addition needs was tried and made
things worse, because a `stackalloc` whose length is not a constant becomes a
dynamic allocation and cost the misaligned shapes more than it saved the
aligned ones.

## Add - budget 3x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **4.209 ns** | **0.0120 ns** | **0.0112 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 11.980 ns | 0.0602 ns | 0.0563 ns |  2.85 |    0.01 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **5.067 ns** | **0.0091 ns** | **0.0076 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 21.044 ns | 0.0781 ns | 0.0652 ns |  4.15 |    0.01 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **3.983 ns** | **0.0108 ns** | **0.0096 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 11.975 ns | 0.0457 ns | 0.0405 ns |  3.01 |    0.01 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **5.290 ns** | **0.0129 ns** | **0.0121 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 20.245 ns | 0.0983 ns | 0.0821 ns |  3.83 |    0.02 |         - |          NA |

## Subtract - budget 3x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **4.220 ns** | **0.0245 ns** | **0.0218 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 13.056 ns | 0.0456 ns | 0.0427 ns |  3.09 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **5.060 ns** | **0.0068 ns** | **0.0063 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 23.912 ns | 0.1115 ns | 0.1043 ns |  4.73 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **4.215 ns** | **0.0238 ns** | **0.0186 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 12.835 ns | 0.0407 ns | 0.0318 ns |  3.05 |    0.01 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **5.289 ns** | **0.0214 ns** | **0.0200 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 23.688 ns | 0.1066 ns | 0.0997 ns |  4.48 |    0.02 |         - |          NA |

## Multiply - budget 3x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **3.568 ns** | **0.0147 ns** | **0.0137 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 10.422 ns | 0.0367 ns | 0.0326 ns |  2.92 |         - |          NA |
|          |          |            |           |           |           |       |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **3.569 ns** | **0.0075 ns** | **0.0067 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 10.422 ns | 0.0494 ns | 0.0438 ns |  2.92 |         - |          NA |
|          |          |            |           |           |           |       |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **4.421 ns** | **0.0094 ns** | **0.0083 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 12.108 ns | 0.0426 ns | 0.0399 ns |  2.74 |         - |          NA |
|          |          |            |           |           |           |       |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **4.423 ns** | **0.0118 ns** | **0.0098 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 12.057 ns | 0.0537 ns | 0.0502 ns |  2.73 |         - |          NA |

## Divide - budget 10x

| Method   | Shape    | Pairing    | Mean      | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **16.23 ns** | **0.237 ns** | **0.222 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 109.35 ns | 0.671 ns | 0.595 ns |  6.74 |    0.10 |         - |          NA |
|          |          |            |           |          |          |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **20.23 ns** | **0.205 ns** | **0.191 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned |  98.56 ns | 0.756 ns | 0.707 ns |  4.87 |    0.06 |         - |          NA |
|          |          |            |           |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **17.65 ns** | **0.040 ns** | **0.034 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 123.20 ns | 0.653 ns | 0.579 ns |  6.98 |    0.03 |         - |          NA |
|          |          |            |           |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **19.59 ns** | **0.172 ns** | **0.153 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 105.42 ns | 0.719 ns | 0.672 ns |  5.38 |    0.05 |         - |          NA |

## Remainder - budget 10x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **4.857 ns** | **0.0292 ns** | **0.0244 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 18.743 ns | 0.0573 ns | 0.0508 ns |  3.86 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **6.322 ns** | **0.0650 ns** | **0.0608 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 26.849 ns | 0.1624 ns | 0.1519 ns |  4.25 |    0.05 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    | **23.490 ns** | **0.0777 ns** | **0.0726 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 28.464 ns | 0.1715 ns | 0.1521 ns |  1.21 |    0.01 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** | **24.949 ns** | **0.0913 ns** | **0.0854 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 26.329 ns | 0.1005 ns | 0.0940 ns |  1.06 |    0.01 |         - |          NA |

## Exact division - budget not dearer than inexact

| Method   | Exact | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |------ |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **False** | **19.249 ns** | **0.1396 ns** | **0.1238 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | False | 84.205 ns | 0.4704 ns | 0.4400 ns |  4.37 |    0.04 |         - |          NA |
|          |       |           |           |           |       |         |           |             |
| **Baseline** | **True**  |  **9.201 ns** | **0.0396 ns** | **0.0309 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | True  | 21.565 ns | 0.1260 ns | 0.1179 ns |  2.34 |    0.01 |         - |          NA |

## Parse - budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **53.55 ns** | **0.210 ns** | **0.197 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | 57.11 ns | 0.478 ns | 0.424 ns |  1.07 |         - |          NA |
|          |          |          |          |          |       |           |             |
| **Baseline** | **TwoWords** | **86.00 ns** | **0.402 ns** | **0.356 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | 77.06 ns | 0.359 ns | 0.336 ns |  0.90 |         - |          NA |

## TryParse - budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **53.91 ns** | **0.270 ns** | **0.239 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | 56.89 ns | 0.330 ns | 0.293 ns |  1.06 |         - |          NA |
|          |          |          |          |          |       |           |             |
| **Baseline** | **TwoWords** | **86.88 ns** | **0.486 ns** | **0.455 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | 77.11 ns | 0.231 ns | 0.205 ns |  0.89 |         - |          NA |

## Parse (UTF-8) - budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **54.37 ns** | **0.263 ns** | **0.246 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | 65.09 ns | 0.487 ns | 0.456 ns |  1.20 |         - |          NA |
|          |          |          |          |          |       |           |             |
| **Baseline** | **TwoWords** | **88.23 ns** | **0.406 ns** | **0.380 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | 87.19 ns | 0.619 ns | 0.549 ns |  0.99 |         - |          NA |

## TryParse (UTF-8) - budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **55.29 ns** | **0.204 ns** | **0.191 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | 63.80 ns | 0.441 ns | 0.412 ns |  1.15 |         - |          NA |
|          |          |          |          |          |       |           |             |
| **Baseline** | **TwoWords** | **87.09 ns** | **0.574 ns** | **0.537 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | 86.19 ns | 0.349 ns | 0.309 ns |  0.99 |         - |          NA |

## TryFormat - budget 3x

| Method   | Shape    | Format | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------- |--------- |------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **Baseline** | **OneWord**  | **F9**     |  **42.46 ns** | **0.279 ns** | **0.261 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | F9     |  94.66 ns | 0.717 ns | 0.636 ns |  2.23 |    0.02 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **OneWord**  | **G**      |  **37.31 ns** | **0.114 ns** | **0.095 ns** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | G      |  87.65 ns | 0.739 ns | 0.691 ns |  2.35 |    0.02 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **OneWord**  | **N2**     |  **45.44 ns** | **0.197 ns** | **0.184 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | N2     | 175.95 ns | 0.519 ns | 0.460 ns |  3.87 |    0.02 | 0.0050 |      64 B |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **F9**     |  **65.57 ns** | **0.576 ns** | **0.511 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | F9     | 104.92 ns | 0.425 ns | 0.332 ns |  1.60 |    0.01 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **G**      |  **58.42 ns** | **0.422 ns** | **0.374 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | G      |  88.95 ns | 0.227 ns | 0.213 ns |  1.52 |    0.01 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **N2**     |  **72.95 ns** | **0.517 ns** | **0.483 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | N2     | 228.18 ns | 1.189 ns | 1.112 ns |  3.13 |    0.02 | 0.0050 |      64 B |          NA |

## TryFormat (UTF-8) - budget 3x

| Method   | Shape    | Format | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------- |--------- |------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **Baseline** | **OneWord**  | **F9**     |  **41.26 ns** | **0.223 ns** | **0.197 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | F9     | 101.46 ns | 1.186 ns | 1.110 ns |  2.46 |    0.03 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **OneWord**  | **G**      |  **36.58 ns** | **0.401 ns** | **0.375 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | G      |  92.56 ns | 0.640 ns | 0.599 ns |  2.53 |    0.03 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **OneWord**  | **N2**     |  **45.36 ns** | **0.122 ns** | **0.108 ns** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | N2     | 209.51 ns | 0.904 ns | 0.802 ns |  4.62 |    0.02 | 0.0050 |      64 B |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **F9**     |  **64.00 ns** | **0.378 ns** | **0.335 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | F9     | 115.97 ns | 0.868 ns | 0.811 ns |  1.81 |    0.02 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **G**      |  **56.90 ns** | **0.355 ns** | **0.314 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | G      |  99.85 ns | 0.713 ns | 0.632 ns |  1.75 |    0.01 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **N2**     |  **70.60 ns** | **0.729 ns** | **0.682 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | N2     | 253.73 ns | 1.898 ns | 1.775 ns |  3.59 |    0.04 | 0.0048 |      64 B |          NA |

## Scale changes - no budget

| Method            | Shape    | Mean      | Error     | StdDev    | Allocated |
|------------------ |--------- |----------:|----------:|----------:|----------:|
| **RoundToEven**       | **OneWord**  | **11.484 ns** | **0.0458 ns** | **0.0406 ns** |         **-** |
| RoundAwayFromZero | OneWord  | 11.550 ns | 0.0488 ns | 0.0457 ns |         - |
| RoundReference    | OneWord  |  3.587 ns | 0.0142 ns | 0.0132 ns |         - |
| Narrow            | OneWord  | 12.716 ns | 0.0664 ns | 0.0621 ns |         - |
| Widen             | OneWord  |  5.196 ns | 0.0187 ns | 0.0175 ns |         - |
| **RoundToEven**       | **TwoWords** | **14.689 ns** | **0.0952 ns** | **0.0891 ns** |         **-** |
| RoundAwayFromZero | TwoWords | 14.513 ns | 0.0889 ns | 0.0832 ns |         - |
| RoundReference    | TwoWords |  4.440 ns | 0.0133 ns | 0.0104 ns |         - |
| Narrow            | TwoWords | 15.915 ns | 0.0856 ns | 0.0714 ns |         - |
| Widen             | TwoWords |  7.759 ns | 0.0871 ns | 0.0727 ns |         - |

## Ordering and hashing - no budget

| Method              | Shape    | Pairing    | Mean       | Error     | StdDev    | Allocated |
|-------------------- |--------- |----------- |-----------:|----------:|----------:|----------:|
| **Compare**             | **OneWord**  | **Aligned**    |  **5.9518 ns** | **0.0634 ns** | **0.0562 ns** |         **-** |
| CompareReference    | OneWord  | Aligned    |  1.5106 ns | 0.0146 ns | 0.0122 ns |         - |
| Hash                | OneWord  | Aligned    |  9.5026 ns | 0.0634 ns | 0.0562 ns |         - |
| HashReference       | OneWord  | Aligned    |  0.7034 ns | 0.0247 ns | 0.0231 ns |         - |
| HashWidenedOne      | OneWord  | Aligned    | 13.0652 ns | 0.0426 ns | 0.0399 ns |         - |
| HashWidened         | OneWord  | Aligned    | 15.8440 ns | 0.1059 ns | 0.0884 ns |         - |
| HashWidenedNineteen | OneWord  | Aligned    | 17.5713 ns | 0.0897 ns | 0.0796 ns |         - |
| HashWidenedBeyond   | OneWord  | Aligned    | 38.0066 ns | 0.2526 ns | 0.2239 ns |         - |
| **Compare**             | **OneWord**  | **Misaligned** | **14.5116 ns** | **0.1664 ns** | **0.1475 ns** |         **-** |
| CompareReference    | OneWord  | Misaligned |  2.0253 ns | 0.0166 ns | 0.0148 ns |         - |
| Hash                | OneWord  | Misaligned |  9.3843 ns | 0.0587 ns | 0.0521 ns |         - |
| HashReference       | OneWord  | Misaligned |  0.7204 ns | 0.0324 ns | 0.0303 ns |         - |
| HashWidenedOne      | OneWord  | Misaligned | 13.3037 ns | 0.0893 ns | 0.0792 ns |         - |
| HashWidened         | OneWord  | Misaligned | 16.6878 ns | 0.1853 ns | 0.1733 ns |         - |
| HashWidenedNineteen | OneWord  | Misaligned | 18.1170 ns | 0.2378 ns | 0.2108 ns |         - |
| HashWidenedBeyond   | OneWord  | Misaligned | 37.7728 ns | 0.3786 ns | 0.3541 ns |         - |
| **Compare**             | **TwoWords** | **Aligned**    |  **5.4630 ns** | **0.0544 ns** | **0.0508 ns** |         **-** |
| CompareReference    | TwoWords | Aligned    |  1.5347 ns | 0.0133 ns | 0.0118 ns |         - |
| Hash                | TwoWords | Aligned    | 11.1833 ns | 0.1228 ns | 0.1149 ns |         - |
| HashReference       | TwoWords | Aligned    |  0.7463 ns | 0.0197 ns | 0.0185 ns |         - |
| HashWidenedOne      | TwoWords | Aligned    | 16.6738 ns | 0.0933 ns | 0.0827 ns |         - |
| HashWidened         | TwoWords | Aligned    | 23.3697 ns | 0.1362 ns | 0.1274 ns |         - |
| HashWidenedNineteen | TwoWords | Aligned    | 24.3248 ns | 0.1377 ns | 0.1221 ns |         - |
| HashWidenedBeyond   | TwoWords | Aligned    | 37.9548 ns | 0.3874 ns | 0.3624 ns |         - |
| **Compare**             | **TwoWords** | **Misaligned** | **14.8641 ns** | **0.3041 ns** | **0.2845 ns** |         **-** |
| CompareReference    | TwoWords | Misaligned |  2.0170 ns | 0.0138 ns | 0.0129 ns |         - |
| Hash                | TwoWords | Misaligned | 11.0786 ns | 0.0684 ns | 0.0572 ns |         - |
| HashReference       | TwoWords | Misaligned |  0.7318 ns | 0.0283 ns | 0.0265 ns |         - |
| HashWidenedOne      | TwoWords | Misaligned | 17.1364 ns | 0.0877 ns | 0.0777 ns |         - |
| HashWidened         | TwoWords | Misaligned | 23.3161 ns | 0.1646 ns | 0.1540 ns |         - |
| HashWidenedNineteen | TwoWords | Misaligned | 24.3693 ns | 0.1880 ns | 0.1759 ns |         - |
| HashWidenedBeyond   | TwoWords | Misaligned | 38.2908 ns | 0.2523 ns | 0.2360 ns |         - |

## Conversions - no budget

| Method        | Shape    | Mean        | Error     | StdDev    | Allocated |
|-------------- |--------- |------------:|----------:|----------:|----------:|
| **ToReference**   | **OneWord**  |   **7.8459 ns** | **0.0822 ns** | **0.0769 ns** |         **-** |
| FromReference | OneWord  |   0.8336 ns | 0.0227 ns | 0.0190 ns |         - |
| ToBinaryFloat | OneWord  | 141.1323 ns | 1.2078 ns | 1.0707 ns |         - |
| FromInteger   | OneWord  |   0.1333 ns | 0.0128 ns | 0.0120 ns |         - |
| ToWords       | OneWord  |   1.5866 ns | 0.0416 ns | 0.0389 ns |         - |
| FromWords     | OneWord  |   2.8825 ns | 0.0560 ns | 0.0468 ns |         - |
| **ToReference**   | **TwoWords** |   **7.7438 ns** | **0.0864 ns** | **0.0766 ns** |         **-** |
| FromReference | TwoWords |   0.8429 ns | 0.0142 ns | 0.0126 ns |         - |
| ToBinaryFloat | TwoWords | 170.6017 ns | 0.6911 ns | 0.6465 ns |         - |
| FromInteger   | TwoWords |   0.1172 ns | 0.0136 ns | 0.0120 ns |         - |
| ToWords       | TwoWords |   1.0689 ns | 0.0264 ns | 0.0221 ns |         - |
| FromWords     | TwoWords |   2.1658 ns | 0.0316 ns | 0.0280 ns |         - |

## Three- and four-word operands - no budget

| Method    | Shape      | Pairing    | Mean      | Error    | StdDev   | Allocated |
|---------- |----------- |----------- |----------:|---------:|---------:|----------:|
| **Add**       | **ThreeWords** | **Aligned**    |  **12.40 ns** | **0.074 ns** | **0.066 ns** |         **-** |
| Subtract  | ThreeWords | Aligned    |  11.80 ns | 0.098 ns | 0.091 ns |         - |
| Multiply  | ThreeWords | Aligned    |  13.13 ns | 0.040 ns | 0.036 ns |         - |
| Divide    | ThreeWords | Aligned    | 123.30 ns | 0.583 ns | 0.517 ns |         - |
| Remainder | ThreeWords | Aligned    |  33.12 ns | 0.254 ns | 0.237 ns |         - |
| **Add**       | **ThreeWords** | **Misaligned** |  **14.60 ns** | **0.084 ns** | **0.078 ns** |         **-** |
| Subtract  | ThreeWords | Misaligned |  16.13 ns | 0.055 ns | 0.052 ns |         - |
| Multiply  | ThreeWords | Misaligned |  13.17 ns | 0.046 ns | 0.043 ns |         - |
| Divide    | ThreeWords | Misaligned | 116.64 ns | 0.608 ns | 0.569 ns |         - |
| Remainder | ThreeWords | Misaligned |  32.26 ns | 0.237 ns | 0.198 ns |         - |
| **Add**       | **FourWords**  | **Aligned**    |  **12.64 ns** | **0.112 ns** | **0.105 ns** |         **-** |
| Subtract  | FourWords  | Aligned    |  11.21 ns | 0.064 ns | 0.057 ns |         - |
| Multiply  | FourWords  | Aligned    |  51.82 ns | 0.550 ns | 0.514 ns |         - |
| Divide    | FourWords  | Aligned    | 128.87 ns | 0.808 ns | 0.755 ns |         - |
| Remainder | FourWords  | Aligned    |  35.64 ns | 0.252 ns | 0.210 ns |         - |
| **Add**       | **FourWords**  | **Misaligned** |  **16.65 ns** | **0.105 ns** | **0.098 ns** |         **-** |
| Subtract  | FourWords  | Misaligned |  16.98 ns | 0.084 ns | 0.079 ns |         - |
| Multiply  | FourWords  | Misaligned |  14.48 ns | 0.074 ns | 0.065 ns |         - |
| Divide    | FourWords  | Misaligned | 117.05 ns | 1.120 ns | 0.993 ns |         - |
| Remainder | FourWords  | Misaligned |  37.80 ns | 0.298 ns | 0.264 ns |         - |
