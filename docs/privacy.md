# Privacy and local data

Sanjaya's default v0.1 contract is local-first and contains no network
operation.

## Repository access

The server reads files inside one explicitly configured repository root per
process. Canonical path validation rejects traversal, absolute tool inputs,
prefix collisions, file symlinks, and symlink targets that escape this
boundary. Search traversal does not follow directory symlinks. Binary and
oversized-file guards run before text is returned, and public evidence and
diagnostics use repository-relative paths.

Immediate discovery performs no writes, subprocess execution, or network
access. Exact search excludes `.git`, `.sanjaya`, common build/package output,
dependency directories, and recognized generated files. This first slice does
not claim complete `.gitignore` compatibility.

## Local Git evidence

`recent_changes` launches the installed Git executable locally with fixed,
read-only arguments. It returns revision hashes, commit timestamps, bounded
commit subjects, and repository-relative changed paths. Commit subjects and
filenames can themselves contain sensitive project information, so users
should treat this output as repository content when choosing an MCP client or
model.

The tool does not return authors, email addresses, commit bodies, diffs, remote
URLs, configuration, credentials, or absolute paths. It clears inherited
`GIT_*` variables that could redirect repository access and disables prompts,
paging, external diffs, fsmonitor execution, and system/user-global Git
configuration loading. It never performs a remote or mutating Git operation.

## Local index

Structural indexing writes source-derived data under `.sanjaya/` in the target
repository. This directory may contain plaintext code chunks, symbols,
signatures, paths, and index metadata.

The current writer owns `.sanjaya/index-v1.json`, a fixed lock, and transient
same-directory temporary output only. It rejects symlinked storage and unknown
pre-existing targets, replaces a recognized index atomically, and preserves the
last good index on failure or cancellation. It never edits the repository's
`.gitignore`; instead it warns when the root ignore file does not explicitly
contain a direct `.sanjaya` rule.

`search_code`, `find_definition`, `find_references`, and `get_source` read this
plaintext index and re-hash the bounded eligible source set to verify
freshness. They perform no writes, subprocess calls, or network access and
never rebuild implicitly. Returned chunks, snippets, and declaration source
are source-derived content and should be treated with the same sensitivity as
the repository itself.

Users must treat `.sanjaya/` as sensitive local build data and must not commit
or distribute it. Sanjaya documentation and generated setup guidance must make
this explicit.

## Vendored TypeScript runtime

The npm payload contains an allowlisted, checksum-verified TypeScript compiler
API runtime and its complete upstream license and notices. TypeScript and
JavaScript source up to 1 MiB is sent to one persistent local Node worker over
bounded newline-delimited JSON. The worker receives a repository-relative
display path, never the repository root or an absolute project path. It parses
the supplied text with the pinned compiler and never reads, imports, or executes
project files.

The worker starts without a shell, inherits only a minimal environment, and has
read access only to its bundled runtime files. Filesystem writes, child
processes, worker threads, addons, WASI, and inspector access are not granted.
Requests and responses have finite byte, item, string, memory, stderr, and time
limits. Failures return stable sanitized codes without worker diagnostics,
source text, environment values, or executable paths. These controls are
defense in depth and are not a hostile-code sandbox.

## Network behavior

Default tools must not contact GitHub, model providers, analytics services, or
any other remote endpoint. Sanjaya will not include telemetry in v0.1.

The trusted TypeScript worker contains no network import or operation and
repository source is parser input, never executed code. This is the enforceable
design boundary on Node 22 and 24 because those runtime lines do not provide a
network permission control. Their Permission Model still restricts the other
capabilities listed above. On newer Node lines, the same launch policy may also
deny network access at runtime. Sanjaya does not describe the Node Permission
Model as an operating-system network sandbox.

The complete npm payload is also checked against an exact reviewed file list
and high-confidence private-path and private-project byte patterns. Debug
symbols are excluded so build-machine source paths are not distributed. These
checks are documented in the [packaging contract](packaging.md).

Future semantic search or generated summaries must be separately opt-in. Before
activation, Sanjaya must identify the endpoint and state that source-derived
content may be transmitted to it.

## Diagnostics

Errors and logs must avoid credentials, environment-variable values, local
absolute paths when relative paths suffice, and source content unrelated to the
requested evidence.

Launcher `--diagnose` is an explicit non-MCP, local, read-only mode. It checks
the active Node version, packaged server and TypeScript files, installed .NET 8
runtime, repository-directory readability, and optional Git worktree metadata.
It never reads repository source, builds an index, loads project configuration,
or contacts the network. Output uses stable codes and generic remediation; it
does not echo the configured absolute root, raw subprocess stderr, or ambient
environment values.
