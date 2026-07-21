# Development guide

## Prerequisites

- .NET SDK 8.0.418 or a compatible later patch
- Node.js 18 or newer
- Git for repository-oriented capabilities

## Validate the scaffold

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
npm run verify:package
```

Package inspection uses an ignored project-local npm cache so validation does
not depend on machine-level cache ownership or permissions.

The package is intentionally marked `private` and versioned
`0.0.0-development`. Do not remove those safeguards until a publication step is
separately reviewed and approved.

## Current limitation

The scaffold does not start an MCP server. `Sanjaya.Server` exits with an
explicit diagnostic until implementation is authorized.
