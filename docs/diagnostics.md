# First-run diagnostics

Sanjaya separates human setup diagnostics from MCP stdio. The launcher handles
three non-MCP commands before starting the .NET server:

```text
sanjaya-mcp --help
sanjaya-mcp --version
sanjaya-mcp --diagnose --root <absolute-path>
```

These commands are part of the published and independently verified `0.1.0`
package. Check one repository before adding Sanjaya to an MCP client:

```bash
npx -y sanjaya-mcp@0.1.0 --diagnose --root /absolute/path/to/repository
```

## Readiness checks

`--diagnose` verifies:

- Node.js 22.13 or newer
- the packaged .NET server assembly
- an installed .NET 8 runtime
- one absolute, existing, readable repository directory
- the bundled TypeScript worker and compiler payload
- optional Git availability and worktree-root alignment

Each line has an `ok`, `warning`, or `error` state and a stable machine-readable
code. Errors include generic remediation. Required errors produce
`Result: not ready` and exit code 1. Git is optional, so a Git warning can still
produce `Result: ready` and exit code 0.

The diagnostic is deliberately shallow. It does not start the MCP server, read
source files, write `.sanjaya/`, load a project, execute repository content, or
use the network. It does not print the configured absolute root, raw subprocess
stderr, credentials, or environment values.

## MCP behavior after startup

Normal startup reserves stdout for JSON-RPC. Missing or malformed root
configuration does not prevent MCP initialization, `capabilities`, or
`health_check`; those tools return the same precise root reason and remediation.
Repository discovery remains unavailable until the client restarts one Sanjaya
process with exactly one valid `--root <absolute-path>` argument.

Sanjaya does not guess from the current directory, prompt interactively, switch
roots in a running process, or manage multiple roots in v0.1. MCP-client and
editor-specific workspace wiring is a separate follow-up.
