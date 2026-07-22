import { spawnSync } from "node:child_process";
import { constants, accessSync, lstatSync, readFileSync, realpathSync, statSync } from "node:fs";
import { isAbsolute, join, resolve } from "node:path";

const minimumNodeVersion = Object.freeze([22, 13, 0]);
const maximumToolOutputBytes = 256 * 1024;
const toolTimeoutMilliseconds = 5_000;

export function handleDiagnosticMode(argumentsToHandle, packageRoot, runtime = defaultRuntime()) {
  if (argumentsToHandle[0] === "--help") {
    return argumentsToHandle.length === 1
      ? handled(0, helpText())
      : handled(1, "", "Sanjaya help does not accept additional arguments (arguments_invalid).\n");
  }

  if (argumentsToHandle[0] === "--version") {
    if (argumentsToHandle.length !== 1) {
      return handled(1, "", "Sanjaya version does not accept additional arguments (arguments_invalid).\n");
    }

    const version = readPackageVersion(packageRoot, runtime);
    return version.ok
      ? handled(0, `sanjaya-mcp ${version.value}\n`)
      : handled(1, "", "Sanjaya package metadata is unavailable (package_metadata_missing).\n");
  }

  if (argumentsToHandle[0] !== "--diagnose") {
    return { handled: false };
  }

  return diagnose(argumentsToHandle.slice(1), packageRoot, runtime);
}

export function checkMcpPrerequisites(packageRoot, runtime = defaultRuntime()) {
  const node = checkNode(runtime.nodeVersion);
  if (node.status === "error") {
    return prerequisiteFailure(node);
  }

  const payload = checkRegularFile(
    join(packageRoot, "dist", "dotnet", "Sanjaya.Server.dll"),
    "package_payload_missing",
    "The packaged .NET server assembly is missing.",
    runtime,
  );
  if (payload.status === "error") {
    return prerequisiteFailure(payload);
  }

  const dotnet = checkDotnet(runtime);
  if (dotnet.status === "error") {
    return prerequisiteFailure(dotnet);
  }

  return { ready: true };
}

function diagnose(argumentsToCheck, packageRoot, runtime) {
  const parsed = parseDiagnosticArguments(argumentsToCheck);
  const checks = [checkNode(runtime.nodeVersion)];

  checks.push(checkRegularFile(
    join(packageRoot, "dist", "dotnet", "Sanjaya.Server.dll"),
    "package_payload_missing",
    "The packaged .NET server assembly is missing.",
    runtime,
  ));
  checks.push(checkDotnet(runtime));

  if (!parsed.ok) {
    checks.push(errorCheck(
      "arguments_invalid",
      "Diagnostic arguments are invalid.",
      "Use sanjaya-mcp --diagnose --root <absolute-path>."));
  }

  const repository = parsed.ok
    ? checkRepository(parsed.root, runtime)
    : { check: warningCheck("repository_not_checked", "Repository readiness was not checked."), root: null };
  checks.push(repository.check);

  checks.push(checkBundledTypeScript(packageRoot, runtime));
  checks.push(checkGit(repository.root, runtime));

  const ready = checks.every(check => check.status !== "error");
  const lines = ["Sanjaya diagnostics"];
  for (const check of checks) {
    lines.push(`[${check.status}] ${check.code}: ${check.message}`);
    if (check.remediation) {
      lines.push(`  ${check.remediation}`);
    }
  }
  lines.push(`Result: ${ready ? "ready" : "not ready"}`);

  return handled(ready ? 0 : 1, `${lines.join("\n")}\n`);
}

function parseDiagnosticArguments(argumentsToCheck) {
  if (argumentsToCheck.length === 2
      && argumentsToCheck[0] === "--root"
      && typeof argumentsToCheck[1] === "string"
      && argumentsToCheck[1].trim().length > 0) {
    return { ok: true, root: argumentsToCheck[1] };
  }

  if (argumentsToCheck.length === 0) {
    return { ok: true, root: null };
  }

  return { ok: false, root: null };
}

function checkNode(version) {
  const parsed = version.split(".").map(part => Number.parseInt(part, 10));
  if (parsed.length < 3 || parsed.some(part => !Number.isInteger(part))) {
    return errorCheck(
      "node_runtime_invalid",
      "The active Node.js version could not be verified.",
      "Install Node.js 22.13 or newer.");
  }

  for (let index = 0; index < minimumNodeVersion.length; index++) {
    if (parsed[index] > minimumNodeVersion[index]) {
      return okCheck("node_runtime_ready", "Node.js satisfies the 22.13 or newer requirement.");
    }
    if (parsed[index] < minimumNodeVersion[index]) {
      return errorCheck(
        "node_runtime_unsupported",
        "Node.js 22.13 or newer is required.",
        "Install a supported Node.js runtime and retry.");
    }
  }

  return okCheck("node_runtime_ready", "Node.js satisfies the 22.13 or newer requirement.");
}

function checkDotnet(runtime) {
  const result = runtime.spawn("dotnet", ["--list-runtimes"], toolOptions(runtime));
  if (result.error?.code === "ENOENT") {
    return errorCheck(
      "dotnet_unavailable",
      ".NET is not installed or is not available on PATH.",
      "Install the .NET 8 runtime from https://dotnet.microsoft.com/download/dotnet/8.0.");
  }
  if (result.error || result.status !== 0 || typeof result.stdout !== "string") {
    return errorCheck(
      "dotnet_check_failed",
      "The installed .NET runtimes could not be verified.",
      "Run dotnet --list-runtimes, then install or repair the .NET 8 runtime.");
  }

  const ready = result.stdout.split(/\r?\n/u)
    .some(line => /^Microsoft\.NETCore\.App 8\./u.test(line));
  return ready
    ? okCheck("dotnet_runtime_ready", "The .NET 8 runtime is available.")
    : errorCheck(
        "dotnet_8_missing",
        "The .NET host is available, but the .NET 8 runtime is missing.",
        "Install the .NET 8 runtime from https://dotnet.microsoft.com/download/dotnet/8.0.");
}

function checkRepository(configuredRoot, runtime) {
  if (!configuredRoot) {
    return {
      check: errorCheck(
        "repository_root_required",
        "No repository root was configured.",
        "Use --root <absolute-path>."),
      root: null,
    };
  }
  if (!isAbsolute(configuredRoot)) {
    return {
      check: errorCheck(
        "repository_root_relative",
        "The repository root is not an absolute path.",
        "Use --root <absolute-path>."),
      root: null,
    };
  }

  try {
    const metadata = runtime.stat(configuredRoot);
    if (!metadata.isDirectory()) {
      return {
        check: errorCheck(
          "repository_root_not_directory",
          "The repository root is not a directory.",
          "Choose a repository directory."),
        root: null,
      };
    }
    runtime.access(configuredRoot, constants.R_OK);
    const canonicalRoot = runtime.realpath(configuredRoot);
    return {
      check: okCheck("repository_root_ready", "The repository root exists and is readable."),
      root: canonicalRoot,
    };
  } catch (error) {
    const code = error?.code === "ENOENT"
      ? "repository_root_not_found"
      : "repository_root_inaccessible";
    const message = code === "repository_root_not_found"
      ? "The repository root does not exist."
      : "The repository root is inaccessible.";
    return {
      check: errorCheck(code, message, "Choose an existing readable repository directory."),
      root: null,
    };
  }
}

function checkBundledTypeScript(packageRoot, runtime) {
  const worker = join(packageRoot, "dist", "dotnet", "runtime", "typescript", "typescript-worker.mjs");
  const compiler = join(
    packageRoot,
    "dist",
    "dotnet",
    "third_party",
    "typescript",
    "package",
    "lib",
    "typescript.js",
  );
  if (!isRegularFile(worker, runtime) || !isRegularFile(compiler, runtime)) {
    return errorCheck(
      "typescript_worker_missing",
      "The bundled TypeScript worker or compiler is missing.",
      "Reinstall the reviewed Sanjaya package.");
  }

  return okCheck("typescript_worker_ready", "The bundled TypeScript worker payload is present.");
}

function checkGit(repositoryRoot, runtime) {
  if (!repositoryRoot) {
    return warningCheck(
      "git_not_checked",
      "Optional Git readiness was not checked because the repository root is unavailable.");
  }

  const result = runtime.spawn(
    "git",
    [
      "--no-pager",
      "-c", "color.ui=false",
      "-c", "core.fsmonitor=false",
      "rev-parse", "--show-toplevel",
    ],
    {
      ...toolOptions(runtime),
      cwd: repositoryRoot,
      env: gitEnvironment(runtime.environment),
    },
  );
  if (result.error?.code === "ENOENT") {
    return warningCheck(
      "git_unavailable",
      "Git is unavailable; code discovery works, but recent_changes does not.");
  }
  if (result.error || result.status !== 0 || typeof result.stdout !== "string") {
    return warningCheck(
      "not_git_repository",
      "The configured root is not a readable Git worktree root; code discovery remains available.");
  }

  try {
    const reportedRoot = runtime.realpath(result.stdout.trim());
    const equal = process.platform === "win32"
      ? reportedRoot.toLowerCase() === repositoryRoot.toLowerCase()
      : reportedRoot === repositoryRoot;
    return equal
      ? okCheck("git_ready", "Git is ready for optional local change evidence.")
      : warningCheck(
          "git_root_mismatch",
          "The configured root is nested inside a Git worktree; recent_changes requires the worktree root.");
  } catch {
    return warningCheck(
      "git_check_failed",
      "Git worktree readiness could not be verified; code discovery remains available.");
  }
}

function readPackageVersion(packageRoot, runtime) {
  try {
    const document = JSON.parse(runtime.readText(join(packageRoot, "package.json")));
    return typeof document.version === "string" && document.version.length > 0
      ? { ok: true, value: document.version }
      : { ok: false };
  } catch {
    return { ok: false };
  }
}

function checkRegularFile(path, code, message, runtime) {
  return isRegularFile(path, runtime)
    ? okCheck("package_payload_ready", "The packaged .NET server assembly is present.")
    : errorCheck(code, message, "Reinstall the reviewed Sanjaya package.");
}

function isRegularFile(path, runtime) {
  try {
    const metadata = runtime.lstat(path);
    return metadata.isFile() && !metadata.isSymbolicLink();
  } catch {
    return false;
  }
}

function toolOptions(runtime) {
  return {
    encoding: "utf8",
    env: runtime.environment,
    maxBuffer: maximumToolOutputBytes,
    timeout: toolTimeoutMilliseconds,
    windowsHide: true,
  };
}

function gitEnvironment(environment) {
  const result = {};
  for (const [key, value] of Object.entries(environment)) {
    if (!key.toUpperCase().startsWith("GIT_")) {
      result[key] = value;
    }
  }
  return {
    ...result,
    GIT_CONFIG_GLOBAL: process.platform === "win32" ? "NUL" : "/dev/null",
    GIT_CONFIG_NOSYSTEM: "1",
    GIT_OPTIONAL_LOCKS: "0",
    GIT_PAGER: "cat",
    GIT_TERMINAL_PROMPT: "0",
  };
}

function prerequisiteFailure(check) {
  return {
    ready: false,
    message: `${check.message} (${check.code})\n${check.remediation}\nRun sanjaya-mcp --diagnose --root <absolute-path> for the complete readiness report.`,
  };
}

function helpText() {
  return `Sanjaya — local-first codebase discovery for AI agents

Usage:
  sanjaya-mcp --root <absolute-path>
  sanjaya-mcp --diagnose --root <absolute-path>
  sanjaya-mcp --help
  sanjaya-mcp --version

Normal startup uses MCP over stdio. Configure exactly one immutable repository
root per process. --diagnose performs local read-only readiness checks and does
not start the MCP server, read source files, write an index, or use the network.
`;
}

function okCheck(code, message) {
  return { status: "ok", code, message };
}

function warningCheck(code, message, remediation = null) {
  return { status: "warning", code, message, remediation };
}

function errorCheck(code, message, remediation) {
  return { status: "error", code, message, remediation };
}

function handled(exitCode, stdout = "", stderr = "") {
  return { handled: true, exitCode, stdout, stderr };
}

function defaultRuntime() {
  return {
    access: accessSync,
    environment: process.env,
    lstat: lstatSync,
    nodeVersion: process.versions.node,
    readText: path => readFileSync(path, "utf8"),
    realpath: realpathSync,
    spawn: spawnSync,
    stat: statSync,
  };
}
