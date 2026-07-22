#!/usr/bin/env node

import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";
import {
  checkMcpPrerequisites,
  handleDiagnosticMode,
} from "./sanjaya-diagnostics.js";

const launcherDirectory = dirname(fileURLToPath(import.meta.url));
const packageRoot = resolve(launcherDirectory, "..");
const serverAssembly = resolve(
  packageRoot,
  "dist",
  "dotnet",
  "Sanjaya.Server.dll",
);

const argumentsToForward = process.argv.slice(2);
const diagnosticMode = handleDiagnosticMode(argumentsToForward, packageRoot);
if (diagnosticMode.handled) {
  process.stdout.write(diagnosticMode.stdout);
  process.stderr.write(diagnosticMode.stderr);
  process.exit(diagnosticMode.exitCode);
}

const prerequisites = checkMcpPrerequisites(packageRoot);
if (!prerequisites.ready) {
  console.error(prerequisites.message);
  process.exit(1);
}

// The npm entry point is a process launcher; MCP behavior remains in .NET.
const child = spawn("dotnet", [serverAssembly, ...argumentsToForward], {
  stdio: "inherit",
  windowsHide: true,
  env: {
    ...process.env,
    SANJAYA_NODE_EXECUTABLE: process.execPath,
  },
});

child.on("error", (error) => {
  if (error.code === "ENOENT") {
    console.error(
      ".NET became unavailable while Sanjaya was starting (dotnet_unavailable). Run sanjaya-mcp --diagnose --root <absolute-path>.",
    );
  } else {
    console.error(
      "Sanjaya could not start its .NET server (server_start_failed). Run sanjaya-mcp --diagnose --root <absolute-path>.",
    );
  }

  process.exitCode = 1;
});

child.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
    return;
  }

  process.exitCode = code ?? 1;
  if (process.exitCode !== 0) {
    console.error(
      "Sanjaya's .NET server exited unexpectedly (server_exit_failed). Run sanjaya-mcp --diagnose --root <absolute-path>.",
    );
  }
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => {
    if (!child.killed) {
      child.kill(signal);
    }
  });
}
