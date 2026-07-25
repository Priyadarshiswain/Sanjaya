# Sanjaya evaluations

This directory defines a reproducible evaluation of whether adding Sanjaya
helps an AI coding agent investigate an unfamiliar local repository.

The headline comparison is intentionally demanding:

- the same agent with normal local shell, search, bounded-read, and Git tools;
- the identical setup with the exact public Sanjaya package also available.

The evaluation is a proposed experiment, not a claim that Sanjaya already
improves agent performance. No model results are published here yet.

Start with [SPEC.md](SPEC.md). Machine-readable contracts are under
[`schemas/`](schemas/), and schema-valid non-result examples are under
[`examples/`](examples/). The newly written SignalDesk project under
[`fixtures/`](fixtures/) provides controlled ground truth and deterministic
core, medium, and large scale profiles.

Validate the contract without running a model:

```bash
npm ci --prefix evals --ignore-scripts
npm run verify --prefix evals
npm run verify:fixture --prefix evals
```

These checks parse and compile every JSON Schema, validate the examples, prove
representative invalid records are rejected, reproduce all controlled fixture
identities, and exercise the exact public `sanjaya-mcp@0.1.2` artifact through
MCP. They do not contact a model, clone a public evaluation repository, publish
results, or submit anything to an external registry.

The frozen v0.1.2 pilot adds 12 tasks across the controlled fixture,
FastEndpoints, Vitest, Kiota, and Aspire. Inspect
[`protocol/pilot.json`](protocol/pilot.json),
[`repositories/manifest.json`](repositories/manifest.json), and
[`tasks/pilot.json`](tasks/pilot.json) before execution.

Acquire the pinned public snapshots into an explicit temporary directory, then
run the deterministic installed-artifact layer:

```bash
npm run acquire:pilot --prefix evals -- --output /tmp/sanjaya-pilot-corpus
npm run run:layer0 --prefix evals -- \
  --corpus-root /tmp/sanjaya-pilot-corpus
```

The paired model pilot is resumable and writes only structured run records and
content-minimized traces:

```bash
npm run verify:pilot --prefix evals
npm run test:scorer --prefix evals
npm run run:pilot --prefix evals -- \
  --corpus-root /tmp/sanjaya-pilot-corpus
```

Model transport uses the authenticated Codex service. Commands available to
the evaluated agent remain in a read-only, network-disabled sandbox. Raw Codex
events stay in temporary local storage and are deleted after sanitization.

The completed pilot exposed an exact-string scoring defect. The versioned,
additive [scorer v1.1 methodology](SCORER-V1.1.md) and its
[arm-hidden review fixtures](fixtures/scorer-v1.1/README.md) correct
deterministic formatting failures without silently guessing semantic
equivalence. Scorer 1.0 remains the frozen scorer for the published v0.1.2 run
records; no result is overwritten.

The additive
[scorer v1.1 reanalysis](results/v0.1.2/reanalysis-scorer-v1.1/REPORT.md)
applies both scorer versions to the same saved answers. It makes no model
calls, preserves the original reports and failures, and publishes per-record
derived scores for audit. Reproduce or verify it with:

```bash
npm run reanalyze:v1.1 --prefix evals
npm run verify:reanalysis --prefix evals
```

## Evidence-First skill study

The proposed
[`evidence_first_skill`](protocol/evidence-first-skill.json) study is separate
from the frozen availability and guided records. It installs the exact reviewed
three-file plugin into a fresh disposable `CODEX_HOME` for every treatment
session. The matching control also uses a fresh home, but has no plugin or MCP
server. Both arms receive the same task prompt; the prompt does not name the
skill, plugin, or Sanjaya.

The study uses all 12 frozen tasks, scorer 1.1.0, three repetitions, and a fresh
native control: 72 runs in total. Execution is deliberately staged. The first
stage runs one repetition in both arms (24 runs), after which routing,
correctness, citations, side effects, failures, tokens, and latency are
reviewed before the remaining 48 runs can be authorized.

Validate the study contract without installing anything persistently or
calling a model:

```bash
npm run verify:evidence-first-skill --prefix evals
npm run verify:evidence-first-skill-runtime --prefix evals
node --check evals/run-pilot.mjs
node --check evals/analyze-evidence-first-skill.mjs
```

After the contract is merged, the owner must separately approve the run count,
aggregate token ceiling, external monetary ceiling, and expected time window.
Only then may stage one be started:

```bash
npm run run:evidence-first-skill --prefix evals -- \
  --corpus-root /tmp/sanjaya-pilot-corpus \
  --repetitions 1 \
  --max-total-tokens <approved-ceiling>
```

Merging the evaluation contract does not authorize that command. A partial
stage is a harness and routing check, not evidence for a marketplace claim.
The aggregate ceiling includes cached, uncached, and output tokens already
recorded in this study. The harness stops between runs when it reaches the
ceiling; it cannot interrupt a model response already in progress.
It also stops after two consecutive session failures so an authentication or
runtime fault cannot consume the entire stage.

The first stage-one attempt stopped exactly this way after its initial native
and skill sessions. A clean `CODEX_HOME` had also hidden the existing ChatGPT
login. The frozen protocol records the amendment: both arms now receive only a
temporary, user-private symlink to the existing `auth.json`, while personal
configuration, rules, skills, plugins, memories, and sessions remain excluded.
The two failed records are retained unchanged.

The owner subsequently approved the remaining 48 sessions under a cumulative
7,000,000-token ceiling with no purchase of extra credits. The completed study
contains all 72 planned records and consumed 5,755,266 aggregate recorded
tokens. Reproduce its deterministic report and verify every record with:

```bash
npm run analyze:evidence-first-skill --prefix evals
npm run verify:evidence-first-skill-results --prefix evals
```

The result does not establish a correctness or efficiency benefit for the
current skill-enabled experience. See the checked-in report for the complete
arm, routing, side-effect, and task-level evidence.

A post-study additive scorer repair
([SCORER-V1.2.md](SCORER-V1.2.md)) rescored all 72 frozen records after an
arm-blind review showed three tasks' accepted phrases rejected correct
answers. Under scorer 1.2 both arms rise symmetrically (native 25→34,
skill 22→31 of 36) with zero lost successes; the comparative verdict is
unchanged. Reproduce or verify with:

```bash
npm run reanalyze:evidence-first-v1.2 --prefix evals
npm run verify:reanalysis-v1.2 --prefix evals
```
