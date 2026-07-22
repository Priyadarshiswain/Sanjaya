# VS Code integration

## Current status

Sanjaya has an exact `0.1.0` install configuration candidate, but the package
has not been published. There is therefore no working VS Code installation
button yet. The public link remains deliberately inactive until the exact npm
release exists and passes installed-package verification.

This avoids sending a first-time user to a package or version that cannot be
installed.

## Intended single-folder experience

After release verification, the Sanjaya documentation can provide a native VS
Code MCP installation link. VS Code will show the proposed stdio server
configuration and ask the user to trust it before startup. The reviewed
configuration will:

- invoke `npx` with one exact immutable `sanjaya-mcp` version;
- pass `--root` and `${workspaceFolder}` as separate arguments;
- contain no credential, environment override, shell expression, network
  permission, or filesystem grant; and
- live in the selected VS Code user profile so it is available across normal
  workspaces.

This uses VS Code's native
[MCP installation and management](https://code.visualstudio.com/docs/agent-customization/mcp-servers)
instead of a Sanjaya-specific installer.

When the user opens a different single-folder project, VS Code substitutes
that folder and launches a separate Sanjaya process. Each process retains one
immutable repository root. Sanjaya does not remember the previous project,
guess from the working directory, or rewrite editor configuration.

Node.js 22.13 or newer and the .NET 8 runtime are prerequisites. Git is
optional and is required only for `recent_changes`. Acquiring the npm package
can contact the npm registry; after acquisition, Sanjaya's default runtime
contains no network operation.

## Trust, removal, and diagnostics

Users should review the publisher, exact package version, command, and
workspace-root argument in VS Code's MCP trust prompt before starting Sanjaya.
Sanjaya does not bypass that confirmation. A local MCP server is executable
code, and the portable configuration does not claim to be an operating-system
sandbox; Sanjaya's repository containment remains an application boundary.

VS Code's **MCP: List Servers** command can show, stop, restart, or inspect the
configured server. Users can remove it through VS Code's installed MCP-server
UI or user-profile MCP configuration. The MCP output log is the first place to
inspect a startup failure. Sanjaya's
`--diagnose --root <absolute-path>` mode then checks Node.js, .NET 8, packaged
files, repository readiness, the TypeScript worker, and optional Git without
starting MCP or printing the repository path.

## Remote environments

A VS Code user-profile MCP server runs on the local machine. A repository
opened through Remote SSH, WSL, or a Dev Container may exist only in the remote
environment, so a local Sanjaya process cannot safely assume that path is
accessible. Configure Sanjaya in VS Code's remote user or workspace MCP
configuration and install its prerequisites in that same environment. VS Code
documents this distinction in its
[MCP configuration reference](https://code.visualstudio.com/docs/agents/reference/mcp-configuration).

## Multi-root workspaces

The v0.1 install-once contract targets a normal single-folder workspace.
Unqualified `${workspaceFolder}` is not an honest automatic selection policy
when a workspace contains several independent roots.

For a multi-root workspace, define one explicitly named Sanjaya server per
repository and use VS Code's folder-qualified workspace variables. Each server
still owns exactly one immutable root. Automatic process orchestration, active
editor switching, and one process serving multiple roots are deferred.

No VS Code extension is planned unless future editor-specific behavior cannot
be provided safely by VS Code's native MCP configuration.
