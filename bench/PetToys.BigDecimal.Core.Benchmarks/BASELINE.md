# Baseline

Taken on 2026-09-02, replacing the run of 2026-08-26 in full. This file is a
copy of one run's own GitHub-markdown export, one section per benchmark class,
with this note prefixed. Every section comes from that single run: a targeted
re-run overwrites only the classes it names, so a file assembled out of the
artifacts directory can otherwise mix code that shipped with code that did not.
The environment block below is the export's own and was identical in all sixteen
sections, so it is stated once rather than sixteen times. It is the same machine
the previous baseline was taken on, and `decimal`'s own figures land where they
did there, so the durations are comparable against it and not only the ratios.

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

Five criteria are stated over these operations. Two that were missed are now
met, two were already met, and two remain missed and belong to the formatting
work. The numbers below are as measured: no operand was chosen after the fact
to make one pass, and no favourable repeat run was substituted for the one this
file records.

| Criterion | Budget | Worst measured | Verdict |
| --------- | ------ | -------------- | ------- |
| Add, subtract, multiply within 3x, one or two words | 3x | Add 2.85x, Subtract 2.83x, Multiply 2.90x | met, and close to the line |
| Divide within 10x | 10x | 6.92x | met |
| An exact division costs no more than an inexact one | ratio 1 | 26.5 ns against 82.2 ns, **0.32x** | met |
| Parse and TryFormat within 3x | 3x | Parse 1.25x; TryFormat 3.87x char, 4.91x UTF-8 | **missed** by TryFormat |
| Zero allocations on every operation | 0 B | 64 B on the `N` specifier, both overloads | **missed** |

Read against the ratios rather than the headline:

- **Exactness is settled, and by a distance.** The same dividend divided
  exactly costs 26.5 ns against 82.2 ns inexact, where the previous baseline had
  it at 485.3 ns against 56.6 ns and the criterion inverted by 8.6x. The
  quotient is no longer manufactured at full precision and then stripped of the
  trailing zeros that lifting produced: an exact quotient is looked for first,
  from a trial division at the scale difference and from the divisor's factors.
- **Add, subtract and multiply are inside 3x**, at 2.85x, 2.83x and 2.90x,
  against 3.87x, 3.73x and 2.94x before. Add and subtract are met by a margin
  narrow enough that the caveat below matters.
- **Division is inside at 6.92x** against a 10x budget, and it is dearer than
  the 4.61x of the previous baseline. That is the price of the two checks for
  an exact quotient: an inexact division runs both and benefits from neither.
  The budget has the room, and the alternative, continuing the long division
  from the trial's remainder rather than restarting it, is written down as an
  open question rather than attempted here.
- **Remainder runs 1.02x to 4.97x**, against 1.11x to 5.87x before it.
- **Parsing is the comfortable result**, 0.89x to 1.25x across all four
  overloads, with four of the eight rows faster than `decimal`. The two-word
  `Parse` baseline was bimodal in the previous run and is not in this one.
- **Formatting misses on the `N` specifier alone.** `F9` and `G` run 1.58x to
  2.66x, inside the budget; `N2` runs 3.16x to 4.91x and is also the only
  format that allocates.
- **The allocation is 64 bytes on `N`, in both overloads**, unchanged and
  untouched. It is two reads of `NumberFormatInfo.NumberGroupSizes` on a single
  line, a property whose getter clones its array on every read. It belongs to
  the formatting work, along with the miss above.

Outside the budgets, hashing is the row that moved. `GetHashCode` costs 8.6 ns
to 11.6 ns on a value with no trailing zeros, against 12.3 ns to 19.0 ns
before, because such a value now performs no division of its magnitude at all.
On a value carrying eleven trailing zeros, which is what `WithScale` produces
when a caller widens to a column's scale, it costs 15.9 ns to 23.3 ns against
96.4 ns to 109.4 ns before, which is 1.60x to 2.02x the narrow value against a
guard of 4x. Four widening rows are new to this run and they give the shape of
the curve rather than one point on it: one zero, eleven, nineteen and
twenty-five. One to nineteen costs 1.35x to 1.43x, where removing a zero at a
time would cost about nineteen times; twenty-five crosses into a second
division and costs one step more, roughly double.

`ToBinaryFloat` remains the dearest operation in the suite at 139 ns to 170 ns,
down from 169 ns to 219 ns, and no criterion is stated over it.

## A caveat on the addition and subtraction rows

`AddBenchmarks` and `SubtractBenchmarks` at the aligned shapes are the
measurements to distrust here. The same binary, run four times on this machine
under `DefaultJob`, reports one word aligned at 2.25x, 2.85x, 3.02x and 3.14x,
and two words aligned at 2.36x, 2.47x, 2.51x and 3.16x. The error bars within
each run are a fraction of a percent, so the spread is between runs rather than
inside them: it is code layout and JIT tiering, not the operands and not the
machine being busy.

The numbers recorded above are this run's, and this run has them inside the
budget. Read them as an operation that has moved from 3.87x into the region of
3x and now sits close to the line, rather than as one comfortably inside it. A
single future run reporting 3.1x is not evidence of a regression, and a single
run reporting 2.3x is not evidence that the margin has grown.

What remains of the cost is fixed rather than proportional: `CopyMagnitude`
clears every word of the working buffer above the four it writes, and the
aligned shape is where `decimal` is fastest and so where any fixed cost reads
worst. Narrowing that buffer to what an aligned addition needs was tried and
made things worse, because a `stackalloc` whose length is not a constant becomes
a dynamic allocation and cost the misaligned shapes more than it saved the
aligned ones.

## Add - budget 3x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **4.343 ns** | **0.0511 ns** | **0.0453 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 12.396 ns | 0.1566 ns | 0.1464 ns |  2.85 |    0.04 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **5.177 ns** | **0.0369 ns** | **0.0327 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 12.207 ns | 0.1317 ns | 0.1167 ns |  2.36 |    0.03 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **4.076 ns** | **0.0398 ns** | **0.0353 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 10.064 ns | 0.0810 ns | 0.0718 ns |  2.47 |    0.03 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **5.358 ns** | **0.0274 ns** | **0.0243 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 14.667 ns | 0.1542 ns | 0.1367 ns |  2.74 |    0.03 |         - |          NA |

## Subtract - budget 3x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **4.294 ns** | **0.0263 ns** | **0.0246 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 12.156 ns | 0.0796 ns | 0.0745 ns |  2.83 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **5.176 ns** | **0.0194 ns** | **0.0172 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 14.307 ns | 0.0972 ns | 0.0910 ns |  2.76 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **4.326 ns** | **0.0162 ns** | **0.0135 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 10.364 ns | 0.1107 ns | 0.1035 ns |  2.40 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **5.408 ns** | **0.0201 ns** | **0.0167 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 14.614 ns | 0.1899 ns | 0.1777 ns |  2.70 |    0.03 |         - |          NA |

## Multiply - budget 3x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **3.632 ns** | **0.0204 ns** | **0.0170 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 10.518 ns | 0.0910 ns | 0.0851 ns |  2.90 |    0.03 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **3.632 ns** | **0.0118 ns** | **0.0105 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 10.510 ns | 0.0601 ns | 0.0562 ns |  2.89 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **4.513 ns** | **0.0278 ns** | **0.0260 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 12.135 ns | 0.0789 ns | 0.0738 ns |  2.69 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **4.542 ns** | **0.0209 ns** | **0.0175 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 12.269 ns | 0.1884 ns | 0.1762 ns |  2.70 |    0.04 |         - |          NA |

## Divide - budget 10x

| Method   | Shape    | Pairing    | Mean      | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **15.68 ns** | **0.158 ns** | **0.140 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 108.50 ns | 1.440 ns | 1.347 ns |  6.92 |    0.10 |         - |          NA |
|          |          |            |           |          |          |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **20.19 ns** | **0.141 ns** | **0.132 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned |  98.00 ns | 0.822 ns | 0.769 ns |  4.85 |    0.05 |         - |          NA |
|          |          |            |           |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **17.79 ns** | **0.078 ns** | **0.065 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 122.81 ns | 1.084 ns | 1.014 ns |  6.90 |    0.06 |         - |          NA |
|          |          |            |           |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **19.38 ns** | **0.079 ns** | **0.074 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 107.53 ns | 1.167 ns | 1.092 ns |  5.55 |    0.06 |         - |          NA |

## Remainder - budget 10x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **4.976 ns** | **0.0331 ns** | **0.0310 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 24.721 ns | 0.3117 ns | 0.2916 ns |  4.97 |    0.06 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **6.468 ns** | **0.1108 ns** | **0.0983 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 27.025 ns | 0.3863 ns | 0.3424 ns |  4.18 |    0.08 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    | **24.017 ns** | **0.1596 ns** | **0.1415 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 24.549 ns | 0.2129 ns | 0.1991 ns |  1.02 |    0.01 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** | **25.577 ns** | **0.1409 ns** | **0.1249 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 26.958 ns | 0.1903 ns | 0.1589 ns |  1.05 |    0.01 |         - |          NA |

## Exact division - budget not dearer than inexact

| Method   | Exact | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |------ |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **False** | **19.682 ns** | **0.1978 ns** | **0.1754 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | False | 82.160 ns | 0.6969 ns | 0.6519 ns |  4.17 |    0.05 |         - |          NA |
|          |       |           |           |           |       |         |           |             |
| **Baseline** | **True**  |  **8.802 ns** | **0.1943 ns** | **0.1908 ns** |  **1.00** |    **0.03** |         **-** |          **NA** |
| Measured | True  | 26.451 ns | 0.2469 ns | 0.2310 ns |  3.01 |    0.07 |         - |          NA |

## Parse - budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **55.05 ns** | **0.735 ns** | **0.687 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Measured | OneWord  | 57.52 ns | 0.731 ns | 0.648 ns |  1.05 |    0.02 |         - |          NA |
|          |          |          |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **86.29 ns** | **0.883 ns** | **0.826 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | 77.41 ns | 0.817 ns | 0.764 ns |  0.90 |    0.01 |         - |          NA |

## TryParse - budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **55.21 ns** | **0.350 ns** | **0.328 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | 57.94 ns | 0.527 ns | 0.493 ns |  1.05 |         - |          NA |
|          |          |          |          |          |       |           |             |
| **Baseline** | **TwoWords** | **87.16 ns** | **0.612 ns** | **0.572 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | 77.41 ns | 0.599 ns | 0.561 ns |  0.89 |         - |          NA |

## Parse (UTF-8) - budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **53.02 ns** | **0.361 ns** | **0.337 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | 66.15 ns | 0.351 ns | 0.328 ns |  1.25 |         - |          NA |
|          |          |          |          |          |       |           |             |
| **Baseline** | **TwoWords** | **86.44 ns** | **0.417 ns** | **0.390 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | 88.07 ns | 0.928 ns | 0.868 ns |  1.02 |         - |          NA |

## TryParse (UTF-8) - budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **53.33 ns** | **0.397 ns** | **0.372 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | 66.12 ns | 0.720 ns | 0.673 ns |  1.24 |         - |          NA |
|          |          |          |          |          |       |           |             |
| **Baseline** | **TwoWords** | **89.63 ns** | **0.799 ns** | **0.748 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | 86.86 ns | 0.384 ns | 0.321 ns |  0.97 |         - |          NA |

## TryFormat - budget 3x

| Method   | Shape    | Format | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------- |--------- |------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **Baseline** | **OneWord**  | **F9**     |  **41.98 ns** | **0.334 ns** | **0.296 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | F9     |  95.59 ns | 1.367 ns | 1.279 ns |  2.28 |    0.03 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **OneWord**  | **G**      |  **37.69 ns** | **0.269 ns** | **0.251 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | G      |  89.17 ns | 0.770 ns | 0.643 ns |  2.37 |    0.02 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **OneWord**  | **N2**     |  **45.82 ns** | **0.299 ns** | **0.233 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | N2     | 177.31 ns | 1.203 ns | 1.126 ns |  3.87 |    0.03 | 0.0050 |      64 B |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **F9**     |  **65.89 ns** | **0.750 ns** | **0.701 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | F9     | 106.12 ns | 1.000 ns | 0.887 ns |  1.61 |    0.02 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **G**      |  **57.83 ns** | **0.653 ns** | **0.611 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | G      |  91.48 ns | 0.369 ns | 0.345 ns |  1.58 |    0.02 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **N2**     |  **72.73 ns** | **0.591 ns** | **0.553 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | N2     | 229.51 ns | 1.390 ns | 1.300 ns |  3.16 |    0.03 | 0.0050 |      64 B |          NA |

## TryFormat (UTF-8) - budget 3x

| Method   | Shape    | Format | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------- |--------- |------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **Baseline** | **OneWord**  | **F9**     |  **39.88 ns** | **0.357 ns** | **0.334 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | F9     | 106.21 ns | 0.998 ns | 0.933 ns |  2.66 |    0.03 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **OneWord**  | **G**      |  **36.45 ns** | **0.258 ns** | **0.241 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | G      |  96.80 ns | 1.103 ns | 1.032 ns |  2.66 |    0.03 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **OneWord**  | **N2**     |  **43.45 ns** | **0.455 ns** | **0.426 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | N2     | 213.27 ns | 2.160 ns | 2.020 ns |  4.91 |    0.06 | 0.0050 |      64 B |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **F9**     |  **65.56 ns** | **0.529 ns** | **0.495 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | F9     | 117.66 ns | 1.501 ns | 1.404 ns |  1.79 |    0.02 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **G**      |  **55.01 ns** | **0.318 ns** | **0.297 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | G      | 102.65 ns | 0.494 ns | 0.412 ns |  1.87 |    0.01 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **N2**     |  **70.91 ns** | **0.474 ns** | **0.420 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | N2     | 256.29 ns | 3.628 ns | 3.394 ns |  3.61 |    0.05 | 0.0048 |      64 B |          NA |

## Scale changes - no budget

| Method            | Shape    | Mean      | Error     | StdDev    | Allocated |
|------------------ |--------- |----------:|----------:|----------:|----------:|
| **RoundToEven**       | **OneWord**  | **11.711 ns** | **0.0594 ns** | **0.0555 ns** |         **-** |
| RoundAwayFromZero | OneWord  | 11.603 ns | 0.1021 ns | 0.0853 ns |         - |
| RoundReference    | OneWord  |  3.882 ns | 0.0333 ns | 0.0312 ns |         - |
| Narrow            | OneWord  | 12.700 ns | 0.0453 ns | 0.0378 ns |         - |
| Widen             | OneWord  |  7.385 ns | 0.0844 ns | 0.0705 ns |         - |
| **RoundToEven**       | **TwoWords** | **15.241 ns** | **0.0891 ns** | **0.0834 ns** |         **-** |
| RoundAwayFromZero | TwoWords | 14.619 ns | 0.1323 ns | 0.1237 ns |         - |
| RoundReference    | TwoWords |  4.774 ns | 0.0426 ns | 0.0398 ns |         - |
| Narrow            | TwoWords | 16.157 ns | 0.1159 ns | 0.1027 ns |         - |
| Widen             | TwoWords |  7.862 ns | 0.0841 ns | 0.0786 ns |         - |

## Ordering and hashing - no budget

| Method              | Shape    | Pairing    | Mean       | Error     | StdDev    | Allocated |
|-------------------- |--------- |----------- |-----------:|----------:|----------:|----------:|
| **Compare**             | **OneWord**  | **Aligned**    |  **6.0618 ns** | **0.0730 ns** | **0.0647 ns** |         **-** |
| CompareReference    | OneWord  | Aligned    |  1.5010 ns | 0.0201 ns | 0.0168 ns |         - |
| Hash                | OneWord  | Aligned    |  8.6100 ns | 0.0765 ns | 0.0679 ns |         - |
| HashReference       | OneWord  | Aligned    |  0.6472 ns | 0.0096 ns | 0.0090 ns |         - |
| HashWidenedOne      | OneWord  | Aligned    | 13.1156 ns | 0.0875 ns | 0.0818 ns |         - |
| HashWidened         | OneWord  | Aligned    | 15.9357 ns | 0.2019 ns | 0.1790 ns |         - |
| HashWidenedNineteen | OneWord  | Aligned    | 18.0483 ns | 0.1269 ns | 0.1187 ns |         - |
| HashWidenedBeyond   | OneWord  | Aligned    | 37.3812 ns | 0.2893 ns | 0.2416 ns |         - |
| **Compare**             | **OneWord**  | **Misaligned** |  **9.8635 ns** | **0.0924 ns** | **0.0819 ns** |         **-** |
| CompareReference    | OneWord  | Misaligned |  2.0107 ns | 0.0223 ns | 0.0198 ns |         - |
| Hash                | OneWord  | Misaligned | 10.2644 ns | 0.0926 ns | 0.0867 ns |         - |
| HashReference       | OneWord  | Misaligned |  0.7126 ns | 0.0259 ns | 0.0242 ns |         - |
| HashWidenedOne      | OneWord  | Misaligned | 13.2597 ns | 0.0810 ns | 0.0676 ns |         - |
| HashWidened         | OneWord  | Misaligned | 16.4128 ns | 0.0923 ns | 0.0863 ns |         - |
| HashWidenedNineteen | OneWord  | Misaligned | 18.1159 ns | 0.1008 ns | 0.0841 ns |         - |
| HashWidenedBeyond   | OneWord  | Misaligned | 37.7520 ns | 0.4181 ns | 0.3911 ns |         - |
| **Compare**             | **TwoWords** | **Aligned**    |  **5.4221 ns** | **0.0537 ns** | **0.0502 ns** |         **-** |
| CompareReference    | TwoWords | Aligned    |  1.5343 ns | 0.0236 ns | 0.0221 ns |         - |
| Hash                | TwoWords | Aligned    | 11.5720 ns | 0.1155 ns | 0.1080 ns |         - |
| HashReference       | TwoWords | Aligned    |  0.7319 ns | 0.0302 ns | 0.0267 ns |         - |
| HashWidenedOne      | TwoWords | Aligned    | 17.3076 ns | 0.1541 ns | 0.1366 ns |         - |
| HashWidened         | TwoWords | Aligned    | 23.3056 ns | 0.1728 ns | 0.1532 ns |         - |
| HashWidenedNineteen | TwoWords | Aligned    | 24.8110 ns | 0.4290 ns | 0.4013 ns |         - |
| HashWidenedBeyond   | TwoWords | Aligned    | 37.4928 ns | 0.2576 ns | 0.2409 ns |         - |
| **Compare**             | **TwoWords** | **Misaligned** | **14.8092 ns** | **0.2073 ns** | **0.1939 ns** |         **-** |
| CompareReference    | TwoWords | Misaligned |  2.0024 ns | 0.0305 ns | 0.0271 ns |         - |
| Hash                | TwoWords | Misaligned | 11.5382 ns | 0.0903 ns | 0.0800 ns |         - |
| HashReference       | TwoWords | Misaligned |  0.7376 ns | 0.0310 ns | 0.0290 ns |         - |
| HashWidenedOne      | TwoWords | Misaligned | 18.3782 ns | 0.1943 ns | 0.1723 ns |         - |
| HashWidened         | TwoWords | Misaligned | 23.3031 ns | 0.1701 ns | 0.1591 ns |         - |
| HashWidenedNineteen | TwoWords | Misaligned | 24.8147 ns | 0.2779 ns | 0.2599 ns |         - |
| HashWidenedBeyond   | TwoWords | Misaligned | 37.8698 ns | 0.4847 ns | 0.4048 ns |         - |

## Conversions - no budget

| Method        | Shape    | Mean        | Error     | StdDev    | Allocated |
|-------------- |--------- |------------:|----------:|----------:|----------:|
| **ToReference**   | **OneWord**  |   **7.5835 ns** | **0.1123 ns** | **0.1050 ns** |         **-** |
| FromReference | OneWord  |   0.8061 ns | 0.0159 ns | 0.0149 ns |         - |
| ToBinaryFloat | OneWord  | 139.2491 ns | 1.2751 ns | 1.1927 ns |         - |
| FromInteger   | OneWord  |   0.1019 ns | 0.0214 ns | 0.0200 ns |         - |
| ToWords       | OneWord  |   1.5511 ns | 0.0394 ns | 0.0368 ns |         - |
| FromWords     | OneWord  |   2.8366 ns | 0.0680 ns | 0.0636 ns |         - |
| **ToReference**   | **TwoWords** |   **7.6362 ns** | **0.1295 ns** | **0.1211 ns** |         **-** |
| FromReference | TwoWords |   0.8332 ns | 0.0175 ns | 0.0146 ns |         - |
| ToBinaryFloat | TwoWords | 170.1827 ns | 1.8989 ns | 1.7762 ns |         - |
| FromInteger   | TwoWords |   0.1082 ns | 0.0194 ns | 0.0182 ns |         - |
| ToWords       | TwoWords |   1.1531 ns | 0.0180 ns | 0.0159 ns |         - |
| FromWords     | TwoWords |   2.1490 ns | 0.0497 ns | 0.0465 ns |         - |

## Three- and four-word operands - no budget

| Method    | Shape      | Pairing    | Mean      | Error    | StdDev   | Allocated |
|---------- |----------- |----------- |----------:|---------:|---------:|----------:|
| **Add**       | **ThreeWords** | **Aligned**    |  **12.45 ns** | **0.193 ns** | **0.171 ns** |         **-** |
| Subtract  | ThreeWords | Aligned    |  10.60 ns | 0.042 ns | 0.037 ns |         - |
| Multiply  | ThreeWords | Aligned    |  13.18 ns | 0.125 ns | 0.111 ns |         - |
| Divide    | ThreeWords | Aligned    | 129.12 ns | 1.085 ns | 0.962 ns |         - |
| Remainder | ThreeWords | Aligned    |  32.98 ns | 0.308 ns | 0.289 ns |         - |
| **Add**       | **ThreeWords** | **Misaligned** |  **14.56 ns** | **0.123 ns** | **0.115 ns** |         **-** |
| Subtract  | ThreeWords | Misaligned |  15.31 ns | 0.101 ns | 0.084 ns |         - |
| Multiply  | ThreeWords | Misaligned |  13.28 ns | 0.081 ns | 0.076 ns |         - |
| Divide    | ThreeWords | Misaligned | 119.96 ns | 1.174 ns | 1.041 ns |         - |
| Remainder | ThreeWords | Misaligned |  32.35 ns | 0.355 ns | 0.315 ns |         - |
| **Add**       | **FourWords**  | **Aligned**    |  **10.47 ns** | **0.090 ns** | **0.084 ns** |         **-** |
| Subtract  | FourWords  | Aligned    |  11.99 ns | 0.120 ns | 0.112 ns |         - |
| Multiply  | FourWords  | Aligned    |  51.74 ns | 0.287 ns | 0.269 ns |         - |
| Divide    | FourWords  | Aligned    | 129.02 ns | 1.236 ns | 1.156 ns |         - |
| Remainder | FourWords  | Aligned    |  38.62 ns | 0.372 ns | 0.348 ns |         - |
| **Add**       | **FourWords**  | **Misaligned** |  **16.65 ns** | **0.163 ns** | **0.145 ns** |         **-** |
| Subtract  | FourWords  | Misaligned |  17.19 ns | 0.200 ns | 0.187 ns |         - |
| Multiply  | FourWords  | Misaligned |  14.64 ns | 0.115 ns | 0.108 ns |         - |
| Divide    | FourWords  | Misaligned | 118.05 ns | 0.583 ns | 0.516 ns |         - |
| Remainder | FourWords  | Misaligned |  38.00 ns | 0.250 ns | 0.234 ns |         - |
