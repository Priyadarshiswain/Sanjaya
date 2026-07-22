# Development guide

## Prerequisites

- .NET SDK 8.0.418 or a compatible later patch
- Node.js 18 or newer
- Git for repository-oriented capabilities

## Validate the development build

```bash
dotnet restore Sanjaya.sln
dotnet build Sanjaya.sln --no-restore --configuration Release
dotnet test Sanjaya.sln --no-build --configuration Release
dotnet format Sanjaya.sln --verify-no-changes
node --check bin/sanjaya-mcp.js
```

## Build the npm payload

```bash
npm run build
npm run verify:launcher
npm run verify:package
```

Package inspection uses an ignored project-local npm cache so validation does
not depend on machine-level cache ownership or permissions.

The package is intentionally marked `private` and versioned
`0.0.0-development`. Do not remove those safeguards until a publication step is
separately reviewed and approved.

## Run from an MCP client

After `npm run build`, the development launcher is:

```bash
node bin/sanjaya-mcp.js --root /absolute/path/to/repository
```

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
`recent_changes`, `index_codebase`, `search_code`, and `find_definition`.
Missing or invalid root configuration does not prevent MCP initialization,
capability reporting, or health checks;
discovery returns stable setup guidance instead. Local Git evidence
additionally requires the configured root to be the Git worktree root and an
installed Git executable.
C# files use a bounded Roslyn syntax outline; other readable files retain the
generic preview. The C# structural-chunk provider powers the explicit
deterministic local index. After `index_codebase`, `search_code` performs
read-only deterministic lexical search and refuses a stale or incompatible
index; `find_definition` adds exact case-sensitive C# syntax-declaration
navigation with explicit ambiguity. TypeScript/JavaScript AST structure remains
unimplemented.
