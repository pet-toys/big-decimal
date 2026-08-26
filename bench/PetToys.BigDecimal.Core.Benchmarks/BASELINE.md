# Baseline

Taken on 2026-08-26. This file is a copy of the run's own GitHub-markdown
export, one section per benchmark class, with this note prefixed. The
environment block below is the export's own and was identical in all sixteen
sections, so it is stated once rather than sixteen times.

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

Four criteria are stated over these operations. Three of them are missed, and
the numbers below are as measured — no operand was chosen after the fact to
make one pass.

| Criterion | Budget | Worst measured | Verdict |
| --------- | ------ | -------------- | ------- |
| Add, subtract, multiply within 3x, one or two words | 3x | Add 3.87x, Subtract 3.73x, Multiply 2.94x | **missed** by add and subtract |
| Divide within 10x | 10x | 4.61x | met |
| An exact division costs no more than an inexact one | ratio 1 | 485.3 ns against 56.6 ns, **8.6x dearer** | **missed** |
| Parse and TryFormat within 3x | 3x | Parse 1.21x; TryFormat 4.43x char, 4.71x UTF-8 | **missed** by TryFormat |
| Zero allocations on every operation | 0 B | 64 B on the `N` specifier, both overloads | **missed** |

Read against the ratios rather than the headline:

- **Add and subtract miss narrowly and only in places.** Add is 3.87x at one
  word aligned and 2.60x at one word misaligned; subtract runs 2.94x to 3.73x.
  A budget is met only if the worst shape is inside it, so both are missed, but
  the gap is a fraction rather than a factor.
- **Multiply is inside at 2.94x** at its worst, and it is the one arithmetic
  criterion this run does not fault.
- **Division is comfortable at 4.61x** against a 10x budget. The division
  primitive is not the problem the criteria describe.
- **Exactness is.** The same dividend divided exactly costs 485.3 ns against
  56.6 ns inexact, so the criterion is missed by 8.6x in the wrong direction.
  The cause is the trailing-zero strip, which performs one full multi-word
  division per zero removed.
- **Parsing is the comfortable result**, 1.15x to 1.21x across all four
  overloads, and two of the eight rows are faster than `decimal`.
- **Formatting misses on the `N` specifier alone.** `F9` and `G` run 1.62x to
  2.62x, inside the budget; `N2` runs 3.28x to 4.71x and is also the only
  format that allocates.
- **The allocation is 64 bytes on `N`, in both overloads**, which is the one
  criterion stated without qualification. It is two reads of
  `NumberFormatInfo.NumberGroupSizes` on a single line, a property whose getter
  clones its array on every read.

Two rows outside every budget are worth carrying forward. `GetHashCode` costs
12.3 ns to 19.0 ns against `decimal`'s 0.7 ns, and 96 ns to 109 ns on a value
carrying trailing zeros — the shape `WithScale` produces when a value is widened
to a column's scale. `ToBinaryFloat` costs 169 ns to 219 ns, the dearest
operation in the suite.

## A caveat on one pair of rows

`ParseBenchmarks` at two words is the only unreliable measurement here.
BenchmarkDotNet reports its `decimal` baseline as bimodal (mValue 3.24) and the
report carries a `Median` column for it; the error bars are wide enough that
its ratio should not be quoted. `TryParseBenchmarks` measures the same operands
through the same path and is clean at 0.89x, so the conclusion for parsing does
not rest on the noisy pair.


## Add — budget 3x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **4.055 ns** | **0.0250 ns** | **0.0222 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 15.681 ns | 0.1249 ns | 0.1168 ns |  3.87 |    0.03 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **5.186 ns** | **0.0329 ns** | **0.0308 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 13.493 ns | 0.1428 ns | 0.1335 ns |  2.60 |    0.03 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **4.009 ns** | **0.0109 ns** | **0.0097 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 10.961 ns | 0.0512 ns | 0.0479 ns |  2.73 |    0.01 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **5.303 ns** | **0.0135 ns** | **0.0126 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 18.597 ns | 0.2066 ns | 0.1932 ns |  3.51 |    0.04 |         - |          NA |

## Subtract — budget 3x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **4.210 ns** | **0.0073 ns** | **0.0068 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 15.674 ns | 0.1787 ns | 0.1584 ns |  3.72 |    0.04 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **5.114 ns** | **0.0380 ns** | **0.0318 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 15.035 ns | 0.0727 ns | 0.0680 ns |  2.94 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **4.161 ns** | **0.0101 ns** | **0.0090 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 15.519 ns | 0.2247 ns | 0.2102 ns |  3.73 |    0.05 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **5.477 ns** | **0.1226 ns** | **0.1147 ns** |  **1.00** |    **0.03** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 18.210 ns | 0.1694 ns | 0.1584 ns |  3.33 |    0.07 |         - |          NA |

## Multiply — budget 3x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **3.577 ns** | **0.0058 ns** | **0.0046 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 10.429 ns | 0.0406 ns | 0.0380 ns |  2.92 |    0.01 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **3.584 ns** | **0.0170 ns** | **0.0142 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 10.526 ns | 0.0270 ns | 0.0225 ns |  2.94 |    0.01 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **4.461 ns** | **0.0405 ns** | **0.0379 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 12.113 ns | 0.0525 ns | 0.0466 ns |  2.72 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **4.407 ns** | **0.0132 ns** | **0.0124 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 12.202 ns | 0.1398 ns | 0.1239 ns |  2.77 |    0.03 |         - |          NA |

## Divide — budget 10x

| Method   | Shape    | Pairing    | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    | **16.44 ns** | **0.237 ns** | **0.403 ns** |  **1.00** |    **0.03** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 75.79 ns | 0.439 ns | 0.389 ns |  4.61 |    0.11 |         - |          NA |
|          |          |            |          |          |          |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** | **20.33 ns** | **0.305 ns** | **0.285 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 73.48 ns | 0.341 ns | 0.302 ns |  3.61 |    0.05 |         - |          NA |
|          |          |            |          |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    | **17.73 ns** | **0.075 ns** | **0.062 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 79.48 ns | 0.383 ns | 0.359 ns |  4.48 |    0.02 |         - |          NA |
|          |          |            |          |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** | **19.42 ns** | **0.097 ns** | **0.091 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 72.94 ns | 0.280 ns | 0.248 ns |  3.76 |    0.02 |         - |          NA |

## Remainder — budget 10x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **4.964 ns** | **0.0294 ns** | **0.0275 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 29.146 ns | 0.4281 ns | 0.4004 ns |  5.87 |    0.08 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **6.547 ns** | **0.0699 ns** | **0.0583 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 30.310 ns | 0.3317 ns | 0.3102 ns |  4.63 |    0.06 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    | **23.490 ns** | **0.0235 ns** | **0.0196 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 26.008 ns | 0.1112 ns | 0.1040 ns |  1.11 |    0.00 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** | **25.197 ns** | **0.0538 ns** | **0.0420 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 27.979 ns | 0.1050 ns | 0.0931 ns |  1.11 |    0.00 |         - |          NA |

## Exact division — budget not dearer than inexact

| Method   | Exact | Mean       | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |------ |-----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **False** |  **19.316 ns** | **0.2488 ns** | **0.2327 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Measured | False |  56.596 ns | 0.2631 ns | 0.2461 ns |  2.93 |    0.04 |         - |          NA |
|          |       |            |           |           |       |         |           |             |
| **Baseline** | **True**  |   **9.178 ns** | **0.0396 ns** | **0.0351 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | True  | 485.320 ns | 4.7409 ns | 4.4347 ns | 52.88 |    0.51 |         - |          NA |

## Parse — budget 3x

| Method   | Shape    | Mean      | Error     | StdDev    | Median    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------:|----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  |  **53.49 ns** |  **0.191 ns** |  **0.179 ns** |  **53.46 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  |  62.21 ns |  0.320 ns |  0.284 ns |  62.20 ns |  1.16 |    0.01 |         - |          NA |
|          |          |           |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **137.85 ns** | **13.122 ns** | **38.691 ns** | **162.22 ns** |  **1.10** |    **0.48** |         **-** |          **NA** |
| Measured | TwoWords | 122.49 ns |  8.606 ns | 25.376 ns | 137.55 ns |  0.98 |    0.38 |         - |          NA |

## TryParse — budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **54.66 ns** | **0.656 ns** | **0.614 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Measured | OneWord  | 63.00 ns | 0.425 ns | 0.398 ns |  1.15 |    0.01 |         - |          NA |
|          |          |          |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **87.09 ns** | **0.359 ns** | **0.335 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | 77.61 ns | 0.625 ns | 0.554 ns |  0.89 |    0.01 |         - |          NA |

## Parse (UTF-8) — budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **53.53 ns** | **0.186 ns** | **0.174 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | 64.73 ns | 0.419 ns | 0.392 ns |  1.21 |         - |          NA |
|          |          |          |          |          |       |           |             |
| **Baseline** | **TwoWords** | **87.52 ns** | **0.338 ns** | **0.316 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | 90.86 ns | 0.626 ns | 0.585 ns |  1.04 |         - |          NA |

## TryParse (UTF-8) — budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **55.05 ns** | **0.142 ns** | **0.133 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  | 66.86 ns | 0.970 ns | 0.860 ns |  1.21 |    0.02 |         - |          NA |
|          |          |          |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **87.13 ns** | **0.376 ns** | **0.333 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | 87.31 ns | 0.909 ns | 0.851 ns |  1.00 |    0.01 |         - |          NA |

## TryFormat — budget 3x

| Method   | Shape    | Format | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------- |--------- |------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **Baseline** | **OneWord**  | **F9**     |  **42.03 ns** | **0.175 ns** | **0.163 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | F9     |  95.33 ns | 0.785 ns | 0.696 ns |  2.27 |    0.02 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **OneWord**  | **G**      |  **36.96 ns** | **0.176 ns** | **0.164 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | G      |  87.99 ns | 1.044 ns | 0.977 ns |  2.38 |    0.03 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **OneWord**  | **N2**     |  **44.95 ns** | **0.225 ns** | **0.211 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | N2     | 199.01 ns | 1.184 ns | 1.108 ns |  4.43 |    0.03 | 0.0050 |      64 B |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **F9**     |  **65.76 ns** | **0.510 ns** | **0.477 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | F9     | 106.71 ns | 1.353 ns | 1.265 ns |  1.62 |    0.02 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **G**      |  **58.63 ns** | **0.391 ns** | **0.366 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | G      |  95.01 ns | 1.906 ns | 2.195 ns |  1.62 |    0.04 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **N2**     |  **73.04 ns** | **0.457 ns** | **0.405 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | N2     | 239.27 ns | 1.280 ns | 1.198 ns |  3.28 |    0.02 | 0.0048 |      64 B |          NA |

## TryFormat (UTF-8) — budget 3x

| Method   | Shape    | Format | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------- |--------- |------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **Baseline** | **OneWord**  | **F9**     |  **41.11 ns** | **0.144 ns** | **0.135 ns** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | F9     | 105.90 ns | 0.549 ns | 0.513 ns |  2.58 |    0.01 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **OneWord**  | **G**      |  **36.36 ns** | **0.215 ns** | **0.201 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | G      |  95.45 ns | 1.002 ns | 0.937 ns |  2.62 |    0.03 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **OneWord**  | **N2**     |  **42.44 ns** | **0.184 ns** | **0.172 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | OneWord  | N2     | 199.91 ns | 0.964 ns | 0.901 ns |  4.71 |    0.03 | 0.0050 |      64 B |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **F9**     |  **68.59 ns** | **0.330 ns** | **0.292 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | F9     | 118.60 ns | 0.809 ns | 0.756 ns |  1.73 |    0.01 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **G**      |  **55.55 ns** | **0.352 ns** | **0.329 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | G      | 105.32 ns | 1.571 ns | 1.469 ns |  1.90 |    0.03 |      - |         - |          NA |
|          |          |        |           |          |          |       |         |        |           |             |
| **Baseline** | **TwoWords** | **N2**     |  **71.52 ns** | **0.516 ns** | **0.483 ns** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| Measured | TwoWords | N2     | 267.69 ns | 1.940 ns | 1.620 ns |  3.74 |    0.03 | 0.0048 |      64 B |          NA |

## Scale changes — no budget

| Method            | Shape    | Mean      | Error     | StdDev    | Allocated |
|------------------ |--------- |----------:|----------:|----------:|----------:|
| **RoundToEven**       | **OneWord**  | **11.796 ns** | **0.0454 ns** | **0.0425 ns** |         **-** |
| RoundAwayFromZero | OneWord  | 11.588 ns | 0.0641 ns | 0.0600 ns |         - |
| RoundReference    | OneWord  |  3.805 ns | 0.0144 ns | 0.0128 ns |         - |
| Narrow            | OneWord  | 12.985 ns | 0.0442 ns | 0.0414 ns |         - |
| Widen             | OneWord  |  5.460 ns | 0.0281 ns | 0.0235 ns |         - |
| **RoundToEven**       | **TwoWords** | **14.595 ns** | **0.0815 ns** | **0.0722 ns** |         **-** |
| RoundAwayFromZero | TwoWords | 14.535 ns | 0.0803 ns | 0.0751 ns |         - |
| RoundReference    | TwoWords |  4.588 ns | 0.1205 ns | 0.1649 ns |         - |
| Narrow            | TwoWords | 16.278 ns | 0.0528 ns | 0.0494 ns |         - |
| Widen             | TwoWords |  5.541 ns | 0.0383 ns | 0.0359 ns |         - |

## Ordering and hashing — no budget

| Method           | Shape    | Pairing    | Mean        | Error     | StdDev    | Allocated |
|----------------- |--------- |----------- |------------:|----------:|----------:|----------:|
| **Compare**          | **OneWord**  | **Aligned**    |   **5.8696 ns** | **0.0370 ns** | **0.0309 ns** |         **-** |
| CompareReference | OneWord  | Aligned    |   1.5117 ns | 0.0158 ns | 0.0140 ns |         - |
| Hash             | OneWord  | Aligned    |  12.3442 ns | 0.0421 ns | 0.0394 ns |         - |
| HashReference    | OneWord  | Aligned    |   0.7333 ns | 0.0328 ns | 0.0307 ns |         - |
| HashWidened      | OneWord  | Aligned    |  96.4133 ns | 0.6479 ns | 0.6060 ns |         - |
| **Compare**          | **OneWord**  | **Misaligned** |   **9.8217 ns** | **0.0379 ns** | **0.0354 ns** |         **-** |
| CompareReference | OneWord  | Misaligned |   2.0233 ns | 0.0101 ns | 0.0089 ns |         - |
| Hash             | OneWord  | Misaligned |  13.2759 ns | 0.1466 ns | 0.1371 ns |         - |
| HashReference    | OneWord  | Misaligned |   0.6836 ns | 0.0201 ns | 0.0188 ns |         - |
| HashWidened      | OneWord  | Misaligned |  98.9482 ns | 2.0005 ns | 1.8713 ns |         - |
| **Compare**          | **TwoWords** | **Aligned**    |   **5.5286 ns** | **0.1396 ns** | **0.1371 ns** |         **-** |
| CompareReference | TwoWords | Aligned    |   1.5425 ns | 0.0154 ns | 0.0136 ns |         - |
| Hash             | TwoWords | Aligned    |  19.0422 ns | 0.2864 ns | 0.2679 ns |         - |
| HashReference    | TwoWords | Aligned    |   0.6813 ns | 0.0305 ns | 0.0285 ns |         - |
| HashWidened      | TwoWords | Aligned    | 109.4373 ns | 0.5124 ns | 0.4793 ns |         - |
| **Compare**          | **TwoWords** | **Misaligned** |  **14.7228 ns** | **0.2229 ns** | **0.2085 ns** |         **-** |
| CompareReference | TwoWords | Misaligned |   2.0170 ns | 0.0194 ns | 0.0162 ns |         - |
| Hash             | TwoWords | Misaligned |  18.8095 ns | 0.1071 ns | 0.0950 ns |         - |
| HashReference    | TwoWords | Misaligned |   0.6492 ns | 0.0163 ns | 0.0152 ns |         - |
| HashWidened      | TwoWords | Misaligned | 108.3984 ns | 0.2531 ns | 0.2367 ns |         - |

## Conversions — no budget

| Method        | Shape    | Mean        | Error     | StdDev    | Median      | Allocated |
|-------------- |--------- |------------:|----------:|----------:|------------:|----------:|
| **ToReference**   | **OneWord**  |  **16.1475 ns** | **0.7680 ns** | **2.2645 ns** |  **16.9453 ns** |         **-** |
| FromReference | OneWord  |   2.6946 ns | 0.0935 ns | 0.2756 ns |   2.7464 ns |         - |
| ToBinaryFloat | OneWord  | 218.5895 ns | 4.1139 ns | 4.2247 ns | 220.4534 ns |         - |
| FromInteger   | OneWord  |   0.3829 ns | 0.0485 ns | 0.1022 ns |   0.3897 ns |         - |
| ToWords       | OneWord  |   2.8253 ns | 0.0938 ns | 0.1515 ns |   2.8220 ns |         - |
| FromWords     | OneWord  |   2.3198 ns | 0.0405 ns | 0.0688 ns |   2.3266 ns |         - |
| **ToReference**   | **TwoWords** |   **8.2666 ns** | **0.1965 ns** | **0.2556 ns** |   **8.2095 ns** |         **-** |
| FromReference | TwoWords |   0.8517 ns | 0.0084 ns | 0.0075 ns |   0.8515 ns |         - |
| ToBinaryFloat | TwoWords | 169.0010 ns | 1.0559 ns | 0.9877 ns | 169.0304 ns |         - |
| FromInteger   | TwoWords |   0.1498 ns | 0.0124 ns | 0.0110 ns |   0.1499 ns |         - |
| ToWords       | TwoWords |   1.0971 ns | 0.0185 ns | 0.0173 ns |   1.0974 ns |         - |
| FromWords     | TwoWords |   2.1835 ns | 0.0502 ns | 0.0445 ns |   2.1831 ns |         - |

## Three- and four-word operands — no budget

| Method    | Shape      | Pairing    | Mean     | Error    | StdDev   | Allocated |
|---------- |----------- |----------- |---------:|---------:|---------:|----------:|
| **Add**       | **ThreeWords** | **Aligned**    | **15.93 ns** | **0.073 ns** | **0.065 ns** |         **-** |
| Subtract  | ThreeWords | Aligned    | 16.47 ns | 0.159 ns | 0.141 ns |         - |
| Multiply  | ThreeWords | Aligned    | 13.19 ns | 0.047 ns | 0.042 ns |         - |
| Divide    | ThreeWords | Aligned    | 82.51 ns | 1.197 ns | 1.120 ns |         - |
| Remainder | ThreeWords | Aligned    | 37.26 ns | 0.200 ns | 0.187 ns |         - |
| **Add**       | **ThreeWords** | **Misaligned** | **19.50 ns** | **0.195 ns** | **0.182 ns** |         **-** |
| Subtract  | ThreeWords | Misaligned | 16.91 ns | 0.150 ns | 0.141 ns |         - |
| Multiply  | ThreeWords | Misaligned | 13.23 ns | 0.049 ns | 0.046 ns |         - |
| Divide    | ThreeWords | Misaligned | 79.69 ns | 0.833 ns | 0.779 ns |         - |
| Remainder | ThreeWords | Misaligned | 39.59 ns | 0.246 ns | 0.230 ns |         - |
| **Add**       | **FourWords**  | **Aligned**    | **15.96 ns** | **0.082 ns** | **0.073 ns** |         **-** |
| Subtract  | FourWords  | Aligned    | 12.12 ns | 0.074 ns | 0.069 ns |         - |
| Multiply  | FourWords  | Aligned    | 52.72 ns | 0.574 ns | 0.537 ns |         - |
| Divide    | FourWords  | Aligned    | 79.98 ns | 1.523 ns | 1.425 ns |         - |
| Remainder | FourWords  | Aligned    | 37.99 ns | 0.440 ns | 0.412 ns |         - |
| **Add**       | **FourWords**  | **Misaligned** | **17.49 ns** | **0.311 ns** | **0.275 ns** |         **-** |
| Subtract  | FourWords  | Misaligned | 18.13 ns | 0.116 ns | 0.097 ns |         - |
| Multiply  | FourWords  | Misaligned | 14.60 ns | 0.060 ns | 0.053 ns |         - |
| Divide    | FourWords  | Misaligned | 74.77 ns | 0.428 ns | 0.401 ns |         - |
| Remainder | FourWords  | Misaligned | 44.80 ns | 0.182 ns | 0.161 ns |         - |
