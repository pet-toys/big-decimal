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

| Run | Date       | Scope                       | Code       | Cost   |
| --- | ---------- | --------------------------- | ---------- | ------ |
| A   | 2026-09-08 | `--anyCategories budget`    | `53d5b6a`  | 66 min |
| B   | 2026-09-08 | `*ComparisonBenchmarks*`    | `c33486b`  | 16 min |

`53d5b6a` is the `formatting-parity` branch, which differs from `c33486b` only
in the formatting path; the rows below that are not formatting were measured on
code identical to it.

## Verdicts

| Criterion                          | Budget | Ratio | Dispersion | Shape                 | Verdict | Run |
| ---------------------------------- | -----: | ----: | ---------: | --------------------- | ------- | --- |
| `Add`                              |   3.5x |  3.00 |       0.06 | one word, aligned     | met     | A   |
| `Subtract`                         |   3.5x |  2.57 |       0.05 | two words, misaligned | met     | A   |
| `Multiply`                         |   3.5x |  3.03 |       0.03 | one word, misaligned  | met     | A   |
| `Divide`                           |    10x |  5.52 |       0.08 | two words, aligned    | met     | A   |
| `Remainder`                        |    10x |  3.02 |       0.04 | one word, aligned     | met     | A   |
| `Parse`, `char`                    |     3x |  1.16 |       0.00 | one word              | met     | A   |
| `Parse`, UTF-8                     |     3x |  1.26 |       0.00 | one word              | met     | A   |
| `TryParse`, `char`                 |     3x |  1.15 |       0.00 | one word              | met     | A   |
| `TryParse`, UTF-8                  |     3x |  1.32 |       0.00 | one word              | met     | A   |
| Exact division against inexact     |    1.0 |  0.35 |       0.00 | `100 / 10` vs `/ 3`   | met     | A   |
| Hashing, widened against narrow    |   2.5x |  2.13 |       0.05 | two words, misaligned | met     | B   |
| Hashing, nineteen zeros against one|   1.5x |  1.43 |       0.02 | one word, aligned     | met     | B   |
| Zero allocations                   | always |     - |          - | every row             | met     | A   |

`TryFormat` is measured but not yet on `dev`:

| Criterion                          | Budget | Ratio | Dispersion | Shape                    | Verdict | Run |
| ---------------------------------- | -----: | ----: | ---------: | ------------------------ | ------- | --- |
| `TryFormat`, `char`                |     3x |  2.67 |       0.02 | two words, `#,##0.00`    | met     | A   |
| `TryFormat`, UTF-8                 |     3x |  2.80 |       0.03 | two words, `#,##0.00`    | met     | A   |

Those two rows measure the `formatting-parity` branch, which is where the nine
format strings the criterion is now read over come from. On `dev` the criterion
is read over three format strings and its last verdict predates this file's
format. The rows move into the table above when that branch lands.

## What the numbers do not say

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
