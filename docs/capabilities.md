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
- `not_git_repository` means local Git evidence is implemented but the selected
  root does not contain top-level Git worktree metadata.
- `index_missing` means indexed search is implemented but `index_codebase` has
  not created its repository-local index.
- `index_invalid` means the expected index path is not a bounded regular local
  file; invoking `search_code` provides the more specific safe failure.

## Current development runtime

`capabilities`, `health_check`, `file_outline`, `search_text`, `recent_changes`,
`index_codebase`, and `search_code` are registered as MCP tools. Generic discovery and the C#
syntax provider report `supported` only when the process has a valid root. C#
currently supports file outlines and structural indexing;
definitions, references, source retrieval, and call graph remain unavailable.
The TypeScript/JavaScript provider and all three deferred tools remain
unavailable with `not_implemented`. The server uses stdio and performs no
network access by default.

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

`file_outline` accepts one repository-relative regular UTF-8 file up to 1 MiB.
For `.cs` files, the `csharp-roslyn-syntax` provider returns a deterministic
flat outline of namespaces, types, delegates, methods, constructors,
properties, indexers, and operators. Each item contains a kind, name, bounded
display signature, optional container, and one-based line range. Responses are
limited to 500 items and 240 display characters per item. Syntax errors return
bounded recovered structure as a `partial` response with a diagnostic count;
Sanjaya does not build the project or claim semantic compilation.

Other readable files use `generic-text`: byte and line counts plus at most 20
preview lines of 240 characters each. TypeScript and JavaScript continue to use
this fallback until their AST provider ships. Both modes reject absolute,
traversal, directory, symlink, binary, oversized, missing, and inaccessible
inputs with stable errors.

The C# provider also implements structural chunks for the local index. It
produces at most 500 deterministic chunks per file and bounds each chunk to 64
KiB.

## Deterministic structural index

`index_codebase` explicitly rebuilds `.sanjaya/index-v1.json`. It indexes only
files claimed by an active structural provider, currently C#, and never edits
source files, Git metadata, or the consumer repository's `.gitignore`. A
warning is returned unless the root `.gitignore` explicitly contains a direct
`.sanjaya` rule.

The v0.1 bounds are 5,000 eligible files, 64 MiB readable source, 50,000
chunks, 2,000 directories, 20,000 filesystem entries, and a 64 MiB serialized
index. Unsupported files and documented exclusions are counted or skipped.
Inaccessible eligible source and hard-limit breaches fail the rebuild; no
knowingly incomplete index is promoted. Recovered syntax diagnostics and
individually bounded chunk content produce an explicit `partial` response.

The index is canonical UTF-8 JSON with stable ordinal ordering and SHA-256
content fingerprints and chunk identifiers. It includes format, producer, and
provider-contract versions but no timestamp, username, hostname, absolute
path, or Git remote. Identical source and versions produce identical bytes.

An exclusive local lock prevents concurrent writers. The full payload is
built and validated before a same-directory temporary file is flushed and
atomically promoted. Cancellation or failure removes the owned temporary file
and preserves the previous recognized index. Unknown targets, symlinked
storage, and path conflicts are never overwritten. Rebuild responses classify
the previous recognized index as `missing`, `current`, `stale`, or
`incompatible` using provider contracts and eligible-source fingerprints.

`search_code` is read-only and requires that index to be present, canonical,
compatible with the active provider contracts, and current for the exact set
of eligible source files. It re-hashes bounded eligible source on each request
in v0.1 so stale results are refused rather than silently returned. Distinct
whitespace-separated query terms are combined with AND semantics across chunk
name, container, kind, path, and bounded content. Matching is ordinal and
case-insensitive by default, with an explicit case-sensitive option.

Each term contributes only its best fixed field score: exact name 1000, name
prefix 800, name substring 600, container 400, kind 300, path 200, or content
100. Results are then ordered by score and stable source/chunk tie-breaks.
Queries are limited to one line and 256 characters; responses return 25
results by default and at most 100, with exact total count, truncation state,
a snippet of at most 480 characters, and repository-relative line evidence.
Missing, corrupt, incompatible, stale, and unverifiable index states use
distinct errors and never trigger an implicit rebuild. Syntax recovery or
truncated indexed chunk content makes matching evidence explicitly `partial`.

## Local Git evidence

`recent_changes` returns the current branch or detached-HEAD state, full HEAD
revision, bounded recent commit subjects and changed paths, and optionally the
staged, unstaged, conflicted, and untracked paths in the working tree. It
defaults to 10 commits and accepts 1 through 50. Working-tree inspection is on
by default and can be disabled.

The tool returns no author names, email addresses, commit bodies, diffs, remote
URLs, Git configuration, credentials, or absolute paths. Commit subjects are
limited to 240 characters, working-tree changes to 200, and changed paths to
200 per commit. Git stdout is limited to 2 MiB, stderr to 32 KiB, and each
command to five seconds.

Sanjaya starts the installed `git` executable directly with fixed read-only
arguments; it never uses a shell or accepts arbitrary Git arguments. Paging,
terminal prompts, external diffs, fsmonitor execution, optional locks, and
inherited `GIT_*` redirection variables are disabled; system and user-global
Git configuration are not loaded. Only worktree-root
verification, symbolic HEAD/revision lookup, porcelain status, and bounded log
operations are performed. No fetch, pull, push, hook, credential, checkout,
staging, commit, or other mutation operation is used.

The selected Sanjaya root must also be the Git worktree root. A normal `.git`
directory and standard nonsymlink Git worktree metadata file are supported;
symlinked Git metadata is rejected. Stable failures distinguish missing root,
non-Git root, root mismatch, missing Git, timeout, output limit, cancellation,
and command/parse failure.

## Planned v0.1 matrix

| Capability | C# | TypeScript/JavaScript | Other readable files |
|---|---|---|---|
| File outline | Roslyn structure | TypeScript AST structure | Generic metadata and preview |
| Exact text search | Supported | Supported | Supported |
| Structural indexing | Supported | Planned | Unsupported |
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
