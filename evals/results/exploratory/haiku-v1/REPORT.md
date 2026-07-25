# Exploratory: claude-haiku-4-5 native vs Sanjaya-guided

Status: exploratory and unregistered. One repetition, no preregistered
protocol, no frozen order seed, and a different agent scaffold from the
v0.1.2 studies. Nothing here is preregistered evidence or a benefit claim;
it exists to decide whether a preregistered small-model study is worth
designing.

## Design

The 12 frozen v0.1.2 tasks, pinned repository snapshots, and warm-index
preparation are reused unchanged. The agent is Claude Code 2.1.220 headless
(`claude -p`) running `claude-haiku-4-5`, restricted to read-only tools
(Read, Grep, Glob, LS; no Bash, no write tools, no network tools). The
treatment arm adds the exact `sanjaya-mcp@0.1.2` server over MCP plus the
frozen guided instruction from `protocol/guided.json`. Output-schema
enforcement is unavailable in this scaffold; answers are validated after the
fact and format failures are recorded as `invalid_output`. Scoring uses
scorer 1.2.0. Runner: [`run-claude-haiku.mjs`](../../../run-claude-haiku.mjs).

## Results (24 runs, 1 repetition)

| Measure | Native | Sanjaya guided |
|---|---:|---:|
| Strict success / planned | 6/12 | 6/12 |
| Invalid output | 1 | 2 |
| Mean claim F1 / completed | 0.811 | 0.925 |
| Sessions using Sanjaya | 0/12 | 8/12 (27 calls) |
| Total recorded cost | $0.67 | $0.84 |
| Median wall time | 24 s | 27 s |

Pairwise strict outcome: 1 native-favoring task (SJ-EVAL-0011),
1 Sanjaya-favoring task (SJ-EVAL-0006), 10 ties. SJ-EVAL-0008 produced
invalid output in both arms; SJ-EVAL-0007 produced invalid output in the
Sanjaya arm without calling Sanjaya. One Sanjaya session (SJ-EVAL-0005)
made 10 Sanjaya calls and still failed strictly.

## Honest reading

1. The small model is far below the frontier reference on the same tasks
   (6/12 versus the frontier native 34/36 under the same scorer), so there
   is a real capability gap for scaffolding to address — which was not true
   in the frontier studies.
2. Routing is not the obstacle it was for the frontier agent: with one
   guided sentence, 8 of 12 sessions used Sanjaya voluntarily.
3. Sanjaya improved claim accuracy (mean claim F1 +0.114) and flipped one
   task to strict success, but strict success tied 6–6 at +25% cost. The
   gains were consumed by exactly the weaknesses a small model has anyway:
   schema-valid output formatting and exact line-range citation precision.
4. This directionally favors delivering structure as injected context
   (a deterministic map slice) rather than as tools the small model must
   orchestrate; that hypothesis is untested here.

## Interpretation boundary

One repetition per cell cannot separate signal from run-to-run variance.
The agent scaffold differs from the frozen v0.1.2 studies, so no cross-study
pairing is valid; the frontier numbers above are context, not a comparison
arm. Any decision-bearing claim requires a preregistered protocol with
repetitions, an order seed, and declared ceilings, per this directory's
standing rules.
