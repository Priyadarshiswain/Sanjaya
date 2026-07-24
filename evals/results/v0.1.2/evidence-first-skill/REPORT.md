# Evidence-First Code Discovery skill evaluation

Status: partial stage. This is a separate treatment
and does not alter the frozen v0.1.2 availability or guided records.

## What this study tests

The same gpt-5.6-terra agent receives the same task, repository snapshot,
native tools, scorer, and limits in both arms. The treatment adds the exact
installed `evidence-first-code-discovery` skill and the Sanjaya MCP server. It therefore
measures the skill-enabled Sanjaya experience against native discovery; it
does not isolate the skill instructions from MCP availability. The task prompt
does not name the skill or tell the agent to use Sanjaya.

## Current outcome

24/72 planned records are present.
There are 10 completed same-task,
same-repetition pairs. The skill arm has
1 strict wins,
1 strict losses, and
8 ties.

Selective routing is measured rather than assuming maximum MCP usage:
10 completed skill sessions used native
tools without Sanjaya, and 1 used at
least one Sanjaya tool (4 calls total).
0 Sanjaya-using sessions achieved
strict success. Measured index writes: 0.

The stage consumed 1937586 aggregate recorded tokens:
407831 uncached input,
1509376 cached input, and
20379 output.

## Comparison

| Measure | Fresh native | Evidence-first skill |
|---|---:|---:|
| Recorded | 12 | 12 |
| Completed | 11 | 11 |
| Strict success | 7 | 7 |
| Mean claim F1 | 0.871 | 0.879 |
| Mean citation validity | 0.767 | 0.814 |
| Median tool calls | 2 | 3 |
| Median wall time | 22605 ms | 30629 ms |
| Median input tokens | 46541 | 55827 |
| Median output tokens | 748 | 919 |

Paired mean claim-F1 delta (skill minus native):
+0.008. Paired mean citation
delta: +0.052.

## Task-level routing and strict results

| Task | Native strict | Skill strict | Skill runs using Sanjaya |
|---|---:|---:|---:|
| SJ-EVAL-0001 | 0 | 1 | 0/1 |
| SJ-EVAL-0002 | 1 | 1 | 0/1 |
| SJ-EVAL-0003 | 1 | 0 | 0/1 |
| SJ-EVAL-0004 | 1 | 1 | 0/1 |
| SJ-EVAL-0005 | 1 | 0 | 1/1 |
| SJ-EVAL-0006 | 1 | 1 | 0/1 |
| SJ-EVAL-0007 | 0 | 0 | 0/1 |
| SJ-EVAL-0008 | 0 | 0 | 0/1 |
| SJ-EVAL-0009 | 1 | 1 | 0/1 |
| SJ-EVAL-0010 | 0 | 0 | 0/1 |
| SJ-EVAL-0011 | 0 | 1 | 0/1 |
| SJ-EVAL-0012 | 1 | 1 | 0/1 |

## Interpretation boundary

Failures, neutral results, regressions, and index writes remain visible. A
partial stage is a harness and routing check, not a marketplace claim. Even a
completed 72-run study supports conclusions only for the pinned agent, model,
repositories, tasks, MCP package, and skill version recorded here.
