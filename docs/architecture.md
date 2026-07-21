# Architecture

## Design goals

Sanjaya separates MCP transport, repository-safe core services, and
language-specific knowledge. The separation lets each provider report only the
capabilities it genuinely implements and makes those claims contract-testable.

```mermaid
flowchart LR
    Client["MCP client"] -->|stdio| Server["Sanjaya.Server"]
    Server --> Core["Sanjaya.Core"]
    Core --> Text["Generic text fallback"]
    Core --> CSharp["C# provider (Roslyn)"]
    Core --> TypeScript["TypeScript/JavaScript provider"]
    Core --> Index["Local .sanjaya index"]
```

## Project boundaries

### `Sanjaya.Server`

Owns MCP stdio hosting, tool registration, input/output schemas, cancellation,
and conversion between domain results and MCP content blocks. Standard output
is reserved for JSON-RPC.

### `Sanjaya.Core`

Owns public response contracts, capability descriptions, repository context,
canonical path containment, evidence models, file guards, deterministic index
contracts, and provider discovery.

Core must not depend on Roslyn, the TypeScript compiler, or network services.

### `Sanjaya.Providers.CSharp`

Owns Roslyn-backed C# outlines, structural chunks, definitions, references, and
symbol-addressed source retrieval. v0.1 must describe syntax-based operations
honestly and must not imply full build or solution semantic resolution.

### `Sanjaya.Providers.TypeScript`

Owns TypeScript compiler AST integration for TypeScript and JavaScript outlines
and chunks. Compiler distribution is blocked until the provenance and notice
gate in `third_party/typescript/README.md` is satisfied.

## Extension model

Core exposes a small discovery contract through `ICapabilityProvider`.
Operation-specific provider interfaces will be added only with their first
implementation. Dynamic plugin loading and a separately versioned provider SDK
are deferred beyond v0.1.

## Distribution boundary

The root npm package contains a thin Node launcher and a framework-dependent
.NET 8 publish output. The launcher forwards stdio and process signals; it does
not implement product behavior or download code during installation.

