# Evidence-First skill distribution contract

Status: local skills-only plugin implemented for owner review. No marketplace
entry, installation, or skill publication has been created.

This document defines how the
[`evidence-first-code-discovery`](../plugins/sanjaya/skills/evidence-first-code-discovery/SKILL.md)
skill can become installable without confusing it with the separately published
Sanjaya MCP server. It is a release contract, not an active installation guide.

## Product boundary

Sanjaya has two independently useful parts:

- `sanjaya-mcp@0.1.2` provides local code-discovery capabilities over MCP; and
- `evidence-first-code-discovery` teaches an agent when to use available
  Sanjaya capabilities, when native repository tools are cheaper, and how to
  report evidence.

Installing either part must not silently install, enable, remove, or upgrade the
other. The skill remains useful without Sanjaya by following its native
read-only fallback. The MCP server remains useful without the skill when a user
or agent calls its tools directly.

The npm package remains the .NET MCP server launcher. It must not absorb the
skill or a Codex plugin as an undeclared payload change.

## Selected channels

### Canonical source

The local plugin directory is the only canonical skill source:

```text
plugins/
└── sanjaya/
    ├── .codex-plugin/
    │   └── plugin.json
    └── skills/
        └── evidence-first-code-discovery/
            ├── SKILL.md
            └── agents/
                └── openai.yaml
```

The implementation moved the two existing source files; it did not create a
second maintained copy. Repository links may let people inspect this source.
They must not describe the plugin as installed, marketplace-listed,
automatically active, or published. A Git marketplace entry may point to
`plugins/sanjaya/` only after separate publication approval.

### Primary Codex distribution

The preferred Codex distribution is a minimal skills-only plugin. OpenAI's
[plugin authoring documentation](https://learn.chatgpt.com/docs/build-plugins)
defines plugins as the installable unit for sharing stable skills and requires
a `.codex-plugin/plugin.json` manifest.

The local initial plugin contains only:

- one manifest;
- the existing two-file skill; and
- truthful presentation and license metadata.

It should not contain an MCP configuration, hook, connector, app, executable
script, network instruction, telemetry, generated index, or duplicated skill
source. Installing it should add an agent workflow, not start a process or
modify a repository.

### Other agent clients

For agents that support compatible skill folders, users may adopt the canonical
skill source using that client's documented local or repository-level
installation mechanism. Sanjaya should not claim one universal destination
path or automatic support across clients.

This source-only route must preserve `SKILL.md` and any declared metadata
together. Users who copy it manually own that copy and must replace or remove
it manually. It is a portability route, not a second package registry.

## Why MCP bundling is deferred

Codex plugins can reference an `.mcp.json` containing literal stdio `command`
and `args` values. The current
[bundled MCP server documentation](https://learn.chatgpt.com/docs/build-plugins#bundled-mcp-servers-and-lifecycle-hooks)
does not define a portable placeholder for the active project root in those
arguments.

Sanjaya intentionally requires one explicit absolute
`--root <path>` per process and never infers a repository from the process
working directory. A plugin with no root would expose only stable
`repository_root_required` failures. A wrapper that guesses the current
directory would weaken the reviewed containment and workspace-switching
contract.

Therefore the first plugin must not declare `mcpServers` or include
`.mcp.json`. Users configure `sanjaya-mcp@0.1.2` separately through an MCP
client that can bind an explicit workspace folder, such as the reviewed VS Code
configuration.

MCP bundling can be reconsidered only after the target plugin surface documents
and tests a portable active-project binding. The test must launch two isolated
projects, prove each process receives exactly one absolute root, and prove
neither process can discover the other project.

## Identity and versioning

The implemented local plugin identity is:

- plugin name: `sanjaya`;
- plugin version: `0.1.0`;
- bundled skill name: `evidence-first-code-discovery`;
- source repository: `Priyadarshiswain/Sanjaya`; and
- license: `Apache-2.0`.

Plugin versions and npm server versions are independent SemVer streams. Plugin
`0.1.0` may document compatibility with `sanjaya-mcp@0.1.2` or newer, but the
skill must continue to treat live capability reporting as authoritative.

Every plugin release requires a new immutable version and a reviewed Git tag or
commit. A branch name, floating npm tag, or `latest` is not release identity.

## Intended user lifecycle

The future reviewed lifecycle is:

1. add the Sanjaya Git marketplace source at an immutable reviewed release;
2. inspect the plugin identity, source, version, files, and requested
   capabilities;
3. install and enable the skills-only plugin;
4. configure the exact Sanjaya MCP package separately when MCP capabilities are
   wanted; and
5. verify in a new agent session that the skill is discoverable and that
   Sanjaya remains optional.

Upgrade should refresh the marketplace, show the new immutable plugin version,
and require the user to choose that update. Removing the plugin should remove
the skill workflow without removing MCP configuration or cached npm artifacts.
Removing the MCP configuration should stop Sanjaya without removing the skill.

Exact installation commands and clickable links remain intentionally absent
until a built plugin and marketplace entry have passed clean-machine
verification.

## Disposable marketplace verification

The repository verifier may construct one test-only marketplace under a newly
created operating-system temporary directory. The fixture contains the exact
three-file plugin plus `.agents/plugins/marketplace.json` with:

- marketplace name `sanjaya-local-test`;
- local source `./plugins/sanjaya`;
- installation policy `AVAILABLE`;
- authentication policy `ON_INSTALL`; and
- category `Developer Tools`.

The verifier compares every copied plugin file by path, byte length, and
SHA-256 digest, then removes the complete temporary directory. It fails if a
repository marketplace exists before or after the check.

This check does not invoke Codex, register a marketplace, install or enable the
plugin, mutate personal configuration, contact a network, or create a
publishable marketplace artifact. It proves only that a correctly shaped local
marketplace can reference the reviewed plugin without changing it.

## Clean-environment lifecycle verification

The [local lifecycle evidence](plugin-lifecycle.md) records a real Codex CLI
test inside a disposable Docker container. The verifier mounts only the
temporary marketplace read-only, installs the pinned CLI inside the container,
disconnects it from the network, and verifies:

- marketplace registration and uninstalled-plugin discovery;
- exact three-file installation and enabled state from a fresh process;
- update through the official plugin cachebuster helper;
- reinstall with removal of the previous version cache;
- plugin uninstall and cache removal;
- marketplace removal and empty final plugin configuration; and
- deletion of the container and temporary marketplace.

The test does not mount account credentials or host Codex state. Therefore it
does not claim an authenticated model selected the installed skill. The
noninteractive CLI also exposes no plugin disable command, while official
documentation places disable/re-enable in the interactive plugin browser. The
verifier records both checks as manual rather than editing Codex configuration
or fabricating an authenticated result.

Public marketplace creation remains blocked until interactive disable/re-enable
is reviewed separately and the preregistered Evidence-First skill evaluation
completes. That evaluation supplies the fresh authenticated agent invocation
while also measuring selective routing, correctness, citations, side effects,
tokens, and latency against a fresh native control.

## Trust and privacy review

Before installation, a user must be able to verify:

- the GitHub owner and repository;
- the immutable plugin version and source revision;
- that the first plugin contains instructions and metadata only;
- that it has no hooks, scripts, connectors, bundled MCP command, or network
  requirement;
- that `index_codebase` remains an explicit, approval-gated repository write;
  and
- that answers use repository-relative evidence and do not expose the absolute
  root.

A later MCP-backed plugin would require a new trust review of the exact command,
npm version, root binding, process prerequisites, tool approvals, and removal
behavior. Approval of a skills-only plugin does not pre-approve that expansion.

## Publication gates

The owner approved local implementation proposals through the fourth step.
Merging each proposal approves only its reviewed repository source and
verification evidence. The first three steps are complete; the fourth remains
partially verified. The fifth is now proposed as a separate contract and still
requires explicit run-budget approval. Every later step requires separate owner
approval and a separate pull request:

1. **Implemented locally:** move the canonical skill into a plugin directory
   and add the manifest.
2. **Implemented locally:** validate the local plugin without installing it
   into the owner's normal environment.
3. **Implemented locally:** create, verify, and remove a test-only marketplace
   in operating-system temporary state.
4. **Partially verified:** clean-environment discovery, install, cachebuster
   update, reinstall, removal, and cleanup pass. Interactive disable/re-enable
   remains a manual gate.
5. **Stage one complete:** the separately named `evidence_first_skill`
   evaluation retained 24 records against a fresh native control. The remaining
   48 runs stay blocked pending review and separate owner approval.
6. Add the public Git marketplace entry only if the reviewed evidence supports
   proceeding.
7. Publish or submit the plugin to any hosted directory.
8. Add active installation commands, badges, or links to public
   documentation.

The implementation PR must fail if it duplicates the canonical skill, changes
the npm package payload, silently adds MCP configuration, or creates a
repository-level `.agents/skills` copy that would auto-activate the skill for
contributors.

## Current local-only state

The local implementation consists only of:

- `plugins/sanjaya/.codex-plugin/plugin.json`;
- `plugins/sanjaya/skills/evidence-first-code-discovery/SKILL.md`; and
- `plugins/sanjaya/skills/evidence-first-code-discovery/agents/openai.yaml`.

It must not add `.mcp.json`, `.app.json`, hooks, scripts, assets, telemetry,
`.agents/plugins/marketplace.json`,
`.agents/skills/evidence-first-code-discovery`, an active marketplace command
or installation link, or a plugin publication or submission workflow.

The repository may contain the offline marketplace verifier under `scripts/`,
but that script is outside the plugin and npm payload. Its generated marketplace
exists only for the duration of the check.

The local plugin exists only as reviewed repository source. Approval of this
implementation does not authorize installation, marketplace publication,
hosted-directory submission, or MCP bundling.
