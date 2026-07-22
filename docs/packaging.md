# Packaging contract

Sanjaya is not published. The current npm metadata remains locked with
`private: true` and version `0.0.0-development`; this document describes the
development package that must pass review before any release decision.

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

These checks create evidence for a future publication review. They do not
reserve the npm name, change a version, create a registry entry, tag a release,
or publish an artifact.

The future VS Code installation configuration must reference the exact
published package version verified by this process. Its generated installation
URL remains inactive while npm metadata is `private`, development-versioned, or
not yet published. Package publication and link activation are separate,
explicitly reviewed release actions.
