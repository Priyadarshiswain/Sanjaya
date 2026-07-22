# Changelog

All notable changes to Sanjaya will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and releases will follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] - 2026-07-23

### Added

- Local-first .NET 8 stdio MCP server with explicit repository-root scoping.
- Capability reporting, health checks, exact-text search, file outlines, and
  bounded local Git change evidence.
- Deterministic repository-local indexing and lexical code search.
- Roslyn-backed C# outlines, structural chunks, exact syntax definitions,
  syntax-reference candidates, and bounded source retrieval.
- TypeScript-compiler-backed structural outlines and chunks for TypeScript and
  JavaScript.
- Generic readable-file capabilities for other languages.
- Exact-version npm launcher candidate with first-run diagnostics, reproducible
  packaging checks, and Apache-2.0 plus third-party notices.

### Security and privacy

- No default runtime network operation; npm network access is limited to package
  acquisition.
- One immutable repository root per process, bounded responses, path
  containment, and no execution of inspected project source.
- Exact package allowlist, privacy scans, no npm dependencies, and no install
  lifecycle scripts.

### Limitations

- TypeScript and JavaScript support is structural, not semantic; definitions,
  references, type checking, module resolution, and source retrieval are not
  claimed.
- Definitions, reference candidates, and source retrieval are C#-only in
  v0.1.0.
- Multi-root orchestration, automatic root switching, remote hosting, and a VS
  Code extension are not included.
