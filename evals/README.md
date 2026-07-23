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
[`examples/`](examples/).

Validate the contract without running a model:

```bash
npm ci --prefix evals --ignore-scripts
npm run verify --prefix evals
```

This check parses and compiles every JSON Schema, validates the examples, and
proves representative invalid records are rejected. It does not contact a
model, start Sanjaya, clone a repository, publish results, or submit anything
to an external registry.
