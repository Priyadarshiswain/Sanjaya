# Packaging contract

Sanjaya `0.1.1` is published with npm provenance. The repository now builds the
`0.1.2` compatibility candidate, which fixes index freshness across nested
paths, includes supported source under `packages/`, and prevents invalid
unnamed C# chunks. Preparing that candidate alone does not make it available
from npm.

## Runtime boundary

The npm package is a launcher and distribution channel, not a JavaScript
rewrite. It contains a thin Node.js launcher and one framework-dependent .NET 8
publish output. A user needs Node.js 22.13 or newer and the .NET 8 runtime. Git
is optional and is needed only for `recent_changes`.

The .NET payload is launched as `dotnet Sanjaya.Server.dll`. It is built with
`UseAppHost=false`, so the package does not contain an operating-system-specific
app host. Portable PDBs and unused satellite resources are excluded from the
publish directory, while deterministic compiler paths are normalized at the
repository boundary. The package has no npm dependencies and defines no
installation lifecycle scripts.

## Clean staging

`npm run build` resolves the repository root from its own location and deletes
only `dist/dotnet`, which is an ignored staging directory. It refuses an
unexpected path, a symlinked staging path, symlinks within existing staging
content, and command-line arguments that could change the reviewed package
boundary. Normal and package Release builds use the same deterministic compiler
settings and normalized paths, so safe incremental reuse cannot reintroduce an
owner checkout path or change assembly bytes. Every package build starts from
clean staging and excludes symbols when copying to that staging directory.

## Exact payload

The package verifier compares `npm pack --dry-run --json --ignore-scripts`
against the exact reviewed file allowlist in `scripts/package-contract.mjs`.
Every entry must be a regular file. Verification fails on an unexpected or
missing file, forbidden lifecycle script, npm dependency field, private-project
marker, owner checkout path, local planning path, private-key marker, or
delegation metadata.

Review ceilings prevent silent payload growth: at most 64 entries, 9 MiB
compressed, and 25 MiB unpacked. The current reviewed target is 61 entries,
approximately 6.5 MiB compressed and 23.3 MiB unpacked. A future dependency or
payload change must deliberately update both the exact allowlist and these
ceilings where necessary.

## Release evidence

`npm run verify:installed-package` creates a real tarball, installs it offline
into an isolated temporary consumer with scripts disabled, compares the
installed files with the same allowlist, scans their bytes, and exercises the
installed npm executable shim through version and readiness diagnostics, an MCP
initialize, tool discovery, and a representative discovery workflow.

`npm run verify:reproducible-package` performs two clean package builds. It
compares file paths, per-file SHA-256 values, npm integrity and shasum values,
and the raw tarball SHA-256. CI runs the package and installed-launcher checks on
Ubuntu, macOS, and Windows with Node.js 22 and .NET 8.

`npm run release:candidate` first proves two clean package builds are identical,
then writes the exact tarball, per-file manifest, source commit, npm integrity,
and SHA-256/SHA-512 evidence to ignored `dist/release`. The companion
`npm run verify:release-candidate` check validates that evidence without
repacking the artifact.

These checks create evidence for publication review. They do not reserve the
npm name, create a registry entry, tag a release, dispatch a workflow, or
publish an artifact.

`npm run verify:registry-metadata` separately proves that the repository's
Official MCP Registry identity, npm ownership fields, exact package version,
stdio transport, and required `--root <path>` inputs agree with this package.
The check is offline and requires exact `0.1.2` agreement, public-access
metadata, and npm provenance; it does not validate a published artifact or
write to a registry. Installed-tarball verification also reads the packed
`package.json` and proves that npm preserved the exact `mcpName` ownership
field, package name, version, and license.

The future VS Code installation configuration references the exact published
package version verified by this process. Its generated installation URL
remains inactive pending separate review and Official MCP Registry verification.
Package publication and link activation are separate release actions.
