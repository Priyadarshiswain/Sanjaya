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

Sanjaya has a working, development-only MCP server and has not been released.
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
package, MCP Registry entry, and installation commands do not exist yet.
The development launcher provides local `--help`, `--version`, and
`--diagnose` commands so setup failures can be understood before an MCP client
starts. Diagnostics report stable reason codes and remediation without reading
source files, writing an index, contacting the network, or exposing the
configured absolute path.
The official TypeScript 6.0.3 compiler API subset and its complete notices are
vendored for the syntax provider. Semantic TypeScript/JavaScript definitions,
references, type checking, module resolution, and source retrieval are not
claimed.

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
[privacy](docs/privacy.md), and [packaging](docs/packaging.md) for the proposed
public contract.

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

The development build requires the .NET 8 SDK and Node.js 22.13 or newer. See the
[development guide](docs/development.md) for validation commands and the
[diagnostics guide](docs/diagnostics.md) for the first-run contract.

## License

Sanjaya is licensed under the [Apache License 2.0](LICENSE). Distributed
third-party components retain their own licenses and notices.
