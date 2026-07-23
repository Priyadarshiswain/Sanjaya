# v0.1.2 compatibility release runbook

This runbook covers the `0.1.2` compatibility release. Public evaluation
readiness probes against pinned FastEndpoints, Vitest, Kiota, and Aspire
repositories found three product defects before any paid model evaluation:

- repository fingerprints could differ between index construction and
  freshness verification when nested files were traversed in a different
  order;
- the directory name `packages` was excluded even when it contained first-party
  TypeScript or JavaScript monorepo source; and
- Roslyn recovery for newer C# syntax could emit an unnamed declaration that
  made the generated index fail its own integrity checks.

The existing `0.1.1` package remains public, immutable, and installable. It must
not be unpublished, replaced, or reused. The `0.1.2` candidate is not public
until the protected npm workflow completes and the result is independently
verified.

## Reviewed change boundary

The readiness pull request and its reviewed `main` base define a candidate
that:

- updates `package.json`, `package-lock.json`, and `server.json` to `0.1.2`;
- retains the exact registry identity
  `io.github.Priyadarshiswain/sanjaya`;
- locks release evidence, artifact names, the workflow allowlist, and the
  annotated tag to `v0.1.2`;
- records `0.1.1` as the working public installation while `0.1.2` is a
  candidate;
- includes the three separately reviewed indexing compatibility fixes and their
  regression tests;
- keeps the approved MCP tool names and capability contract unchanged;
- keeps existing traversal, output, source-byte, and 1 MiB per-file limits
  unchanged; and
- leaves the evaluation dependency pinned to public `0.1.1` until `0.1.2` is
  published and independently verified.

It does not configure npm, create or push a tag, dispatch a workflow, publish a
package, update the evaluation lockfile, run a paid model, submit registry
metadata, create a GitHub release, or activate a VS Code installation link.

## Owner setup before release execution

Confirm that npm still lists the trusted publisher for `sanjaya-mcp` with these
exact fields:

```text
Provider: GitHub Actions
Organization or user: Priyadarshiswain
Repository: Sanjaya
Workflow filename: release.yml
Environment: npm-release
Allowed action: npm publish
```

The GitHub `npm-release` environment must continue to require owner approval,
disallow administrator bypass where supported, and allow only the exact
`v0.1.2` tag. It must contain no npm token or other publishing secret. The
publish job runs on a GitHub-hosted runner with `id-token: write` and receives
only a short-lived OIDC credential from npm's trusted-publisher exchange.

Stop if the npm publisher fields, GitHub environment policy, repository name,
workflow filename, tag, package version, or registry identity differs from this
contract.

## Local candidate verification

From a clean checkout with Node.js 22.13 or newer and the .NET 8 SDK:

```bash
npm ci --ignore-scripts
dotnet restore Sanjaya.sln
dotnet build Sanjaya.sln --no-restore --configuration Release
dotnet test Sanjaya.sln --no-build --configuration Release
dotnet format Sanjaya.sln --no-restore --verify-no-changes
npm run verify:vscode-install
npm run verify:registry-metadata
npm run build -- --no-restore
npm run verify:typescript
npm run verify:typescript-worker
npm run verify:launcher
npm run verify:diagnostics
npm run verify:package
npm run verify:installed-package
npm run release:candidate
npm run verify:release-candidate
```

Local output is review evidence only. Publication uses the exact artifact built
on the GitHub-hosted runner from the annotated release tag.

## Separately approved release ceremony

1. Merge the green readiness pull request during the agreed release window.
2. Recheck the npm trusted publisher and GitHub `npm-release` environment tag
   policy. Neither action publishes a package.
3. Create one annotated `v0.1.2` tag at the exact readiness merge commit,
   inspect it, and separately approve pushing only that tag.
4. Dispatch **Publish npm release** with tag input `v0.1.2` and `publish`
   disabled. The candidate job has no npm credential.
5. Download the candidate evidence and compare its commit, manifest, integrity,
   and hashes with the approved source.
6. Separately dispatch or approve the publication-enabled run. Approve the
   protected `npm-release` environment only after its downloaded artifact
   matches the reviewed candidate.
7. Verify `sanjaya-mcp@0.1.2` from the public npm registry using a clean
   installation, diagnostics, an MCP handshake, and npm provenance.
8. Confirm that no npm secret exists in GitHub and that trusted publishing was
   used.
9. In a separate pull request, pin the evaluation harness and controlled
   fixture to the exact public `sanjaya-mcp@0.1.2` artifact, rerun Layer 0, and
   review the evidence before approving any model spend.
10. Only after separate owner approval, authenticate to the Official MCP
    Registry, publish `io.github.Priyadarshiswain/sanjaya@0.1.2`, verify the
    public record, and log out.
11. Create a GitHub release or activate the VS Code one-click link only through
    later, separately approved steps.

## Failure and recovery

Before npm publication, reject the environment deployment and correct the
candidate in a new pull request. Do not move or reuse an approved tag.

After npm publication, `0.1.2` is immutable. A material correction requires a
new patch version. A failed Official MCP Registry submission must leave npm and
the Git tag unchanged; diagnose the registry response before attempting
another version.
