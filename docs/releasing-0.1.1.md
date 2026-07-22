# v0.1.1 corrective release runbook

This runbook covers the metadata-only `0.1.1` release. It corrects the Official
MCP Registry identity from `io.github.priyadarshiswain/sanjaya` to the canonical
GitHub-account casing `io.github.Priyadarshiswain/sanjaya`. Runtime behavior,
the npm package name, and the approved MCP tool contract are unchanged.

The existing `0.1.0` package remains public and installable. It must not be
unpublished, replaced, or reused. The `0.1.1` candidate is not public until the
protected npm workflow completes and the result is independently verified.

## Reviewed change boundary

The readiness pull request:

- updates `package.json`, `package-lock.json`, and `server.json` to `0.1.1`;
- uses the exact registry identity `io.github.Priyadarshiswain/sanjaya`;
- locks release evidence, artifact names, and the annotated tag to `v0.1.1`;
- retains `0.1.0` as the working public installation while `0.1.1` is a
  candidate;
- replaces the bootstrap `NPM_TOKEN` path with npm trusted publishing through
  GitHub OIDC; and
- changes no runtime source, provider, MCP tool, or capability contract.

It does not configure npm, create or push a tag, dispatch a workflow, publish a
package, submit registry metadata, create a GitHub release, or activate a VS
Code installation link.

## Owner setup before release execution

After the readiness pull request merges, configure one trusted publisher for
`sanjaya-mcp` on npm:

```text
Provider: GitHub Actions
Organization or user: Priyadarshiswain
Repository: Sanjaya
Workflow filename: release.yml
Environment: npm-release
Allowed action: npm publish
```

The GitHub `npm-release` environment must continue to require owner approval,
disallow administrator bypass, and allow only the exact `v0.1.1` tag. It must
contain no npm token or other publishing secret. The publish job runs on a
GitHub-hosted runner with `id-token: write` and receives only a short-lived OIDC
credential from npm's trusted-publisher exchange.

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
npm run release:candidate
npm run verify:release-candidate
```

Local output is review evidence only. Publication uses the exact artifact built
on the GitHub-hosted runner from the annotated release tag.

## Separately approved release ceremony

1. Merge the green readiness pull request during the agreed release window.
2. Configure and review the npm trusted publisher and the GitHub environment
   tag policy. Neither action publishes a package.
3. Create one annotated `v0.1.1` tag at the exact readiness merge commit,
   inspect it, and separately approve pushing only that tag.
4. Dispatch **Publish npm release** with tag input `v0.1.1` and `publish`
   disabled. The candidate job has no npm credential.
5. Download the candidate evidence and compare its commit, manifest, integrity,
   and hashes with the approved source.
6. Separately dispatch or approve the publication-enabled run. Approve the
   protected `npm-release` environment only after its rebuilt artifact matches
   the reviewed candidate.
7. Verify `sanjaya-mcp@0.1.1` from the public npm registry using a clean install,
   diagnostics, an MCP handshake, and npm provenance.
8. Confirm that no npm secret exists in GitHub and that trusted publishing was
   used.
9. Authenticate to the Official MCP Registry with GitHub, validate and publish
   `io.github.Priyadarshiswain/sanjaya@0.1.1`, verify the public record, and log
   out.
10. Create a GitHub release or activate the VS Code one-click link only through
    later, separately approved steps.

## Failure and recovery

Before npm publication, reject the environment deployment and correct the
candidate in a new pull request. Do not move or reuse an approved tag.

After npm publication, `0.1.1` is immutable. A material correction requires a
new patch version. A failed Official MCP Registry submission must leave npm and
the Git tag unchanged; diagnose the registry response before attempting another
version.
