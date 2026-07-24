---
name: evidence-first-code-discovery
description: Discover and explain unfamiliar codebases with verifiable repository-relative evidence. Use when locating implementations, declarations, candidate references, structure, recent changes, or code evidence for an explanation, review, or planned edit; choose capability-fitting Sanjaya MCP tools when available and fall back to native exact search, file reading, and read-only Git when they are not.
---

# Evidence-First Code Discovery

Ground material codebase claims in the smallest sufficient set of
repository-relative evidence. Use Sanjaya selectively; availability does not
make it the right tool for every lookup.

## Discover

1. Identify the claims the answer must support before searching.
2. Skip broad discovery when the exact file and relevant range are already
   supplied or the necessary source is already in context.
3. When Sanjaya is available and its capabilities are not established for the
   current repository session, call `capabilities` once. Reuse the result.
4. Treat the live capability response as authoritative. Do not infer support
   from the language, package version, or presence of another provider.
5. Choose one primary route from the table below.

| Evidence need | Primary route | Constraint |
|---|---|---|
| Exact known text or identifier | `search_text` or native exact search | Try one; do not duplicate a successful lookup |
| Structure of a known file | `file_outline` | Read natively when only a small known range is needed |
| Broad lookup over supported source | `search_code` | Use only with an existing current index |
| Exact C# declaration | `find_definition` | Preserve `ambiguous` results |
| C# identifier usages | `find_references` | Report syntax candidates, never semantic bindings |
| Exact C# declaration source | `get_source` | Use a chunk ID returned by indexed discovery |
| Recent local Git evidence | `recent_changes` | Separate committed and working-tree observations |
| Unsupported or unavailable operation | Native exact search, file reading, or read-only Git | State the fallback only when it affects confidence |

Use Sanjaya tool names as exposed by the client; an MCP client may add a
server-specific prefix.

## Control side effects

Treat discovery as read-only except for explicitly approved indexing.
`index_codebase` writes `.sanjaya/index-v1.json` in the selected repository.

- Do not create or rebuild an index silently.
- When an index can plausibly repay its cost across broad or repeated
  discovery, explain the repository-local write and ask for approval before
  calling `index_codebase`.
- On `index_missing`, `index_stale`, `index_invalid`, or incompatibility, do not
  trigger a rebuild automatically.
- Prefer immediate discovery or native tools for one-off exact lookups.

Do not treat this skill invocation as permission to install software, contact
the network, edit source, or perform other repository mutations.

## Verify

Inspect only enough source to validate the intended claim.

- Retrieve the smallest useful source window.
- Use a second route only to resolve ambiguity, recover from a bounded or
  unsupported result, or verify a materially consequential claim.
- Keep declaration evidence distinct from usage evidence.
- Preserve `partial`, truncation, recovered-syntax, and ambiguity states.
- Treat every `find_references` result as a `syntax_candidate`; do not claim
  overload, alias, inheritance, or compiler-level binding certainty.
- Use `health_check` only for setup questions or after a reported readiness
  problem. Do not call it after successful discovery.

On a stable failure:

1. Preserve the reason and any usable evidence.
2. Do not retry the same call unchanged.
3. Select a native read-only fallback when it can answer the question.
4. Mention the limitation only when it affects the answer or setup.

Remain useful when Sanjaya is absent by following the same evidence-first
workflow with native tools.

## Control cost

Begin with one targeted discovery call. After three Sanjaya discovery calls,
reassess whether another call is necessary. Treat three as a review point, not
a hard limit; continue only when unresolved ambiguity or the requested depth
justifies more evidence.

- Do not repeat `capabilities` in the same repository session.
- Do not run the same successful lookup through both Sanjaya and native tools.
- Do not broaden a precise result without a claim-driven reason.
- Do not enumerate every match when a bounded unique answer is sufficient.
- Stop when every material claim has adequate evidence.

## Report

Lead with the answer. Support material codebase claims using
repository-relative paths and one-based line ranges.

Distinguish:

- observed evidence;
- inference derived from that evidence; and
- unavailable or unsupported information.

Never expose an absolute repository path. Do not upgrade partial evidence,
syntax candidates, or ambiguity into certainty. Keep caveats concise and
include them only when they change how the answer should be interpreted.
