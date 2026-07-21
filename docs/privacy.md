# Privacy and local data

Sanjaya's default v0.1 contract is local-first and network-free.

## Repository access

The server reads files inside one configured repository and recognized
worktrees. Canonical path validation must reject paths and symlink targets that
escape this boundary. Binary and oversized-file guards must run before parsing
or indexing.

## Local index

Structural indexing writes source-derived data under `.sanjaya/` in the target
repository. This directory may contain plaintext code chunks, symbols,
signatures, paths, and index metadata.

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

