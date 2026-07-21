# Sanjaya

**Codebase vision, grounded in evidence.**

Sanjaya is a local-first Model Context Protocol (MCP) server that helps AI
agents inspect a repository, navigate its structure, and support conclusions
with evidence such as repository-relative paths, symbols, and line ranges.

In the Mahabharata, Sanjaya could perceive events that others could not see
directly and report them faithfully. This project serves a similar role for AI
coding agents: it helps them see across a codebase and report what they find
with evidence.

## Project status

Sanjaya is an implementation scaffold and has not been released. The npm
package, MCP Registry entry, and installation commands do not exist yet. Please
do not publish or present the current scaffold as a functioning server.

## v0.1 direction

- .NET 8 stdio MCP server
- npm installation and launcher channel
- No network calls in the default configuration
- Roslyn-backed C# discovery
- TypeScript compiler AST chunking for TypeScript and JavaScript
- Generic file and exact-text capabilities for other readable languages
- Deterministic repository-local structural index
- Explicit capability and degradation reporting

See [capabilities](docs/capabilities.md), [architecture](docs/architecture.md),
and [privacy](docs/privacy.md) for the proposed public contract.

## Proposed tools

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

These names remain under review until the scaffold is approved for
implementation.

## Development

The scaffold requires the .NET 8 SDK and Node.js 18 or newer. See the
[development guide](docs/development.md) for validation commands.

## License

Sanjaya is licensed under the [Apache License 2.0](LICENSE). Distributed
third-party components retain their own licenses and notices.

