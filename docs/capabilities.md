# Capability contract

Sanjaya reports capabilities explicitly. A provider may report a capability as
`Supported`, `Unavailable`, `Unsupported`, or `Experimental`.

- **Supported** means the capability is ready in the current runtime.
- **Unavailable** means it is implemented but a runtime dependency is missing.
- **Unsupported** means the provider does not implement it.
- **Experimental** means it is opt-in and not part of the stable v0.1 contract.

## Proposed v0.1 matrix

| Capability | C# | TypeScript/JavaScript | Other readable files |
|---|---|---|---|
| File outline | Roslyn structure | TypeScript AST structure | Generic metadata and preview |
| Exact text search | Supported | Supported | Supported |
| Structural indexing | Roslyn chunks | TypeScript AST chunks | Unsupported |
| Definitions | Syntax-based | Unsupported | Unsupported |
| References | Syntax-based | Unsupported | Unsupported |
| Symbol source retrieval | Supported | Unsupported | Unsupported |
| Call graph | Experimental | Unsupported | Unsupported |
| Vector search | Experimental | Experimental | Unsupported |

## Public tool names

| Tool | Purpose | Default side effect |
|---|---|---|
| `capabilities` | Report provider capabilities and availability | None |
| `health_check` | Diagnose local runtime readiness | None |
| `file_outline` | Summarize a file structurally or generically | None |
| `search_text` | Find exact text evidence | None |
| `recent_changes` | Read bounded local Git history | None |
| `index_codebase` | Rebuild the deterministic structural index | Writes `.sanjaya/` |
| `search_code` | Search structural chunks lexically | None |
| `find_definition` | Locate C# syntax definitions | None |
| `find_references` | Locate C# syntax references | None |
| `get_source` | Retrieve one C# symbol's source | None |

Every repository tool will accept an optional `repoRoot` and enforce that the
resolved canonical path belongs to the configured repository or its worktrees.

## Response envelope

Public tools return structured MCP content conforming to an output schema and
a compact text fallback. The structured envelope contains:

- `schemaVersion`
- `status`: `ok`, `partial`, or `error`
- `capability`
- `provider`
- `data`
- `evidence`
- `warnings`
- `error`

Evidence paths are repository-relative. Unsupported operations, missing
dependencies, missing or stale indexes, ambiguity, invalid paths, cancellation,
and unexpected failures use distinct stable error codes.

