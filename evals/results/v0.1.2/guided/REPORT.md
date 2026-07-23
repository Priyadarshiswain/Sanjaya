# Sanjaya v0.1.2 guided diagnostic

Status: completed diagnostic; separate from the headline availability result.

## Outcome

All 18 guided sessions used Sanjaya, averaging
5.333 Sanjaya calls per run. The
instruction therefore corrected the zero-uptake problem observed in the
headline pilot.

Active Sanjaya use did not demonstrate an accuracy or efficiency advantage in
this six-task diagnostic. The preregistered mechanical strict score was
6/18 for the matching
native records and 3/18
for guided runs. Because scorer 1.0.0 under-credits explanatory but correct
claim values, these counts are not reliable absolute accuracy estimates.

Citation validity increased from
0.876 to
0.927 on average, while
median wall time increased from 18151 ms to
26388 ms and median input tokens increased from
46032 to 106357.
This is diagnostic evidence that the current guided orchestration adds
discovery overhead; it is not evidence of universal regression or benefit.

## Comparison

| Measure | Matching native | Sanjaya guided |
|---|---:|---:|
| Planned records | 18 | 18 |
| Completed | 17 | 18 |
| Runs using Sanjaya | 0 | 18 |
| Mean Sanjaya calls | 0.000 | 5.333 |
| Mean claim F1 | 0.480 | 0.384 |
| Mean citation validity | 0.876 | 0.927 |
| Median total tool calls | 2 | 5 |
| Median wall time | 18151 ms | 26388 ms |
| Median input tokens | 46032 | 106357 |
| Median output tokens | 650 | 800 |

There were 17 completed same-task, same-repetition
pairs. Their mean claim-F1 delta was
-0.074 and their mean citation-validity
delta was +0.046.

## Task-level strict results

| Task | Native | Guided | Guided Sanjaya calls |
|---|---:|---:|---:|
| SJ-EVAL-0001 | 3/3 | 3/3 | 15 |
| SJ-EVAL-0002 | 3/3 | 0/3 | 17 |
| SJ-EVAL-0003 | 0/3 | 0/3 | 10 |
| SJ-EVAL-0005 | 0/3 | 0/3 | 30 |
| SJ-EVAL-0008 | 0/3 | 0/3 | 11 |
| SJ-EVAL-0009 | 0/3 | 0/3 | 13 |

## Interpretation

The product-level lesson is an orchestration problem, not a basis for a
marketing claim:

- availability alone produced no tool adoption;
- a short instruction produced consistent adoption;
- current guided use consumed more context and time on these tasks; and
- evidence citations improved modestly, while the frozen claim scorer needs a
  versioned redesign before any broader accuracy study.

The next evaluation should first repair and independently review the answer
normalization/scoring contract, then compare targeted Sanjaya strategies
against native discovery on tasks where indexing can plausibly repay its cost.
