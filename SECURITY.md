# Security policy

## Supported versions

Sanjaya has not released a supported version yet. This policy will be updated
before the first public release.

## Reporting a vulnerability

Do not disclose suspected vulnerabilities in a public issue.

After the public repository is created, use its private vulnerability-reporting
feature. If that feature is unavailable, use the private maintainer contact
published in the repository's security settings. A dedicated contact will be
recorded before external contributions or releases are enabled.

Useful reports include affected versions, reproduction steps, expected impact,
and any suggested mitigation. Do not include secrets or data belonging to
another person.

## Security principles

- Default operation is local and network-free.
- Repository boundaries are enforced after canonical path resolution.
- Generated indexes are treated as sensitive source-derived data.
- Standard output is reserved for MCP JSON-RPC.
- Dependencies and release artifacts are pinned, scanned, and attributable.

