# v0.1.0 release runbook

This repository is prepared for a `0.1.0` candidate. It is not evidence that
the npm package, Official MCP Registry entry, GitHub release, or VS Code install
link exists. Every external action below requires a separate owner decision.

## What the readiness pull request does

- pins `package.json`, `package-lock.json`, launcher diagnostics, and
  `server.json` to exact `0.1.0`;
- requires public npm access and provenance;
- builds the package twice and rejects any byte-level difference;
- records the exact tarball, per-file SHA-256 manifest, npm integrity, source
  commit, and tarball SHA-256/SHA-512 values under ignored `dist/release`;
- defines a manual-only GitHub Actions workflow with a no-secret candidate job
  and a separately protected npm publish job; and
- keeps the VS Code install link inactive and makes no registry/gallery claim.

It does not create a tag, configure GitHub or npm, store a credential, dispatch
a workflow, publish a package, submit registry metadata, or create a release.

## Owner setup before any release execution

1. Recheck that `sanjaya-mcp` is still available on npm. An earlier `E404`
   indicated only that it appeared unclaimed at that moment; it did not reserve
   the name.
2. Create a GitHub environment named exactly `npm-release`. Add an owner
   approval rule and prevent administrators from bypassing it where the
   repository plan supports that setting.
3. Because npm trusted publishing cannot bootstrap a package that has never
   been published, create one short-lived granular npm token with only the
   access required for this first public package publish and store it as the
   environment secret `NPM_TOKEN`. Never place its value in a file, issue,
   pull request, log, or repository-level secret.
4. Confirm the current npm trusted-publisher and provenance requirements before
   dispatch. After the first publish, revoke the bootstrap token immediately
   and configure a stage-only GitHub Actions trusted publisher for future
   versions.
5. Enable GitHub immutable releases before the GitHub release is created, then
   confirm the repository's release policy still matches the planned ceremony.

Stop if any identity, version, package name, permission, or current service
behavior differs from this reviewed contract.

## Local candidate verification

From a clean checkout with Node.js 22.13 or newer and the .NET 8 SDK:

```bash
npm ci --ignore-scripts
dotnet restore Sanjaya.sln
dotnet build Sanjaya.sln --no-restore --configuration Release
dotnet test Sanjaya.sln --no-build --configuration Release
dotnet format Sanjaya.sln --no-restore --verify-no-changes
npm run release:candidate
npm run verify:release-candidate
```

Local output is evidence for review only. Publication uses the exact artifact
built on the GitHub-hosted runner from the annotated release tag.

## Separately approved release ceremony

1. Merge the green readiness pull request only during the agreed release
   window.
2. Approve creation of one annotated `v0.1.0` tag at that exact merge commit,
   inspect it, and separately approve pushing only that tag.
3. Approve manual dispatch of **Publish npm release** with exact tag input
   `v0.1.0`. Leave the `publish` input disabled for an evidence-only run; enable
   it only when npm publication has also been explicitly approved. The
   candidate job has no npm credential and uploads the exact tarball plus its
   evidence.
4. Compare the candidate commit, manifest, integrity, and hashes with the
   approved source. If they match, separately approve the protected
   `npm-release` environment. The publish job downloads and verifies that same
   tarball; it does not repack it.
5. Verify `sanjaya-mcp@0.1.0` from the public npm registry using clean install,
   diagnostics, and an MCP handshake. Revoke the bootstrap npm token
   immediately.
6. Only after npm verification, separately approve submission of the matching
   `server.json` to the Official MCP Registry and verify its returned record.
7. Separately approve an immutable GitHub `v0.1.0` release using the reviewed
   changelog and exact npm artifact evidence.
8. Activate the exact-version VS Code install link in a later documentation
   pull request only after npm and registry verification. Do not claim GitHub
   MCP Registry inclusion unless it is independently observed there.

## Failure and recovery

Before npm publication, reject the environment deployment and fix the source in
a new pull request. Do not publish a questionable candidate.

After npm publication, `0.1.0` is immutable and cannot be reused. For a material
problem, deprecate the affected version with an honest message and publish a
fixed `0.1.1` through a new reviewed release. Official MCP Registry versions
cannot be edited in place, and an immutable GitHub release locks its tag and
assets, so corrections use a new version rather than rewriting history.
