# Capability contract

Sanjaya reports runtime availability explicitly. Stable public responses use
lowercase string values so clients do not need to infer availability from tool
failures.

- `supported` means the tool is ready in the current runtime.
- `unavailable` means the tool or provider cannot currently be used. The stable
  reason `not_implemented` distinguishes approved roadmap work that has not
  shipped yet.
- `repository_root_required` means an implemented discovery tool needs the
  process to be restarted with a valid explicit fully qualified
  `--root <path>`.

## Current development runtime

`capabilities`, `health_check`, `file_outline`, and `search_text` are registered
as MCP tools. The two discovery tools and generic text provider report
`supported` only when the process has a valid root. The C# and
TypeScript/JavaScript providers and the six deferred tools remain unavailable
with `not_implemented`. The server uses stdio and performs no network access by
default.

## Immediate discovery behavior

`search_text` performs ordinal exact matching for a single-line query; CR, LF,
and NUL characters are rejected. Matching is case-sensitive by default;
clients can request ordinal case-insensitive matching. Results are
ordered deterministically and include repository-relative `/`-separated path,
one-based line and column, and a bounded snippet. It searches readable UTF-8
text directly without an index.

The fixed search bounds are:

- single-line query: 256 characters; CR, LF, and NUL are not accepted
- results: 50 by default, 200 maximum
- candidate files: 10,000
- visited directories: 2,000
- filesystem entries: 20,000
- total readable bytes: 8 MiB
- individual file: 1 MiB
- searched line: 16,384 characters
- matches: 20 per line and 50 per file
- snippet: 320 characters (always enough for the maximum query)

Search skips file and directory symlinks, binary/non-UTF-8 files, files over
the individual limit, recognized generated files, and `.git`, `.sanjaya`,
common build/package outputs, and dependency directories. Bounded warning codes
describe skipped categories. Reaching a limit, encountering inaccessible
content, or cancellation produces a `partial` response with the evidence found
so far. Documented exclusions, generated files, symlinks, and binary files may
produce aggregate warnings but do not by themselves make an otherwise complete
search partial. Cancellation before any match also includes the stable
`cancelled` error.

`file_outline` is deliberately generic. It accepts one repository-relative
regular UTF-8 file up to 1 MiB, reports byte and line counts, and returns at
most 20 preview lines of 240 characters each. It rejects absolute, traversal,
directory, symlink, binary, oversized, missing, and inaccessible inputs with
stable errors. It does not claim C# or TypeScript/JavaScript structure.

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

One immutable repository scope is created per process. Missing or invalid root
configuration does not prevent MCP initialization, `capabilities`, or
`health_check`. Discovery accepts repository-relative paths only; containment
uses canonical existing paths and rejects traversal, absolute-path injection,
prefix collisions, and external symlink targets. No public result contains the
absolute repository path.

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
