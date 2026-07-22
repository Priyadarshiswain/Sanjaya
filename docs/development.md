# Development guide

## Prerequisites

- .NET SDK 8.0.418 or a compatible later patch
- Node.js 22.13 or newer
- Git for repository-oriented capabilities

## Validate the development build

```bash
dotnet restore Sanjaya.sln
dotnet build Sanjaya.sln --no-restore --configuration Release
dotnet test Sanjaya.sln --no-build --configuration Release
dotnet format Sanjaya.sln --verify-no-changes
node --check bin/sanjaya-mcp.js
node --check bin/sanjaya-diagnostics.js
```

## Build the npm payload

```bash
npm run build
npm run verify:typescript
npm run verify:typescript-worker
npm run verify:launcher
npm run verify:diagnostics
npm run verify:vscode-install
npm run verify:registry-metadata
npm run verify:package
npm run verify:installed-package
npm run verify:reproducible-package
npm run release:candidate
npm run verify:release-candidate
```

`npm run build` deletes and recreates only the ignored `dist/dotnet` staging
directory. It rejects symlinked staging content and release-boundary overrides.
Release compiler paths are normalized at the repository boundary, and the
package publish excludes portable PDBs from staging. Package inspection checks
an exact file allowlist, privacy patterns, and size ceilings. The
installed-package check packs a real tarball, installs it into an isolated
temporary consumer with lifecycle scripts disabled and offline mode, then
verifies the installed version and readiness commands before completing an MCP
handshake through the installed launcher. The
reproducibility check performs two clean builds and compares every file hash,
npm integrity value, and tarball hash.

See the [packaging contract](packaging.md) for the distribution boundary and
the exact verification guarantees.

The package metadata is pinned to the `0.1.1` corrective candidate; `0.1.0`
remains the independently verified public release until publication completes.
Candidate creation, tag creation, workflow dispatch, environment approval, npm
publication, registry submission, and installation-link activation remain
separate owner-approved actions. See the
[v0.1.1 release runbook](releasing-0.1.1.md).

`verify:vscode-install` proves the future VS Code user-profile configuration
pins one exact release, passes `${workspaceFolder}` as the immutable root, and
keeps the live installation URL out of public docs while activation is pending.
The public [VS Code integration guide](vscode.md) remains non-installing until
link activation is separately approved after registry verification.

`verify:registry-metadata` checks `server.json` without contacting a registry.
It locks the official schema URL, GitHub identity and repository id, npm
package ownership fields, exact candidate version, stdio transport, required
repository-root input, and 4 KiB metadata ceiling. The public
[registry metadata guide](registry.md) describes the publication boundary.

## Run from an MCP client

After `npm run build`, the development launcher is:

```bash
node bin/sanjaya-mcp.js --root /absolute/path/to/repository
```

Before configuring a client, inspect the launcher contract and local readiness:

```bash
node bin/sanjaya-mcp.js --help
node bin/sanjaya-mcp.js --version
node bin/sanjaya-mcp.js --diagnose --root /absolute/path/to/repository
```

`--diagnose` exits after local read-only checks; it does not start MCP. See the
[diagnostics guide](diagnostics.md) for its guarantees and stable results.

It communicates using JSON-RPC over stdio, so it should be started by an MCP
client rather than used as an interactive terminal command. The launcher
forwards `--root` without interpreting it. A VS Code workspace configuration
can supply the active folder at process launch:

```json
{
  "servers": {
    "sanjaya": {
      "type": "stdio",
      "command": "node",
      "args": ["/path/to/sanjaya/bin/sanjaya-mcp.js", "--root", "${workspaceFolder}"]
    }
  }
}
```

The current server also registers `file_outline`, `search_text`,
`recent_changes`, `index_codebase`, `search_code`, `find_definition`,
`find_references`, and `get_source`.
Missing or invalid root configuration does not prevent MCP initialization,
capability reporting, or health checks;
discovery returns stable setup guidance instead. Local Git evidence
additionally requires the configured root to be the Git worktree root and an
installed Git executable.
C# files use a bounded Roslyn syntax outline; other readable files retain the
generic preview. TypeScript and JavaScript files use the pinned TypeScript
6.0.3 compiler for syntax-only outlines and structural chunks. The C#,
TypeScript, and JavaScript structural providers power the explicit
deterministic local index. After `index_codebase`, `search_code` performs
read-only deterministic lexical search and refuses a stale or incompatible
index; `find_definition` adds exact case-sensitive C# syntax-declaration
navigation with explicit ambiguity; `find_references` returns bounded Roslyn
identifier candidates without semantic-binding claims; and `get_source`
resolves a stable indexed chunk ID to exact bounded declaration source.
Definitions, references, and source retrieval remain C#-only.

`verify:typescript` checks the vendored TypeScript allowlist, provenance hashes,
fixed-path loading, bounded TypeScript and JavaScript parsing, and published
file hashes. `verify:typescript-worker` checks the strict worker protocol,
supported syntax fixtures, inert treatment of project source, and fixed worker
capability boundary. `verify:package` checks the complete npm payload against
the reviewed allowlist and rejects missing notices, unexpected files, local
paths, private-project markers, lifecycle scripts, dependencies, and size
drift. The worker never executes project source.
