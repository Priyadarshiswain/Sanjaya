# Official MCP Registry metadata

Sanjaya has a reviewed `server.json` contract, but it has not been submitted to
the Official MCP Registry. The npm package is still private and versioned
`0.0.0-development`; there is no installable registry release.

The registry is currently in preview and stores server metadata rather than
the package artifact. Its [publishing quickstart](https://modelcontextprotocol.io/registry/quickstart)
requires the public npm package to exist before registry submission and
requires npm `mcpName` to match the `server.json` name.

## Reviewed identity and installation shape

- Registry name: `io.github.priyadarshiswain/sanjaya`
- Display title: `Sanjaya`
- Source and website: the public Sanjaya GitHub repository
- Package: exact-version `sanjaya-mcp` from the public npm registry
- Transport: local stdio
- Startup arguments: fixed `--root`, then one required non-secret repository
  path with the registry `filepath` format

The repository metadata includes GitHub's immutable repository id so ownership
does not depend only on a reusable owner/repository name. No icon, remote
endpoint, environment variable, secret, shell command, or unreviewed extension
metadata is declared.

The registry document is intentionally an identity and installation record. It
does not duplicate Sanjaya's full tool catalog, language capability matrix,
privacy guarantees, or third-party notices. Those remain authoritative in the
runtime `capabilities` response, [capabilities](capabilities.md),
[privacy](privacy.md), the Apache-2.0 [license](../LICENSE), and
[third-party notices](../THIRD-PARTY-NOTICES.txt).

## Offline verification

Run:

```bash
npm run verify:registry-metadata
```

The verifier makes no network request. It enforces the pinned official schema
URL, registry-name syntax, text limits, canonical HTTPS repository fields,
immutable repository id, exact npm identity and version agreement,
Apache-2.0 package metadata, stdio transport, argument order, configurable
repository path, absence of unexpected metadata, and the registry's 4 KiB JSON
limit. It also fails if `private: true` or `0.0.0-development` changes during
metadata preparation. The installed-tarball check independently proves that
the packed npm `package.json` retains the same `mcpName`, package name, version,
and license that the registry will use for ownership verification.

The pinned schema and current publisher behavior must be checked again during
release review because the registry remains in preview. A focused offline
contract avoids making ordinary builds depend on a mutable network response.

## Separately approved release order

1. Review the exact stable version in a release change. Replace the development
   version in `package.json` and `server.json` with that same version, remove
   the npm publication lock, and re-run every release check.
2. Build and approve the reproducible npm artifact for that exact version.
3. Publish the approved npm artifact and verify its installed diagnostics and
   MCP workflow from the public registry.
4. After a separate explicit owner approval, authenticate and submit the same
   version's metadata to the Official MCP Registry.
5. Verify the registry record before activating installation links or making
   downstream gallery claims.

The registry requires a unique version for each publication, and published
metadata cannot be edited in place. The current
[versioning guidance](https://modelcontextprotocol.io/registry/versioning)
therefore makes submission a release operation, not a routine CI action. No
registry login, automated publisher workflow, name reservation, submission,
tag, GitHub release, npm publication, VS Code install-link activation, or
GitHub MCP Registry claim is part of this metadata-preparation step.
