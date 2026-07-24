# Evidence-First Code Discovery skill contract

Status: approved contract with a local skills-only plugin implementation; the
skill is not installed, marketplace-listed, or published. Its installation and
publication boundaries are defined separately in the
[distribution contract](skill-distribution.md).

This contract defines the portable skill that teaches an AI coding agent when
and how to use Sanjaya. Its canonical source lives under
`plugins/sanjaya/skills/evidence-first-code-discovery/`. It does not change the
Sanjaya MCP server, make Sanjaya mandatory, or claim that guided use is already
more accurate or efficient than native repository tools.

## Why this is separate from the MCP server

An MCP server makes tools available. A skill supplies a reusable decision
policy for choosing and combining those tools.

In the public `0.1.2` evaluation, none of the 35 completed
`sanjaya_available` sessions called Sanjaya. A short guided instruction caused
all 18 guided sessions to use it, but also increased median tool calls, wall
time, and input tokens. The skill must therefore solve two problems together:

1. make suitable Sanjaya capabilities discoverable to the agent; and
2. prevent indiscriminate or duplicative tool use.

The skill will be evaluated as its own named treatment. Its results must not be
presented as evidence for MCP availability alone.

## Implemented identity and trigger

The skill name is `evidence-first-code-discovery`.

The implemented `SKILL.md` metadata is:

```yaml
---
name: evidence-first-code-discovery
description: Discover and explain unfamiliar codebases with verifiable repository-relative evidence. Use when locating implementations, declarations, candidate references, structure, recent changes, or code evidence for an explanation, review, or planned edit; choose capability-fitting Sanjaya MCP tools when available and fall back to native exact search, file reading, and read-only Git when they are not.
---
```

This description makes the unfamiliar-codebase discovery triggers and native
fallback explicit without forcing Sanjaya into unrelated coding work.

The skill should not trigger merely because a user asks to edit a known file,
run an already identified command, or discuss code that is already present in
the conversation.

Representative trigger requests include:

- “Find the production method that calculates the retry delay and cite it.”
- “There are several classes with this name; identify the intended one.”
- “Where is this function used?”
- “Summarize the recent commits separately from uncommitted changes.”
- “Trace enough of this implementation to plan a safe change.”

## Required workflow

### 1. Frame the evidence need

Identify the claims the answer must support before searching. Prefer the
smallest discovery operation that can produce the required evidence.

Do not perform broad repository discovery when the user supplied the exact
file and line range or the necessary source is already in context.

### 2. Discover capabilities once

When Sanjaya is available and its capabilities are not already established for
the current repository session, call `capabilities` once when the client
exposes it.

If `capabilities` is not exposed, use the client’s live tool schemas or metadata
as the fallback capability boundary and do not invent missing tools. Reuse
capability information for later decisions in the same repository session. Do
not call `health_check` routinely. Use it only for setup questions or when a
capability reports a runtime or repository-readiness problem that needs
diagnosis.

Capability reporting is authoritative. Do not infer that a language supports
definitions, references, source retrieval, or call graphs merely because
another language does.

### 3. Choose one primary discovery route

Use the cheapest fitting route first:

| Evidence need | Preferred route | Boundary |
|---|---|---|
| Exact known text or identifier | `search_text` or native exact search | Choose one first; do not duplicate successful searches |
| Structure of a known file | `file_outline` | Use native reading when only a small known range is needed |
| Broad structural lookup across supported source | `search_code` | Use only with an existing current index |
| Exact C# declaration | `find_definition` | Preserve and report ambiguity |
| C# identifier usages | `find_references` | Treat every result as a syntax candidate, not a semantic binding |
| Exact C# declaration source | `get_source` | Use a chunk ID returned by indexed discovery |
| Recent local Git evidence | `recent_changes` | Keep committed and working-tree observations distinct |
| Unsupported or unavailable capability | Native search, file reading, or read-only Git | State the fallback when it affects confidence |

Do not call a Sanjaya tool and a native tool for the same successful lookup
solely to prove that both work. Use a second route only to resolve ambiguity,
recover from a bounded or unsupported result, or verify a claim whose
correctness materially depends on it.

When a Sanjaya result answers only part of the task, use a native fallback only
for the missing evidence. Do not re-query facts already established by the
successful result.

### 4. Keep indexing opt-in

`index_codebase` writes `.sanjaya/index-v1.json` in the selected repository.
The skill must not create or rebuild the index silently.

If indexed discovery is likely to repay its cost across a broad or repeated
task, explain the repository-local write and ask for approval before calling
`index_codebase`. For a one-off exact lookup, prefer immediate discovery or
native tools. A missing, stale, or incompatible index must never trigger an
automatic rebuild.

### 5. Inspect only enough source

Retrieve the smallest source region needed to support the answer. Do not load a
complete declaration when a bounded window is sufficient, and do not read
large files after an outline or search result already identifies the relevant
range.

### 6. Report grounded findings

Support material codebase claims with repository-relative paths and one-based
line ranges. Distinguish:

- observed evidence;
- inference derived from that evidence; and
- unsupported or unavailable information.

Never expose an absolute repository path. Preserve `partial`, ambiguity,
recovered-syntax, truncation, and `syntax_candidate` qualifications instead of
upgrading them into certainty.

For recent-change evidence, report only requested revisions, subjects, paths,
and working-tree state. Omit author names, email addresses, remote URLs, Git
configuration, commit bodies, and change statistics unless the user explicitly
requests them and they are relevant.

## Cost and stopping rules

Begin with one targeted discovery call. After three Sanjaya discovery calls,
reassess whether another call is necessary. Three is a review point, not a
hard limit: continue only when unresolved ambiguity or the user’s requested
depth justifies the added evidence.

Stop when every material claim has adequate evidence. Specifically:

- do not repeat `capabilities` within the same repository session;
- do not run `health_check` after successful discovery;
- do not search again with a broader tool after a precise result is adequate;
- do not enumerate all matches when a bounded unique answer is requested; and
- do not continue exploring unrelated architecture after answering the task.

## Failure and fallback contract

On `unsupported`, `unavailable`, missing-root, missing-index, stale-index,
bounded `partial`, or other stable failure:

1. preserve the reason and any usable evidence;
2. do not retry the same call unchanged;
3. select a native read-only fallback when it can answer the question; and
4. mention the limitation only when it affects the answer or setup.

The skill must still be useful when Sanjaya is absent. In that case it should
follow the same evidence-first workflow with native exact search, file reads,
and read-only Git operations. It must not instruct the agent to install
software, contact the network, or modify the repository unless the user
separately requests that action.

## Initial packaging boundary

The first implementation contains only:

```text
plugins/
└── sanjaya/
    ├── .codex-plugin/
    │   └── plugin.json
    └── skills/
        └── evidence-first-code-discovery/
            ├── SKILL.md
            └── agents/
                └── openai.yaml
```

No MCP configuration, script, hook, app, asset, reference bundle, model
dependency, telemetry, marketplace entry, or generated index belongs in the
initial plugin unless later testing demonstrates a concrete need and receives
separate approval. The body should remain concise and use capability names
rather than client-specific tool prefixes so the instructions can work across
MCP clients.

The skill should target Sanjaya `0.1.2` or newer while treating the live
`capabilities` response—not a version assumption—as the source of truth.
Installing the skill into a user environment and selecting a distribution
channel require separate owner review.

## Initial qualitative forward test

On 2026-07-24, six fresh read-only agents exercised three realistic tasks in
two independent passes: an exact CI lookup, a structural C# lookup, and a
recent-change plus working-tree question. Agents received the skill path and
task but not the intended answer or the author’s diagnosis.

All six answers were correct and used valid repository-relative evidence. The
first pass exposed two orchestration gaps:

- one client did not expose `capabilities`, although its live tool metadata was
  sufficient to select supported operations; and
- a recent-change answer included an unrequested author name and change
  statistics, while native Git partially duplicated successful Sanjaya
  evidence.

The skill was tightened to accept live tool schemas as a capability fallback,
use native tools only for missing evidence, and omit unrequested Git identity
and metadata. In the second pass:

- the exact lookup selected native exact search without duplicating it through
  MCP;
- the structural lookup remained read-only and created no index; and
- the recent-change lookup used Sanjaya for commit evidence, native Git only
  for missing working-tree state, and omitted author identity and statistics.

No forward-test agent modified the repository, created an index, installed the
skill, or published anything. This small qualitative check validates workflow
comprehension only; it is not a preregistered benchmark or evidence of product
benefit.

## Evaluation contract

Implementation approval does not authorize a paid model run or publication.
The skill requires a new, separately named evaluation treatment such as
`evidence_first_skill`; it must not replace or rewrite the frozen `native`,
`sanjaya_available`, or `sanjaya_guided` records.

The forward evaluation should:

- use the same model, effort, repository commits, task text, and native tools
  across paired arms;
- include both tasks where structural discovery can plausibly repay its cost
  and simple lookups where native search should remain preferable;
- record Sanjaya and native tool calls, index writes, strict success, claim
  accuracy, citation validity, tokens, response size, turns, and latency;
- verify that the skill sometimes chooses native tools and sometimes chooses
  Sanjaya rather than maximizing MCP adoption;
- retain failed, neutral, and negative runs; and
- publish no broad benefit claim from a small diagnostic.

Forward-testing should use fresh agents that receive the skill and task without
the intended answer or the author’s diagnosis. Any externally billed run needs
an explicit model, effort, run count, token cap, and cost ceiling approved by
the owner.

## Implementation status and remaining gates

The owner approved the following contract in pull request 30:

1. the proposed name and trigger boundary;
2. the capability-selection table;
3. the opt-in indexing rule;
4. the three-call reassessment rule;
5. the evidence and fallback contract;
6. the initial two-file packaging boundary; and
7. the separate evaluation treatment.

The canonical skill is now packaged in a minimal local plugin and must remain
validated against this contract. The initial qualitative forward test is
complete. A separate `evidence_first_skill` protocol now proposes a fresh
native control and skill arm across the 12 frozen tasks, beginning with a
24-run review stage. Merging that protocol does not authorize model calls:
run count, aggregate token ceiling, external monetary ceiling, and expected
time window require separate owner approval. Marketplace creation and
publication remain blocked pending the reviewed evaluation outcome and the
separate distribution gates. Installation, marketplace creation, model
evaluation, and publication remain later explicit decisions.
