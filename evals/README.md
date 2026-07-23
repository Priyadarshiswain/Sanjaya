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
identities, and exercise the exact public `sanjaya-mcp@0.1.1` artifact through
MCP. They do not contact a model, clone a public evaluation repository, publish
results, or submit anything to an external registry.
