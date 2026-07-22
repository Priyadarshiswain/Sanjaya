import { spawnSync } from "node:child_process";
import {
  accessSync,
  lstatSync,
  mkdtempSync,
  readFileSync,
  realpathSync,
  rmSync,
  statSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { handleDiagnosticMode } from "../bin/sanjaya-diagnostics.js";

const repositoryRoot = resolve(".");
const launcher = join(repositoryRoot, "bin", "sanjaya-mcp.js");

verifyProcess(["--help"], 0, "Usage:", "");
verifyProcess(["--version"], 0, "sanjaya-mcp 0.0.0-development", "");

const ready = verifyProcess(["--diagnose", "--root", repositoryRoot], 0, "Result: ready", "");
assertIncludes(ready.stdout, "[ok] dotnet_runtime_ready:");
assertIncludes(ready.stdout, "[ok] repository_root_ready:");
assertIncludes(ready.stdout, "[ok] typescript_worker_ready:");
assertIncludes(ready.stdout, "[ok] git_ready:");
assertPrivatePathAbsent(ready.stdout);

const missing = verifyProcess(["--diagnose"], 1, "repository_root_required", "");
assertIncludes(missing.stdout, "Result: not ready");
assertPrivatePathAbsent(missing.stdout);

const relative = verifyProcess(
  ["--diagnose", "--root", "relative/repository"],
  1,
  "repository_root_relative",
  "",
);
assertPrivatePathAbsent(relative.stdout);

const invalid = verifyProcess(
  ["--diagnose", "--unknown"],
  1,
  "arguments_invalid",
  "",
);
assertPrivatePathAbsent(invalid.stdout);

const fakeRuntime = {
  access: accessSync,
  environment: process.env,
  lstat: lstatSync,
  nodeVersion: "22.13.0",
  readText: path => readFileSync(path, "utf8"),
  realpath: realpathSync,
  spawn: command => command === "dotnet"
    ? { status: 0, stdout: "Microsoft.NETCore.App 9.0.0 [runtime]", stderr: "" }
    : { status: 1, stdout: "", stderr: "" },
  stat: statSync,
};
const wrongDotnet = handleDiagnosticMode(
  ["--diagnose", "--root", repositoryRoot],
  repositoryRoot,
  fakeRuntime,
);
if (!wrongDotnet.handled || wrongDotnet.exitCode !== 1) {
  throw new Error("Diagnostic mode accepted a runtime without .NET 8.");
}
assertIncludes(wrongDotnet.stdout, "dotnet_8_missing");
assertPrivatePathAbsent(wrongDotnet.stdout);

const emptyPath = mkdtempSync(join(canonicalTemporaryRoot(), "sanjaya-empty-path-"));
try {
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(([key]) => key.toUpperCase() !== "PATH"),
  );
  environment.PATH = emptyPath;
  const unavailable = spawnSync(process.execPath, [launcher, "--root", repositoryRoot], {
    cwd: repositoryRoot,
    encoding: "utf8",
    env: environment,
    timeout: 10_000,
    windowsHide: true,
  });
  if (unavailable.status !== 1) {
    throw new Error("Normal launcher startup did not reject a missing .NET host.");
  }
  assertIncludes(unavailable.stderr, "dotnet_unavailable");
  assertPrivatePathAbsent(unavailable.stderr);
} finally {
  rmSync(emptyPath, { recursive: true, force: true });
}

console.log("Launcher help, version, diagnostics, privacy, and runtime failures verified.");

function verifyProcess(argumentsToPass, expectedStatus, stdoutFragment, stderrFragment) {
  const result = spawnSync(process.execPath, [launcher, ...argumentsToPass], {
    cwd: repositoryRoot,
    encoding: "utf8",
    timeout: 15_000,
    windowsHide: true,
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== expectedStatus) {
    throw new Error(`Launcher exited with ${result.status}; expected ${expectedStatus}. ${result.stderr}`);
  }
  assertIncludes(result.stdout, stdoutFragment);
  if (result.stderr !== stderrFragment) {
    throw new Error(`Launcher wrote unexpected stderr: ${result.stderr}`);
  }
  return result;
}

function assertIncludes(value, expected) {
  if (!value.includes(expected)) {
    throw new Error(`Expected launcher output to contain ${expected}.`);
  }
}

function assertPrivatePathAbsent(value) {
  if (value.includes(repositoryRoot)) {
    throw new Error("Launcher diagnostics exposed the absolute repository root.");
  }
}

function canonicalTemporaryRoot() {
  const root = resolve(tmpdir());
  return process.platform === "darwin" && root.startsWith("/var/")
    ? `/private${root}`
    : root;
}
