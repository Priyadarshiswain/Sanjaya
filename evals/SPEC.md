# Sanjaya v0.1.1 benefit-evaluation specification

Status: proposed public contract. No model evaluation has run.

Target: the exact public `sanjaya-mcp@0.1.1` npm artifact.

Schema version: `1.0`.

## Evaluation question

For an AI coding agent investigating an unfamiliar local repository, does
adding Sanjaya improve evidence-grounded correctness or investigation
efficiency compared with the same agent using its normal local discovery
tools?

“Without Sanjaya” does not mean “without repository access.” A realistic
coding agent already has shell search, bounded file reads, and local Git
commands. Removing those capabilities would create an artificially weak
control and would not represent a user's installation decision.

## Experimental arms

### `native`

The agent has a read-only repository snapshot and a fixed set of ordinary local
tools:

- shell commands limited to the snapshot;
- exact text search such as `rg`;
- bounded file reads; and
- local Git inspection.

Network access is disabled during the measured task.

### `sanjaya_available`

The agent has everything in `native`, plus the exact public Sanjaya package
configured with the snapshot root. The task prompt does not require a Sanjaya
call. This arm measures the practical value of making the tools available,
including whether the agent discovers and selects them appropriately.

This is the treatment used in the headline comparison.

### `sanjaya_guided`

This optional diagnostic arm adds one short, frozen instruction explaining
that Sanjaya should be used when its reported capability fits the task.

Guided results must remain separate. They measure orchestration guidance, not
the benefit of the v0.1.1 package alone, and cannot replace the
`sanjaya_available` versus `native` headline.

## Preregistered hypotheses

### Primary

1. Sanjaya increases strict task success: all required claims are correct, no
   critical forbidden claim is present, and every required citation is valid.
2. Sanjaya reduces silent ambiguity errors and unsupported assertions on
   duplicate, absent, stale, malformed, and unsupported cases.

### Secondary

1. On successful paired runs, Sanjaya reduces agent turns, tool calls,
   repository source bytes read, or tool-response bytes.
2. On successful paired runs, Sanjaya reduces uncached input tokens, output
   tokens, or measured wall time.
3. Sanjaya improves recovery after a missing index, stale index, invalid path,
   unavailable capability, or other structured failure.

Cached input tokens are reported separately. Monetary cost is derived from a
separately timestamped price table and is never the only efficiency record.

### Claims this evaluation cannot establish

The v0.1.1 evaluation does not establish:

- compiler-semantic reference binding or call-graph correctness;
- TypeScript or JavaScript definitions, references, type checking, or source
  retrieval;
- semantic/vector search benefit;
- universal token savings;
- support outside the language capability exercised by a task; or
- improved patch quality from discovery-only questions.

## Evaluation layers

### 0. Installed-artifact contract

Use no language model. Exercise the exact npm artifact through MCP against a
controlled fixture. Verify initialization, approved tool discovery,
schema-valid bounded responses, capability honesty, repository containment,
and absence of absolute-path leakage.

### 1. Paired agent benefit

Run every frozen task in independent `native` and `sanjaya_available`
sessions. Hold the model, prompt, budgets, snapshot, machine allocation, and
scorer constant. This is the headline experiment.

### 2. Onboarding and project switching

Measure package acquisition, diagnostics, first useful non-indexed query,
index construction, warm restart, switching between two roots, and proof that
one process cannot consult another project's files or index.

### 3. Downstream coding outcome

Deferred. A later experiment may use execution-based tests to determine
whether discovery changes patch quality. Layer 1 results must not be presented
as Layer 3 evidence.

## Repository corpus and scale

The corpus combines a controlled synthetic fixture with public repositories
pinned to immutable commit SHAs. No private source, task, result, path, or
project history may enter the suite.

The synthetic fixture will be newly written for this repository. Its source is
tracked and reviewed. A setup step copies it into an isolated temporary Git
repository and creates deterministic history; nested `.git` data is never
tracked.

Public repositories are acquired before model execution. Their canonical
HTTPS URL, exact lowercase 40-character commit SHA, license, language counts,
file counts, and source bytes are recorded in the repository manifest. Model
runs have no network access. A moving branch, tag, or default-branch checkout
is not a valid snapshot.

Size bands use source-file count at the pinned snapshot:

| Band | Source files |
|---|---:|
| `controlled` | Synthetic fixture and deterministic scale variants |
| `small` | Fewer than 500 |
| `medium` | 500–4,999 |
| `large` | 5,000 or more |

Size bands are descriptive slices, not quality labels. The pilot must contain
at least one `large` public repository. The full study must contain at least
two `large` repositories with different language profiles, including C# and
TypeScript/JavaScript or a mixed-language project.

For every large repository, report:

- tracked files, source files, source bytes, and selected language counts;
- cold index duration, peak memory, and index bytes;
- warm startup and task latency;
- failures, truncation, exclusions, and partial states; and
- the measured number of successful warm tasks needed to repay indexing cost.

Synthetic scale variants may test traversal and hard limits, but they cannot
replace realistic public repositories in the headline result.

## Task design

The full suite targets approximately 30 human-reviewed tasks:

| Family | Target count | Purpose |
|---|---:|---|
| Repository orientation | 4 | Identify relevant files and major structure |
| Natural-language target location | 5 | Locate code without receiving its exact name |
| C# definition ambiguity | 4 | Resolve or report duplicate syntax declarations |
| C# references and exact source | 5 | Find syntax candidates and retrieve bounded source |
| Exact text and generic fallback | 3 | Exercise common cross-language discovery |
| TypeScript/JavaScript structure | 3 | Exercise structural support without semantic overclaim |
| Recent local changes | 2 | Recover bounded commit and working-tree evidence |
| Negative and recovery cases | 4 | Absence, unsupported operations, stale state, and containment |

Tasks do not name a preferred tool. Each task record contains a user-visible
question and hidden scoring ground truth. The runner supplies only the
question, required answer shape, and permitted repository snapshot to the
agent.

Each required claim has:

- a stable key;
- a human-readable description;
- one or more accepted normalized values;
- a deterministic match mode; and
- zero or more acceptable repository-relative evidence ranges.

Positive code claims require evidence. Absence and unsupported outcomes may
have no supporting source line; they are scored through the expected outcome,
explicit unknown/unsupported answer fields, and the sanitized search/tool
trajectory.

Ground truth is produced independently of Sanjaya using parser/compiler
inspection, deterministic repository scripts, and human review. Every
reported task requires a second review.

## Index-state design

Indexing cost is never blended into warm-query benefit:

- `none` tasks exercise immediate capabilities without an index;
- `warm` tasks receive an index built before the measured agent session; and
- `stale` tasks deliberately change a controlled snapshot and score detection
  and recovery.

Index construction is measured as a separate onboarding operation. A report
must state whether and when warm-task savings amortize that operation.

## Answer and scoring contract

All arms use the same answer schema. An answer contains:

- a concise response;
- keyed atomic claims;
- normalized claim values;
- repository-relative line evidence;
- explicit unknown or unsupported items; and
- confidence from zero to one.

Primary scores:

- strict task success;
- required-claim precision, recall, and F1; and
- citation validity.

Safety and honesty scores:

- critical and non-critical forbidden claims;
- unsupported assertion rate;
- silent ambiguity errors;
- correct abstention;
- recovery after tool/state error; and
- cross-root or absolute-path leakage, which must remain zero.

Efficiency records:

- turns and tool calls by tool family;
- repository source bytes read;
- tool-response bytes;
- uncached input, cached input, and output tokens;
- wall time; and
- index duration, peak memory, and disk bytes where applicable.

Efficiency comparisons are reported both across all attempts and across
successful paired runs. There is no opaque combined score.

Prefer deterministic scoring. If a claim cannot be scored mechanically, any
model judge must be calibrated against blinded human review, must not know the
experimental arm, and must publish its disagreement rate.

## Run controls

Hold constant within every paired comparison:

- exact model identifier and reasoning/effort setting;
- agent CLI and harness versions;
- system prompt, task prompt, and output schema;
- maximum turns, timeout, and token budget;
- repository commit and filesystem state;
- CPU and memory allocation;
- locale and relevant environment variables;
- disabled network access;
- clean non-persistent session; and
- scorer version.

Personal memories, project instructions, plugins, unrelated MCP servers,
fallback models, and unrecorded configuration are disabled.

Arm order is randomized within each task and repetition. All failures,
timeouts, invalid outputs, and zero-Sanjaya-call treatment runs are retained.
Scoring is blind to the arm.

## Staged budget

### Pilot

- 12 tasks covering the important families and including one large repository.
- `native` and `sanjaya_available`.
- Three independent repetitions per task and arm.
- 72 headline model runs.
- A small guided diagnostic subset only if treatment adoption is low.

The pilot validates the tasks, scorers, harness, and accounting. It cannot
support a broad, model-independent, or statistical-significance claim.

### Full v0.1.1 study

- Approximately 30 frozen tasks.
- Five repetitions per task and headline arm.
- Approximately 300 runs for the primary model.
- A stratified 10-task subset repeated three times with a second model family:
  60 additional runs.

Report paired task-level deltas, full distributions, bootstrap confidence
intervals, and paired binary success analysis. Practical effect size matters
more than a bare p-value.

## Freeze and publication policy

Before the first model run, freeze and timestamp:

- hypotheses;
- repository manifest and commits;
- tasks and ground truth;
- scoring and exclusion rules;
- run budget and controls; and
- analysis code.

After execution:

- publish every run, including failures and negative deltas;
- report by task family, language, size band, and index state;
- do not change tasks or scorers after viewing arm labels;
- version and rerun both arms if a scorer defect is discovered;
- sanitize usernames, absolute paths, credentials, and machine identifiers;
- avoid republishing unnecessary third-party source bodies; and
- identify the exact package, repository, model, agent, harness, task, scorer,
  and analysis versions.

Normal pull-request CI runs only schema, fixture, scorer, and harness checks.
Paid or credentialed model runs require a separately approved, fixed budget.
Merging evaluation infrastructure cannot publish results, contact a model,
submit registry metadata, create a release, or make a product claim.

## Planned public sequence

1. Specification, schemas, non-result examples, and offline contract checks.
2. Newly written controlled fixture and deterministic installed-MCP runner.
3. Pinned public-repository manifest and reviewed pilot tasks.
4. Frozen pilot protocol and analysis implementation.
5. Separately approved 72-run pilot.
6. Findings review and a decision on the full study.
7. Full tasks and cross-model replication if justified.
8. Honest public report.
9. Separately approved Official MCP Registry submission.

## Method references

- [MCP-Atlas](https://github.com/scaleapi/mcp-atlas) for claims-based task
  scoring and tool-trajectory diagnostics.
- [MCP-Universe](https://mcp-universe.github.io/) for execution-based MCP task
  evaluators.
- [RepoQA](https://arxiv.org/abs/2406.06025) for natural-language code-location
  tasks.
- [SWE-bench harness](https://www.swebench.com/SWE-bench/reference/harness/)
  for version-pinned, isolated, reproducible execution.
