# Sanjaya v0.1.2 pilot result

Status: completed pilot; exploratory, not a broad product claim.

## Outcome

This pilot does **not** demonstrate a benefit from merely making Sanjaya
available to GPT-5.6-Terra through Codex CLI. None of the
35 completed treatment sessions called a Sanjaya tool.
The agent used its ordinary shell/search tools in both arms, so the experiment
measured zero treatment uptake rather than the effect of active Sanjaya use.

The preregistered strict score was 6/36 in the native
arm and 6/36 in the availability arm. Across
33 completed pairs there were
0 treatment wins,
0 native wins, and
33 ties. No causal performance or efficiency benefit
can be attributed to Sanjaya from this headline comparison.

## Run accounting

- Planned model records: 72
- Completed sessions: 69
- Retained harness failures: 3
- Model: gpt-5.6-terra, effort medium
- Agent: codex-cli 0.144.5
- Treatment sessions with a Sanjaya call: 0

The three failures occurred before a usable model answer: one outer harness
permission error and two structured-output schema compatibility errors. They
remain in the planned denominator.

## Mechanical scores and efficiency

| Measure | Native | Sanjaya available |
|---|---:|---:|
| Strict success / planned | 6/36 | 6/36 |
| Mean claim F1 / completed | 0.255 | 0.326 |
| Mean citation validity / completed | 0.780 | 0.800 |
| Median tool calls / completed | 2 | 2 |
| Median wall time | 20809 ms | 21562 ms |
| Median input tokens | 47292.5 | 47436 |
| Median output tokens | 755 | 698 |

The source-byte metric is a conservative tool-output-byte proxy because Codex
CLI does not expose actual filesystem bytes read. It must not be described as
precise disk I/O.

## Task-level strict results

| Task | Native | Available | Treatment used Sanjaya |
|---|---:|---:|---:|
| SJ-EVAL-0001 | 3/3 | 3/3 | 0/3 |
| SJ-EVAL-0002 | 3/3 | 3/3 | 0/3 |
| SJ-EVAL-0003 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0004 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0005 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0006 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0007 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0008 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0009 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0010 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0011 | 0/3 | 0/3 | 0/3 |
| SJ-EVAL-0012 | 0/3 | 0/3 | 0/3 |

## Installed-artifact layer

| Repository | Index status | Files | Chunks | Error |
|---|---|---:|---:|---|
| signaldesk-core | ok | 25 | 84 | — |
| fastendpoints | partial | 884 | 9702 | — |
| vitest | partial | 2307 | 11744 | — |
| kiota | partial | 768 | 7350 | — |
| aspire | error | — | — | index_source_unreadable |

The controlled fixture indexed cleanly. FastEndpoints, Vitest, and Kiota
produced ready indexes with explicit partial warnings. Aspire's full index
failed with `index_source_unreadable` because a supported source file exceeds
the v0.1.2 one-mebibyte file limit; this negative result is retained.

## Important scoring limitation

Scorer 1.0.0 required exact canonical claim values, while the generation prompt
required the keys but did not tell the agent to emit terse canonical values.
Many substantively correct answers therefore received zero claim credit after
adding explanatory text or Markdown formatting. Citation validity remained
separately measurable. The preregistered scores are preserved, but they should
not be interpreted as absolute answer accuracy.

Any corrected scorer must receive a new version and be applied to both arms.
It cannot silently replace these results. The preregistered contingency is a
separate guided diagnostic, explicitly reported outside the headline
comparison, to test whether Sanjaya helps when orchestration actually selects
it.
