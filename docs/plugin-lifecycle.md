# Local plugin lifecycle verification

Status: the noninteractive Codex lifecycle is verified in a disposable,
network-isolated container. Interactive disable/re-enable and an authenticated
agent invocation remain unverified manual gates.

This document records how the local skills-only Sanjaya plugin is tested
without registering it in the owner's normal Codex environment or publishing a
marketplace.

## Isolation model

The verifier:

1. creates the reviewed `sanjaya-local-test` marketplace in operating-system
   temporary storage;
2. starts the pinned public
   `node:22-bookworm-slim` image by immutable digest;
3. mounts only that temporary marketplace at `/marketplace`, read-only;
4. installs `@openai/codex@0.144.5` inside the disposable container;
5. disconnects the container from the network and proves a network request
   fails before running any plugin command;
6. uses a fresh Codex process for every lifecycle observation; and
7. removes the plugin, marketplace registration, container, and temporary
   marketplace before succeeding.

No host Codex configuration, cache, credential, home directory, or repository
is mounted into the container. Docker may retain the public base-image layers
in its normal image cache, but all Codex and Sanjaya test state is deleted with
the container.

## Reproduce the automated check

Prerequisites are a running Docker engine, Node.js 22 or newer, Python 3, and
the `update_plugin_cachebuster.py` helper from Codex's `plugin-creator` skill.

Run:

```bash
npm run verify:plugin-lifecycle -- \
  --cachebuster-helper /absolute/path/to/update_plugin_cachebuster.py
```

The absolute helper path is an operator input and is not recorded in repository
files or test output. The helper changes only the disposable copy of the plugin
to `0.1.0+codex.lifecycle-test`; the canonical `0.1.0` manifest remains
unchanged.

The lifecycle verifier intentionally is not run in hosted CI because it depends
on a local Docker engine and a separately distributed Codex skill helper. CI
checks its JavaScript syntax and continues to run the offline marketplace,
plugin contract, npm payload, and cross-platform package tests.

## Results

The following checks passed on 2026-07-24 with Codex CLI `0.144.5`:

| Check | Result |
|---|---|
| Register the local marketplace | Passed |
| Discover uninstalled `sanjaya@0.1.0` | Passed |
| Install the exact three-file plugin | Passed |
| Observe installed and enabled state from a fresh Codex process | Passed |
| Compare installed paths, byte lengths, and SHA-256 digests | Passed |
| Apply the official cachebuster helper and reinstall | Passed |
| Remove the previous-version cache during reinstall | Passed |
| Uninstall the plugin and remove its cache | Passed |
| Remove the marketplace and leave empty plugin configuration | Passed |
| Delete the container and temporary marketplace | Passed |
| Disable and re-enable from the interactive plugin browser | Not automated |
| Invoke the skill in a fresh authenticated agent session | Not run |

The current noninteractive CLI exposes add, list, remove, and marketplace
commands, but no plugin disable command. The official
[plugin guide](https://learn.chatgpt.com/docs/plugins) documents disable and
enable as an interactive plugin-browser action. The test does not edit
`config.toml` by hand to manufacture a passing result.

The container deliberately receives no account credentials, so it verifies the
installed and enabled bundle but does not claim that an authenticated model
selected the skill. These two manual checks remain required before a public Git
marketplace entry or active installation instructions can be approved.
