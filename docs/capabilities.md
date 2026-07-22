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
- `definition_provider_unavailable` means this runtime has no active C#
  structural provider that honestly advertises syntax definitions.
- `reference_provider_unavailable` means this runtime has no active C# provider
  that honestly advertises syntax-reference candidates.
- `source_provider_unavailable` means this runtime has no active C# provider
  that honestly advertises exact syntax-source retrieval.
- `index_missing` means indexed discovery is implemented but `index_codebase` has
  not created its repository-local index.
- `index_invalid` means the expected index path is not a bounded regular local
  file; invoking the indexed tool provides the more specific safe failure.

## Current development runtime

`capabilities`, `health_check`, `file_outline`, `search_text`, `recent_changes`,
`index_codebase`, `search_code`, `find_definition`, `find_references`, and
`get_source` are registered as MCP tools. Generic discovery and the C#
syntax provider report `supported` only when the process has a valid root. C#
currently supports file outlines, structural indexing, exact syntax definition
lookup, syntax-reference candidates, and symbol-addressed source retrieval;
call graph remains unavailable. The TypeScript/JavaScript provider remains
unavailable with `not_implemented`. The server uses stdio and performs no
network access by default.

The package contains a provenance-verified TypeScript 6.0.3 compiler API subset
and complete upstream notices. Merely bundling that inactive runtime does not
change capability reporting: no TypeScript/JavaScript process is started and
no AST claim is made until the provider's separate safety and behavior contract
is implemented.

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

`find_definition` is read-only and applies exact ordinal C# declaration-name
matching to the same compatible fresh index. Optional exact `kind`,
`container`, and repository-relative `path` filters disambiguate common names
and overloads. It reports `not_found`, `unique`, or `ambiguous`; ambiguity is a
complete result rather than an error. Results use stable source ordering and
include provider, language, declaration metadata, chunk identifier, a snippet
of at most 480 characters, and one-based repository-relative evidence.

Names and containers are limited to 240 characters. Supported v0.1 kinds are
namespace, record, record struct, class, struct, interface, enum, delegate,
method, constructor, destructor, property, indexer, operator, and conversion
operator. Responses return 25 matches by default and at most 100 while
reporting the exact total and truncation state. Lookup is case-sensitive,
never rebuilds the index, and does not load projects, resolve invocations or
overloads semantically, inspect referenced assemblies, or claim compiler-level
symbol resolution.

`find_references` verifies the same current index and then rescans bounded
eligible C# source with Roslyn. It matches exact case-sensitive identifier and
generic-name tokens while excluding declaration names, comments, and strings.
Every result is labelled `syntax_candidate` and includes an exact token range,
fixed syntax kind, enclosing declaration metadata, and a source-line snippet
of at most 320 characters. An optional repository-relative C# path limits the
search scope.

Responses return 50 candidates by default and at most 200 while reporting the
exact total. A file may contribute at most 10,000 matches and the repository at
most 50,000; exceeding either bound fails with `reference_limit`. Syntax
diagnostics produce explicit partial evidence. The tool does not semantically
distinguish same-name locals, overloads, aliases, inherited members, or types.

`get_source` accepts only an exact lowercase chunk ID returned by `search_code`
or `find_definition`. It verifies the current compatible index, reuses the
same bounded in-memory source snapshot used for freshness validation, and asks
the active C# provider to resolve the indexed declaration to one exact Roslyn
syntax span. Missing, duplicate, or inconsistent resolution fails explicitly;
the tool never accepts an arbitrary path or chooses a declaration
heuristically.

The complete declaration is returned when its UTF-8 source is at most 64 KiB.
Larger declarations can be retrieved through an optional one-based inclusive
`startLine` and `endLine` window that must remain inside the exact declaration.
A window is reported as `partial` evidence and cannot expose adjacent source,
including when declarations share a physical line. Results contain the stable
chunk and index identifiers, provider and declaration metadata,
repository-relative path, exact start-inclusive/end-exclusive line and column
ranges, source text, and a `complete` flag. Syntax recovery remains explicit;
no project build or semantic resolution is claimed.

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
| Definitions | Exact syntax declarations | Unsupported | Unsupported |
| References | Syntax candidates | Unsupported | Unsupported |
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
