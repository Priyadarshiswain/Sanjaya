#!/usr/bin/env node

import { spawn } from "node:child_process";
import { existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const launcherDirectory = dirname(fileURLToPath(import.meta.url));
const serverAssembly = resolve(
  launcherDirectory,
  "..",
  "dist",
  "dotnet",
  "Sanjaya.Server.dll",
);

if (!existsSync(serverAssembly)) {
  console.error(
    "Sanjaya is not built. Run `npm run build` from the package source.",
  );
  process.exit(1);
}

// The npm entry point is a process launcher; MCP behavior remains in .NET.
const child = spawn("dotnet", [serverAssembly, ...process.argv.slice(2)], {
  stdio: "inherit",
  windowsHide: true,
});

child.on("error", (error) => {
  if (error.code === "ENOENT") {
    console.error(
      "Sanjaya requires .NET 8. Install it from https://dotnet.microsoft.com/download/dotnet/8.0",
    );
  } else {
    console.error(`Unable to start Sanjaya: ${error.message}`);
  }

  process.exitCode = 1;
});

child.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
    return;
  }

  process.exitCode = code ?? 1;
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => {
    if (!child.killed) {
      child.kill(signal);
    }
  });
}
