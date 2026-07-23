# Controlled evaluation fixture

SignalDesk is a synthetic incident-routing and notification project written
from scratch for Sanjaya's evaluation suite. It contains no migrated or private
source.

The tracked [`controlled/`](controlled/) core contains C#, TypeScript,
JavaScript, JSON, YAML, and Markdown. It deliberately includes:

- production and legacy C# types with the same short name;
- interface and implementation declarations sharing a method name;
- overloaded methods and partial classes;
- frontend and backend retry calculations with different authority;
- comments, strings, and documentation that act as text-search decoys;
- deterministic commit history and working-tree evidence; and
- generated and ignored content used to test exclusions.

The builder creates three profiles:

| Profile | Source files | Purpose |
|---|---:|---|
| `core` | 25 | Human-reviewable correctness and MCP behavior |
| `medium` | 1,000 | Traversal and scaling diagnostics |
| `large` | 5,000 | The v0.1 structural-index file ceiling |

Generated scale sources are deliberately simple. They test traversal,
determinism, and hard limits; they are not substitutes for realistic pinned
public repositories in the headline agent evaluation.

[`controlled-contract.json`](controlled-contract.json) freezes each profile's
Git commit, file counts, final configuration state, and working-tree evidence.
The builder uses fixed Git identity, timestamps, branch, line endings, and
commit messages.

Prepare a disposable core fixture:

```bash
npm run prepare:core --prefix evals
```

The command prints the temporary root and frozen identity. The verification
command creates and removes its own temporary repositories. Remove the printed
temporary `cleanupRoot` after manually inspecting a prepared fixture.

```bash
npm run verify:fixture --prefix evals
```

Verification uses the exact public `sanjaya-mcp@0.1.2` package locked under the
isolated eval development project. It exercises the installed launcher through
MCP and checks all three profile identities. It runs no AI model, publishes no
result, and makes no external submission.
