# Evidence-First Code Discovery skill evaluation

Status: full study complete. This is a separate treatment
and does not alter the frozen v0.1.2 availability or guided records.

## What this study tests

The same gpt-5.6-terra agent receives the same task, repository snapshot,
native tools, scorer, and limits in both arms. The treatment adds the exact
installed `evidence-first-code-discovery` skill and the Sanjaya MCP server. It therefore
measures the skill-enabled Sanjaya experience against native discovery; it
does not isolate the skill instructions from MCP availability. The task prompt
does not name the skill or tell the agent to use Sanjaya.

## Current outcome

72/72 planned records are present.
There are 34 completed same-task,
same-repetition pairs. The skill arm has
1 strict
win,
4 strict losses, and
29 ties.

Selective routing is measured rather than assuming maximum MCP usage:
31 completed skill sessions used native
tools without Sanjaya, and 4 used at
least one Sanjaya tool (16 calls total).
2 Sanjaya-using sessions achieved
strict success. Measured index writes: 0.

The stage consumed 5755266 aggregate recorded tokens:
1144371 uncached input,
4546304 cached input, and
64591 output.

## Verdict

The current skill-enabled Sanjaya experience did not demonstrate a correctness
or efficiency benefit. Strict success and mean claim F1 were lower than the
fresh native control. Mean citation validity was slightly higher, but median
tool calls, wall time, input tokens, and output tokens were also higher. This
result does not support a marketplace benefit claim.

## Comparison

| Measure | Fresh native | Evidence-first skill |
|---|---:|---:|
| Recorded | 36 | 36 |
| Completed | 35 | 35 |
| Strict success | 25 | 22 |
| Mean claim F1 | 0.893 | 0.867 |
| Mean citation validity | 0.787 | 0.801 |
| Median tool calls | 2 | 3 |
| Median wall time | 22605 ms | 30629 ms |
| Median input tokens | 46541 | 55827 |
| Median output tokens | 703 | 963 |

Paired mean claim-F1 delta (skill minus native):
-0.027. Paired mean citation
delta: +0.014.

## Active Sanjaya pairs

| Task | Repetition | Native strict | Skill strict | Sanjaya calls | Native tokens | Skill tokens |
|---|---:|---:|---:|---:|---:|---:|
| SJ-EVAL-0007 | 3 | false | false | 3 | 43752 | 154425 |
| SJ-EVAL-0001 | 3 | true | true | 5 | 34600 | 153035 |
| SJ-EVAL-0009 | 2 | true | true | 4 | 53972 | 145409 |
| SJ-EVAL-0005 | 1 | true | false | 4 | 48506 | 135315 |

## Task-level routing and strict results

| Task | Native strict | Skill strict | Skill runs using Sanjaya |
|---|---:|---:|---:|
| SJ-EVAL-0001 | 2 | 3 | 1/3 |
| SJ-EVAL-0002 | 3 | 3 | 0/3 |
| SJ-EVAL-0003 | 3 | 2 | 0/3 |
| SJ-EVAL-0004 | 3 | 2 | 0/3 |
| SJ-EVAL-0005 | 3 | 2 | 1/3 |
| SJ-EVAL-0006 | 3 | 3 | 0/3 |
| SJ-EVAL-0007 | 0 | 0 | 1/3 |
| SJ-EVAL-0008 | 0 | 0 | 0/3 |
| SJ-EVAL-0009 | 3 | 2 | 1/3 |
| SJ-EVAL-0010 | 0 | 0 | 0/3 |
| SJ-EVAL-0011 | 2 | 2 | 0/3 |
| SJ-EVAL-0012 | 3 | 3 | 0/3 |

## Interpretation boundary

Failures, neutral results, regressions, and index writes remain visible.
The completed study supports no broader or model-independent claim.
Even a completed 72-run study supports conclusions only for the pinned agent,
model, repositories, tasks, MCP package, and skill version recorded here.
