# Security policy

## Supported versions

Sanjaya has not released a supported version yet. This policy will be updated
before the first public release.

## Reporting a vulnerability

Do not disclose suspected vulnerabilities in a public issue.

Use the repository's **Report a vulnerability** action to submit a private
GitHub security advisory. If private reporting is temporarily unavailable,
open a public issue asking how to contact the maintainer, but do not include
vulnerability details in that issue.

Useful reports include affected versions, reproduction steps, expected impact,
and any suggested mitigation. Do not include secrets or data belonging to
another person.

## Security principles

- Default operation is local and network-free.
- Repository boundaries are enforced after canonical path resolution.
- Generated indexes are treated as sensitive source-derived data.
- Standard output is reserved for MCP JSON-RPC.
- Dependencies and release artifacts are pinned, scanned, and attributable.
