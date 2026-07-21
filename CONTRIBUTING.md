# Contributing to Sanjaya

Thank you for considering a contribution.

Sanjaya is still establishing its v0.1 implementation. Issues and pull requests
are welcome, but public capability claims must remain aligned with the approved
contract and the current project status.

Before starting a substantial implementation or public API change:

1. Search existing issues and pull requests for related work.
2. Open a feature request describing the problem and proposed capability.
3. Wait for scope agreement before investing in a large change.

Bug reports should include reproducible steps, environment information, and
the smallest useful evidence. Never include credentials, private source code,
or vulnerability details in a public issue.

## Design principles

- Return evidence rather than unsupported conclusions.
- Report capabilities honestly and explicitly.
- Keep default behavior local and network-free.
- Enforce repository boundaries before reading or indexing files.
- Add contract tests for every capability claim.
- Keep language-specific knowledge inside provider projects.
- Do not add dependencies without a license, security, and distribution review.

## Development checks

Run the validation contract described in `docs/development.md` before proposing
a change. Complete the pull request template and explain any check that does
not apply to the proposed change.

## Contributions and licensing

Unless explicitly stated otherwise, a contribution intentionally submitted for
inclusion in Sanjaya is provided under the Apache License 2.0, as described in
Section 5 of that license.

By participating, contributors agree to follow `CODE_OF_CONDUCT.md`.
