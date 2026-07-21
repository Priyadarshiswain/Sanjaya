# Capability contract

Sanjaya reports runtime availability explicitly. Stable public responses use
lowercase string values so clients do not need to infer availability from tool
failures.

- `supported` means the tool is ready in the current runtime.
- `unavailable` means the tool or provider cannot currently be used. The stable
  reason `not_implemented` distinguishes approved roadmap work that has not
  shipped yet.

## Current development runtime

Only `capabilities` and `health_check` are registered as MCP tools. All eight
discovery tools and the C#, TypeScript/JavaScript, and generic providers report
`unavailable` with reason `not_implemented`. The server uses stdio and performs
no network access by default.

## Planned v0.1 matrix

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

Repository-root inputs and canonical path containment are planned but are not
implemented by the protocol foundation.

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
