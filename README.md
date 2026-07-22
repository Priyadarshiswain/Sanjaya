# Sanjaya

**Codebase vision, grounded in evidence.**

Sanjaya provides local-first codebase discovery for AI agents, with precise
navigation and verifiable evidence such as repository-relative paths, symbols,
and line ranges.

The first delivery is a .NET Model Context Protocol (MCP) server. A portable
evidence-first code-discovery skill is planned for a later release and is not
included in the current development build.

In the Mahabharata, Sanjaya could perceive events that others could not see
directly and report them faithfully. This project serves a similar role for AI
coding agents: it helps them see across a codebase and report what they find
with evidence.

## Project status

Sanjaya has a reviewed `0.1.0` release candidate, but it has not been published.
The command below will become usable only after the separately approved npm
release is complete and independently verified.
It implements capability reporting, a health check, bounded exact-text search,
Roslyn syntax outlines for C#, generic readable-file outlines, and bounded local
Git change evidence. A bounded C# structural-chunk provider powers the
repository-local deterministic index, which can be rebuilt explicitly with
`index_codebase` and searched read-only with deterministic lexical ranking via
`search_code`. Exact C# syntax declarations can be located with
`find_definition`, including explicit ambiguity reporting.
Exact C# identifier usages can be inspected as honestly labelled syntax
candidates with `find_references`. The resulting stable declaration chunk IDs
can be used with `get_source` for exact bounded C# source retrieval.
TypeScript and JavaScript files receive compiler-backed syntax outlines and
structural chunks through a bounded local worker, and those chunks participate
in the same deterministic index and lexical search.
Discovery is scoped to one explicit repository root per process. The npm
package and Official MCP Registry entry are not available yet.
The future VS Code install-once configuration is defined and tested, but its
public link remains inactive until an exact npm release is available. See the
[VS Code integration contract](docs/vscode.md).
Official MCP Registry metadata is also defined and checked offline, but no
registry entry has been submitted. See the
[registry metadata contract](docs/registry.md).
The launcher provides local `--help`, `--version`, and
`--diagnose` commands so setup failures can be understood before an MCP client
starts. Diagnostics report stable reason codes and remediation without reading
source files, writing an index, contacting the network, or exposing the
configured absolute path.
The official TypeScript 6.0.3 compiler API subset and its complete notices are
vendored for the syntax provider. Semantic TypeScript/JavaScript definitions,
references, type checking, module resolution, and source retrieval are not
claimed.

## Install after publication

Prerequisites are Node.js 22.13 or newer and the .NET 8 runtime. Git is optional
and is needed only for `recent_changes`.

Check one repository before adding Sanjaya to an MCP client:

```bash
npx -y sanjaya-mcp@0.1.0 --diagnose --root /absolute/path/to/repository
```

An MCP client should then start the same exact package over stdio with separate
arguments for `--root` and the repository's absolute path. Pinning `0.1.0`
keeps setup reproducible; do not replace it with `latest`. When moving between
projects, configure the client to substitute that project's workspace folder
and start a separate Sanjaya process. Each process remains confined to one
immutable repository root.

Review the package name, version, command, and root argument in the client's
trust prompt. To remove Sanjaya, remove or disable its MCP server configuration;
`npx` does not require a global Sanjaya installation. See the
[VS Code integration contract](docs/vscode.md) and
[first-run diagnostics](docs/diagnostics.md).

## v0.1 direction

- .NET 8 stdio MCP server
- npm installation and launcher channel
- No network operations in the default implementation
- Roslyn-backed C# discovery
- TypeScript compiler AST chunking for TypeScript and JavaScript
- Generic file and exact-text capabilities for other readable languages
- Deterministic repository-local structural index
- Explicit capability and degradation reporting

See [capabilities](docs/capabilities.md), [architecture](docs/architecture.md),
[privacy](docs/privacy.md), [packaging](docs/packaging.md), and
[VS Code integration](docs/vscode.md), and
[registry metadata](docs/registry.md) for the public contract. Release operators
should use the [approval-gated release runbook](docs/releasing.md).

## Approved v0.1 MCP tools

- `capabilities`
- `health_check`
- `file_outline`
- `search_text`
- `recent_changes`
- `index_codebase`
- `search_code`
- `find_definition`
- `find_references`
- `get_source`

These names form the approved v0.1 contract. The current runtime registers
`capabilities`, `health_check`, `file_outline`, `search_text`, `recent_changes`,
`index_codebase`, `search_code`, `find_definition`, `find_references`, and
`get_source`.
Discovery tools report
`repository_root_required` until the process starts with a valid
`--root <path>`; local Git evidence also requires that root to be a Git
worktree root, and indexed discovery requires an index created by
`index_codebase`.

## Development

Building from source requires the .NET 8 SDK and Node.js 22.13 or newer. See the
[development guide](docs/development.md) for validation commands and the
[diagnostics guide](docs/diagnostics.md) for the first-run contract.

## License

Sanjaya is licensed under the [Apache License 2.0](LICENSE). Distributed
third-party components retain their own licenses and notices.
