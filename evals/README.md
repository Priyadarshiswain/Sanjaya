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

The completed pilot exposed an exact-string scoring defect. The proposed,
additive [scorer v1.1 methodology](SCORER-V1.1.md) and its
[arm-hidden review fixtures](fixtures/scorer-v1.1/README.md) correct
deterministic formatting failures without silently guessing semantic
equivalence. Scorer 1.0 remains the frozen scorer for the published v0.1.2 run
records; no result is overwritten.
