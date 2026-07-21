# Privacy and local data

Sanjaya's default v0.1 contract is local-first and network-free.

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

Users must treat `.sanjaya/` as sensitive local build data and must not commit
or distribute it. Sanjaya documentation and generated setup guidance must make
this explicit.

## Network behavior

Default tools must not contact GitHub, model providers, analytics services, or
any other remote endpoint. Sanjaya will not include telemetry in v0.1.

Future semantic search or generated summaries must be separately opt-in. Before
activation, Sanjaya must identify the endpoint and state that source-derived
content may be transmitted to it.

## Diagnostics

Errors and logs must avoid credentials, environment-variable values, local
absolute paths when relative paths suffice, and source content unrelated to the
requested evidence.
