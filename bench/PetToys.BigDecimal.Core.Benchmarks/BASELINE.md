# The recorded run

Taken on 2026-09-07, replacing the run of 2026-09-02 in full.

**This file is a dated account of one run, not a reference for the next one.**
Durations are not compared across runs at all, and a ratio to `System.Decimal`
is compared between two runs only when both pass the canary below. Where a
change has to show that something became faster, the comparison belongs inside
one run, between the new implementation and the one it replaced. The reasoning
is in `README.md` under "Whether a run may be used at all"; the short version is
that this is a working developer machine, and a run taken here on 2026-09-07
measured `System.Decimal` itself at 1.5x to 2.3x its recorded cost, which no
amount of care about the package could have detected from the package's own
rows.

Allocation figures are the exception. They are counts rather than durations and
travel anywhere.

Every section below is one class's own GitHub-markdown export from a single run
of the budgeted subset, `--anyCategories budget`, 136 benchmarks in 49 minutes.
A targeted re-run overwrites only the classes it names, so a file assembled out
of the artifacts directory can otherwise mix two states of the code and read as
one.

```
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2/2025Update/HudsonValley2)
12th Gen Intel Core i7-12700H 2.30GHz, 1 CPU, 20 logical and 14 physical cores
.NET SDK 10.0.400
  [Host]     : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.11 (10.0.11, 10.0.1126.37416), X64 RyuJIT x86-64-v3
```

`DefaultJob` is the point: a run taken with `--job short` or `--job dry` is not
comparable against this file, and neither is one taken on another machine.

## What is not in this file

The budgeted subset leaves out what carries no criterion: the conversions, the
scale changes, and `DivisionPrimitiveBenchmarks`, which measures the division
primitive against the `UInt128` form it replaced. That group is deliberately
absent rather than pasted in: it is a different run, and a record that mixes
runs is exactly what this file must not be. Its result, taken the same day on
its own: the primitive costs 0.54x to 0.32x of the replaced form on net10.0 as
the magnitude grows from one word to four, and 0.59x to 0.04x on net8.0, where
the runtime's software division of a `UInt128` is several times dearer.

## The canary

These are `System.Decimal`'s own rows, code this package does not change. A
later run whose ratios between them differ from these by more than 15% was taken
on a machine in a different state, and no budget may be graded from it.

Rows under 2 ns are excluded, because the instrument cannot support the test at
that size: `decimal.GetHashCode` measures 0.74 ns here, where a twelfth of a
nanosecond is 12% and says nothing about anything.

| Ratio | Rows | Value |
| --- | --- | --- |
| divide over add | `DivideBenchmarks` Baseline OneWord Aligned over `AddBenchmarks` Baseline OneWord Aligned | 3.851 |
| parse over add | `ParseBenchmarks` Baseline OneWord over the same add row | 12.983 |
| format over add | `FormatBenchmarks` Baseline OneWord F9 over the same add row | 10.421 |

On this run those moved by +6.7%, +2.4% and +7.8% from the 2026-09-02 record,
which is what a machine in the same state looks like.

## What the budgets say, and what this run measured

The numbers below are as measured. No operand was chosen after the fact to make
one pass, and no favourable repeat run was substituted for the one this file
records.

| Criterion | Budget | Worst measured | Verdict |
| --- | --- | --- | --- |
| Add | 3x | 3.11x, two words aligned | missed by 4% |
| Subtract | 3x | 2.82x | met |
| Multiply | 3x | 3.02x, one word | missed by 0.7% |
| Divide | 10x | 5.62x | met |
| Remainder | 10x | 3.79x | met |
| Parse and TryParse, char and UTF-8 | 3x | 1.31x | met |
| TryFormat, char and UTF-8 | 3x | 1.96x | met |
| An exact division against an inexact one | not dearer | 0.36x | met |
| Zero allocations | always | zero, every row | met |
| Hashing, widened against narrow | 2.5x | 2.01x | met |
| Hashing, nineteen zeros against one | 1.5x | 1.44x | met |

Two remarks on the two misses, and one on the ceiling.

**Add and multiply miss by a few percent, on paths nothing has touched.**
Neither addition nor multiplication reaches any code the division work changed:
`StripTrailingZeros`, the only caller of the modified helpers outside division
itself, is reached from `Divide` and from `GetHashCode` and from nowhere else.
The previous record already carried a caveat on exactly these rows, having found
them anywhere between 2.25x and 3.16x across four runs of one binary. They are
recorded as measured and they are not this change's to answer for; a change that
sets out to move them will have to measure them against their own predecessor in
one run, which is now what the capability asks of anybody claiming an
improvement.

**The hashing ceiling was tightened from 4x to 2.5x, not to 2x.** The proposal
had asked for 2x on the strength of the primitive being three times cheaper in
isolation. At the level of the operation that number does not appear: the gap
between hashing a value carrying eleven trailing zeros and the same value
without them is what it was, and the worst of the four shapes sits at 2.01x. So
the ceiling is set above the measurement with room for the few percent that
separates two runs of one binary here, rather than at the figure the primitive's
own ratio suggested. What the change demonstrably delivers is in the section
above: the primitive's own cost, and the removal of the net8.0 penalty.

## Reading the tables

`Ratio` is `Measured / Baseline` within one combination of parameters, and it is
the column a budget is read against. Classes with no `Baseline` method carry no
budget and print no ratio: those are the operations no criterion is stated over,
and the three- and four-word widths `decimal` cannot represent at all.


## Add - budget 3x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **4.248 ns** | **0.0135 ns** | **0.0112 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    |  9.994 ns | 0.0438 ns | 0.0366 ns |  2.35 |    0.01 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **5.488 ns** | **0.0633 ns** | **0.0592 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 12.368 ns | 0.0394 ns | 0.0369 ns |  2.25 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **4.053 ns** | **0.0229 ns** | **0.0203 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 12.610 ns | 0.0424 ns | 0.0397 ns |  3.11 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **5.738 ns** | **0.0216 ns** | **0.0192 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 13.475 ns | 0.0693 ns | 0.0615 ns |  2.35 |    0.01 |         - |          NA |

## Subtract - budget 3x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **4.244 ns** | **0.0138 ns** | **0.0122 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 11.970 ns | 0.0367 ns | 0.0325 ns |  2.82 |    0.01 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **5.444 ns** | **0.0657 ns** | **0.0614 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 14.571 ns | 0.0624 ns | 0.0553 ns |  2.68 |    0.03 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **4.263 ns** | **0.0252 ns** | **0.0236 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 10.567 ns | 0.0314 ns | 0.0294 ns |  2.48 |    0.01 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **5.700 ns** | **0.0555 ns** | **0.0520 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 14.575 ns | 0.0576 ns | 0.0510 ns |  2.56 |    0.02 |         - |          NA |

## Multiply - budget 3x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **3.592 ns** | **0.0130 ns** | **0.0102 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 10.842 ns | 0.1044 ns | 0.0976 ns |  3.02 |    0.03 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **3.590 ns** | **0.0206 ns** | **0.0193 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 10.777 ns | 0.0502 ns | 0.0470 ns |  3.00 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **4.481 ns** | **0.0204 ns** | **0.0170 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 12.326 ns | 0.0684 ns | 0.0606 ns |  2.75 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **4.468 ns** | **0.0144 ns** | **0.0127 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 12.269 ns | 0.0259 ns | 0.0230 ns |  2.75 |    0.01 |         - |          NA |

## Divide - budget 10x

| Method   | Shape    | Pairing    | Mean      | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **16.36 ns** | **0.280 ns** | **0.262 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    |  91.98 ns | 0.353 ns | 0.330 ns |  5.62 |    0.09 |         - |          NA |
|          |          |            |           |          |          |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **20.48 ns** | **0.218 ns** | **0.203 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned |  78.71 ns | 0.397 ns | 0.352 ns |  3.84 |    0.04 |         - |          NA |
|          |          |            |           |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    |  **18.61 ns** | **0.290 ns** | **0.271 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 102.61 ns | 0.311 ns | 0.291 ns |  5.52 |    0.08 |         - |          NA |
|          |          |            |           |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** |  **19.27 ns** | **0.079 ns** | **0.066 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned |  86.90 ns | 0.159 ns | 0.149 ns |  4.51 |    0.02 |         - |          NA |

## Remainder - budget 10x

| Method   | Shape    | Pairing    | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |----------- |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **Aligned**    |  **5.254 ns** | **0.0468 ns** | **0.0438 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Aligned    | 15.829 ns | 0.0590 ns | 0.0551 ns |  3.01 |    0.03 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **OneWord**  | **Misaligned** |  **6.715 ns** | **0.0450 ns** | **0.0399 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | Misaligned | 25.423 ns | 0.0701 ns | 0.0655 ns |  3.79 |    0.02 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Aligned**    | **23.724 ns** | **0.0870 ns** | **0.0814 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | Aligned    | 21.812 ns | 0.0782 ns | 0.0731 ns |  0.92 |    0.00 |         - |          NA |
|          |          |            |           |           |           |       |         |           |             |
| **Baseline** | **TwoWords** | **Misaligned** | **25.154 ns** | **0.1012 ns** | **0.0947 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | Misaligned | 24.165 ns | 0.1288 ns | 0.1142 ns |  0.96 |    0.01 |         - |          NA |

## Exact division - budget not dearer than inexact

| Method   | Exact | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |------ |----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **Baseline** | **False** | **19.105 ns** | **0.2478 ns** | **0.2070 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | False | 69.441 ns | 0.3737 ns | 0.3313 ns |  3.64 |    0.04 |         - |          NA |
|          |       |           |           |           |       |         |           |             |
| **Baseline** | **True**  |  **8.539 ns** | **0.0286 ns** | **0.0223 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | True  | 24.717 ns | 0.1828 ns | 0.1710 ns |  2.89 |    0.02 |         - |          NA |

## Parse - budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **55.15 ns** | **0.204 ns** | **0.191 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | 63.57 ns | 0.437 ns | 0.387 ns |  1.15 |         - |          NA |
|          |          |          |          |          |       |           |             |
| **Baseline** | **TwoWords** | **87.42 ns** | **0.221 ns** | **0.206 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | 77.09 ns | 0.522 ns | 0.463 ns |  0.88 |         - |          NA |

## TryParse - budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **53.92 ns** | **0.211 ns** | **0.197 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | 63.99 ns | 0.509 ns | 0.476 ns |  1.19 |         - |          NA |
|          |          |          |          |          |       |           |             |
| **Baseline** | **TwoWords** | **88.01 ns** | **0.243 ns** | **0.227 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | 76.26 ns | 0.494 ns | 0.438 ns |  0.87 |         - |          NA |

## Parse (UTF-8) - budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **56.69 ns** | **0.507 ns** | **0.474 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | 71.25 ns | 0.330 ns | 0.308 ns |  1.26 |         - |          NA |
|          |          |          |          |          |       |           |             |
| **Baseline** | **TwoWords** | **90.11 ns** | **0.610 ns** | **0.571 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | 88.03 ns | 0.850 ns | 0.795 ns |  0.98 |         - |          NA |

## TryParse (UTF-8) - budget 3x

| Method   | Shape    | Mean     | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|--------- |--------- |---------:|---------:|---------:|------:|----------:|------------:|
| **Baseline** | **OneWord**  | **53.58 ns** | **0.366 ns** | **0.343 ns** |  **1.00** |         **-** |          **NA** |
| Measured | OneWord  | 70.17 ns | 0.453 ns | 0.424 ns |  1.31 |         - |          NA |
|          |          |          |          |          |       |           |             |
| **Baseline** | **TwoWords** | **86.76 ns** | **0.487 ns** | **0.455 ns** |  **1.00** |         **-** |          **NA** |
| Measured | TwoWords | 86.15 ns | 0.249 ns | 0.233 ns |  0.99 |         - |          NA |

## TryFormat - budget 3x

| Method   | Shape    | Format | Mean      | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |------- |----------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **F9**     |  **44.27 ns** | **0.561 ns** | **0.525 ns** |  **1.00** |    **0.02** |         **-** |          **NA** |
| Measured | OneWord  | F9     |  66.66 ns | 0.303 ns | 0.253 ns |  1.51 |    0.02 |         - |          NA |
|          |          |        |           |          |          |       |         |           |             |
| **Baseline** | **OneWord**  | **G**      |  **38.10 ns** | **0.192 ns** | **0.180 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | G      |  64.46 ns | 0.629 ns | 0.588 ns |  1.69 |    0.02 |         - |          NA |
|          |          |        |           |          |          |       |         |           |             |
| **Baseline** | **OneWord**  | **N2**     |  **45.93 ns** | **0.164 ns** | **0.153 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | OneWord  | N2     |  73.11 ns | 0.320 ns | 0.284 ns |  1.59 |    0.01 |         - |          NA |
|          |          |        |           |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **F9**     |  **66.57 ns** | **0.386 ns** | **0.342 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | F9     |  70.63 ns | 0.274 ns | 0.257 ns |  1.06 |    0.01 |         - |          NA |
|          |          |        |           |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **G**      |  **58.20 ns** | **0.147 ns** | **0.130 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | G      |  67.89 ns | 0.232 ns | 0.217 ns |  1.17 |    0.00 |         - |          NA |
|          |          |        |           |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **N2**     |  **74.01 ns** | **0.397 ns** | **0.352 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | N2     | 106.78 ns | 0.424 ns | 0.397 ns |  1.44 |    0.01 |         - |          NA |

## TryFormat (UTF-8) - budget 3x

| Method   | Shape    | Format | Mean      | Error    | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|--------- |--------- |------- |----------:|---------:|---------:|------:|--------:|----------:|------------:|
| **Baseline** | **OneWord**  | **F9**     |  **40.45 ns** | **0.224 ns** | **0.198 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | F9     |  79.14 ns | 1.318 ns | 1.233 ns |  1.96 |    0.03 |         - |          NA |
|          |          |        |           |          |          |       |         |           |             |
| **Baseline** | **OneWord**  | **G**      |  **37.57 ns** | **0.248 ns** | **0.232 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | G      |  73.56 ns | 0.384 ns | 0.359 ns |  1.96 |    0.01 |         - |          NA |
|          |          |        |           |          |          |       |         |           |             |
| **Baseline** | **OneWord**  | **N2**     |  **44.28 ns** | **0.286 ns** | **0.267 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | OneWord  | N2     |  82.08 ns | 0.294 ns | 0.275 ns |  1.85 |    0.01 |         - |          NA |
|          |          |        |           |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **F9**     |  **67.47 ns** | **0.666 ns** | **0.623 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | F9     |  84.24 ns | 0.350 ns | 0.327 ns |  1.25 |    0.01 |         - |          NA |
|          |          |        |           |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **G**      |  **56.84 ns** | **0.179 ns** | **0.158 ns** |  **1.00** |    **0.00** |         **-** |          **NA** |
| Measured | TwoWords | G      |  79.54 ns | 0.596 ns | 0.558 ns |  1.40 |    0.01 |         - |          NA |
|          |          |        |           |          |          |       |         |           |             |
| **Baseline** | **TwoWords** | **N2**     |  **71.31 ns** | **0.374 ns** | **0.350 ns** |  **1.00** |    **0.01** |         **-** |          **NA** |
| Measured | TwoWords | N2     | 127.65 ns | 0.696 ns | 0.651 ns |  1.79 |    0.01 |         - |          NA |

## Ordering and hashing - no ratio budget, one ceiling

| Method              | Shape    | Pairing    | Mean       | Error     | StdDev    | Allocated |
|-------------------- |--------- |----------- |-----------:|----------:|----------:|----------:|
| **Compare**             | **OneWord**  | **Aligned**    |  **6.0997 ns** | **0.0490 ns** | **0.0409 ns** |         **-** |
| CompareReference    | OneWord  | Aligned    |  1.5252 ns | 0.0112 ns | 0.0100 ns |         - |
| Hash                | OneWord  | Aligned    |  9.6272 ns | 0.0895 ns | 0.0837 ns |         - |
| HashReference       | OneWord  | Aligned    |  0.7402 ns | 0.0166 ns | 0.0155 ns |         - |
| HashWidenedOne      | OneWord  | Aligned    | 15.2173 ns | 0.0609 ns | 0.0569 ns |         - |
| HashWidened         | OneWord  | Aligned    | 18.7569 ns | 0.0943 ns | 0.0836 ns |         - |
| HashWidenedNineteen | OneWord  | Aligned    | 21.9118 ns | 0.1073 ns | 0.1004 ns |         - |
| HashWidenedBeyond   | OneWord  | Aligned    | 33.6863 ns | 0.1808 ns | 0.1691 ns |         - |
| **Compare**             | **OneWord**  | **Misaligned** |  **9.9533 ns** | **0.0388 ns** | **0.0363 ns** |         **-** |
| CompareReference    | OneWord  | Misaligned |  2.0855 ns | 0.0185 ns | 0.0173 ns |         - |
| Hash                | OneWord  | Misaligned | 10.5433 ns | 0.0787 ns | 0.0736 ns |         - |
| HashReference       | OneWord  | Misaligned |  0.7756 ns | 0.0227 ns | 0.0212 ns |         - |
| HashWidenedOne      | OneWord  | Misaligned | 15.0982 ns | 0.0382 ns | 0.0298 ns |         - |
| HashWidened         | OneWord  | Misaligned | 18.4149 ns | 0.0626 ns | 0.0555 ns |         - |
| HashWidenedNineteen | OneWord  | Misaligned | 20.8119 ns | 0.1366 ns | 0.1211 ns |         - |
| HashWidenedBeyond   | OneWord  | Misaligned | 34.7747 ns | 0.2046 ns | 0.1814 ns |         - |
| **Compare**             | **TwoWords** | **Aligned**    |  **5.4686 ns** | **0.0139 ns** | **0.0116 ns** |         **-** |
| CompareReference    | TwoWords | Aligned    |  1.5685 ns | 0.0084 ns | 0.0078 ns |         - |
| Hash                | TwoWords | Aligned    | 11.9215 ns | 0.1923 ns | 0.1606 ns |         - |
| HashReference       | TwoWords | Aligned    |  0.7785 ns | 0.0192 ns | 0.0179 ns |         - |
| HashWidenedOne      | TwoWords | Aligned    | 22.6579 ns | 0.1364 ns | 0.1276 ns |         - |
| HashWidened         | TwoWords | Aligned    | 23.7061 ns | 0.1637 ns | 0.1532 ns |         - |
| HashWidenedNineteen | TwoWords | Aligned    | 25.9202 ns | 0.1781 ns | 0.1666 ns |         - |
| HashWidenedBeyond   | TwoWords | Aligned    | 38.2070 ns | 0.2904 ns | 0.2716 ns |         - |
| **Compare**             | **TwoWords** | **Misaligned** | **15.3155 ns** | **0.1649 ns** | **0.1542 ns** |         **-** |
| CompareReference    | TwoWords | Misaligned |  2.0678 ns | 0.0171 ns | 0.0160 ns |         - |
| Hash                | TwoWords | Misaligned | 11.8122 ns | 0.0759 ns | 0.0710 ns |         - |
| HashReference       | TwoWords | Misaligned |  0.7844 ns | 0.0129 ns | 0.0121 ns |         - |
| HashWidenedOne      | TwoWords | Misaligned | 22.6199 ns | 0.1379 ns | 0.1290 ns |         - |
| HashWidened         | TwoWords | Misaligned | 23.7849 ns | 0.1424 ns | 0.1332 ns |         - |
| HashWidenedNineteen | TwoWords | Misaligned | 25.8383 ns | 0.1417 ns | 0.1326 ns |         - |
| HashWidenedBeyond   | TwoWords | Misaligned | 38.3939 ns | 0.1690 ns | 0.1580 ns |         - |

## Three- and four-word operands - no budget

| Method    | Shape      | Pairing    | Mean      | Error    | StdDev   | Allocated |
|---------- |----------- |----------- |----------:|---------:|---------:|----------:|
| **Add**       | **ThreeWords** | **Aligned**    |  **10.34 ns** | **0.030 ns** | **0.028 ns** |         **-** |
| Subtract  | ThreeWords | Aligned    |  12.20 ns | 0.049 ns | 0.046 ns |         - |
| Multiply  | ThreeWords | Aligned    |  13.33 ns | 0.062 ns | 0.058 ns |         - |
| Divide    | ThreeWords | Aligned    | 104.27 ns | 0.538 ns | 0.503 ns |         - |
| Remainder | ThreeWords | Aligned    |  27.19 ns | 0.120 ns | 0.106 ns |         - |
| **Add**       | **ThreeWords** | **Misaligned** |  **15.85 ns** | **0.057 ns** | **0.053 ns** |         **-** |
| Subtract  | ThreeWords | Misaligned |  15.43 ns | 0.094 ns | 0.079 ns |         - |
| Multiply  | ThreeWords | Misaligned |  13.48 ns | 0.057 ns | 0.050 ns |         - |
| Divide    | ThreeWords | Misaligned |  95.62 ns | 0.491 ns | 0.459 ns |         - |
| Remainder | ThreeWords | Misaligned |  26.12 ns | 0.136 ns | 0.127 ns |         - |
| **Add**       | **FourWords**  | **Aligned**    |  **10.51 ns** | **0.049 ns** | **0.046 ns** |         **-** |
| Subtract  | FourWords  | Aligned    |  12.58 ns | 0.031 ns | 0.029 ns |         - |
| Multiply  | FourWords  | Aligned    |  44.18 ns | 0.114 ns | 0.101 ns |         - |
| Divide    | FourWords  | Aligned    | 106.38 ns | 0.318 ns | 0.298 ns |         - |
| Remainder | FourWords  | Aligned    |  24.96 ns | 0.069 ns | 0.065 ns |         - |
| **Add**       | **FourWords**  | **Misaligned** |  **16.80 ns** | **0.128 ns** | **0.120 ns** |         **-** |
| Subtract  | FourWords  | Misaligned |  17.36 ns | 0.126 ns | 0.118 ns |         - |
| Multiply  | FourWords  | Misaligned |  14.66 ns | 0.076 ns | 0.071 ns |         - |
| Divide    | FourWords  | Misaligned |  90.04 ns | 0.334 ns | 0.296 ns |         - |
| Remainder | FourWords  | Misaligned |  27.71 ns | 0.103 ns | 0.096 ns |         - |

