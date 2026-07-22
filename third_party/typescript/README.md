# TypeScript compiler distribution boundary

Sanjaya vendors an exact, unmodified subset of the official
`typescript@6.0.3` npm artifact for its planned TypeScript/JavaScript syntax
provider. The artifact identity, registry integrity values, independent
tarball SHA-256, and extracted-file SHA-256 values are recorded in
`PROVENANCE.json`.

Only these upstream files are approved:

- `package/package.json`
- `package/LICENSE.txt`
- `package/ThirdPartyNoticeText.txt`
- `package/lib/typescript.js`

The repository and npm payload intentionally exclude `tsc`, `tsserver`,
declaration libraries, localized diagnostics, source maps, native binaries,
tests, and development dependencies. The root npm launcher package has no
runtime JavaScript dependency and does not install TypeScript separately.

Normal CI verifies the allowlist and every extracted-file hash without network
access. The published payload is verified to contain the full upstream license
and third-party notice beside the compiler runtime.

The vendored runtime does not itself make TypeScript/JavaScript capabilities
available. Provider behavior, Node subprocess safety, bounded source transfer,
timeouts, and capability reporting require a separate reviewed implementation.

Files from an editor installation, global package, ambient `node_modules`,
cache, another application, or private source are never acceptable release
provenance. Any version upgrade requires a new artifact and notice review.
