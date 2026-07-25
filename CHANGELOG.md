# Changelog

All notable changes to Sanjaya will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and releases will follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Verify install-once workspace switching with two independent Sanjaya MCP
  processes and explicit cross-root isolation on Linux, macOS, and Windows.
- Define the design-only Evidence-First Code Discovery skill contract,
  including capability-aware routing, native fallbacks, opt-in indexing,
  evidence requirements, stopping rules, and a separate evaluation boundary.
- Add the contract-matched two-file `evidence-first-code-discovery` skill in a
  minimal local skills-only Codex plugin without installing, marketplace
  listing, publishing, or including it in the npm server package.
- Add an offline verifier that generates, validates, and removes an exact
  test-only local marketplace without invoking Codex or modifying personal
  plugin state.
- Add a Docker-contained Codex lifecycle verifier for offline discovery,
  install, cachebuster update, reinstall, removal, and cleanup, with
  interactive and authenticated gaps reported explicitly.
- Add a preregistered Evidence-First skill evaluation contract with a fresh
  native control, disposable per-session plugin installation, scorer 1.1,
  selective-routing and side-effect gates, and a 24-run review stage before
  the remaining 48 runs can be authorized.
- Record all 72 Evidence-First skill evaluation runs, including two retained
  authentication failures, lower strict success in the skill arm, selective
  native routing, four active Sanjaya sessions, zero index writes, and complete
  token, latency, trace, and privacy evidence.
- Add a model-free engine gate over 15 frozen evidence targets and record its
  stop decision: native and Sanjaya both achieved full target recall, while
  Sanjaya did not achieve the preregistered aggregate precision or
  response-size benefit required to justify another agent-routing study.

### Changed

- Record `0.1.2` as the independently verified npm release and prevent the
  publication workflow from rebuilding its immutable version.
- Refine the Evidence-First skill after clean forward testing so clients can
  fall back to live tool schemas when `capabilities` is not exposed, native
  tools retrieve only missing evidence, and unrequested Git identity or
  metadata is omitted.

## [0.1.2] - 2026-07-23

### Fixed

- Make repository fingerprints independent of filesystem traversal order so a
  freshly built nested index is immediately readable.
- Include supported source under common `packages/` monorepo directories in
  structural indexing and exact-text search.
- Prevent Roslyn recovery for newer C# syntax from emitting unnamed
  declarations that invalidate the generated index.

### Limitations

- The existing 1 MiB per-file discovery ceiling is unchanged; an eligible
  source file above that limit still causes indexing to fail explicitly.

## [0.1.1] - 2026-07-23

### Fixed

- Correct the Official MCP Registry identity to match the canonical
  capitalization of the GitHub account. Runtime behavior and MCP capabilities
  are unchanged.

### Security

- Replace the bootstrap npm token path with GitHub Actions trusted publishing
  through short-lived OIDC credentials.

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
