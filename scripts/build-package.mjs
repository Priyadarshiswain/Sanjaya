import { spawnSync } from "node:child_process";
import { existsSync, lstatSync, readdirSync, rmSync } from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const distributionRoot = join(repositoryRoot, "dist");
const outputRoot = join(distributionRoot, "dotnet");
const expectedRelativeOutput = ["dist", "dotnet"].join("/");
const actualRelativeOutput = relative(repositoryRoot, outputRoot).split(sep).join("/");

if (actualRelativeOutput !== expectedRelativeOutput) {
  throw new Error("Refusing to clean an unexpected package staging path.");
}

const forwardedArguments = process.argv.slice(2);
validateForwardedArguments(forwardedArguments);
rejectSymlink(distributionRoot);
rejectSymlinkTree(outputRoot);
rmSync(outputRoot, { recursive: true, force: true });

const publish = spawnSync("dotnet", [
  "publish",
  join("src", "Sanjaya.Server", "Sanjaya.Server.csproj"),
  ...forwardedArguments,
  "--configuration",
  "Release",
  "--framework",
  "net8.0",
  "--self-contained",
  "false",
  "--output",
  outputRoot,
  "-p:UseAppHost=false",
  "-p:CopyOutputSymbolsToPublishDirectory=false",
  "-p:SatelliteResourceLanguages=en",
], {
  cwd: repositoryRoot,
  stdio: "inherit",
  windowsHide: true,
});

if (publish.error) {
  throw publish.error;
}
if (publish.status !== 0) {
  throw new Error(`Package publish failed with exit code ${publish.status}.`);
}
removePortableSymbols(outputRoot);
if (!existsSync(join(outputRoot, "Sanjaya.Server.dll"))) {
  throw new Error("Package publish did not produce the portable server assembly.");
}

console.log("Created a clean portable .NET package payload in dist/dotnet.");

function rejectSymlink(path) {
  if (existsSync(path) && lstatSync(path).isSymbolicLink()) {
    throw new Error(`Refusing symlinked package staging path: ${relative(repositoryRoot, path)}`);
  }
}

function rejectSymlinkTree(root) {
  if (!existsSync(root)) {
    return;
  }

  rejectSymlink(root);
  const pending = [root];
  while (pending.length > 0) {
    const current = pending.pop();
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const fullPath = join(current, entry.name);
      if (entry.isSymbolicLink()) {
        throw new Error(`Refusing symlink inside package staging: ${relative(repositoryRoot, fullPath)}`);
      }
      if (entry.isDirectory()) {
        pending.push(fullPath);
      }
    }
  }
}

function validateForwardedArguments(argumentsToCheck) {
  const exactArguments = new Set(["--disable-build-servers", "--no-restore", "--nologo"]);
  const safeValueArguments = /^(?:-m(?::[1-9]\d*)?|--maxcpucount(?::[1-9]\d*)?|-v:(?:q|quiet|m|minimal|n|normal|d|detailed|diag|diagnostic)|--verbosity=(?:q|quiet|m|minimal|n|normal|d|detailed|diag|diagnostic))$/u;
  for (const argument of argumentsToCheck) {
    if (!exactArguments.has(argument) && !safeValueArguments.test(argument)) {
      throw new Error(`Build argument is outside the package allowlist: ${argument}`);
    }
  }
}

function removePortableSymbols(root) {
  const pending = [root];
  while (pending.length > 0) {
    const current = pending.pop();
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const fullPath = join(current, entry.name);
      if (entry.isSymbolicLink()) {
        throw new Error(`Publish produced a symlinked package entry: ${relative(repositoryRoot, fullPath)}`);
      }
      if (entry.isDirectory()) {
        pending.push(fullPath);
      } else if (entry.isFile() && entry.name.toLowerCase().endsWith(".pdb")) {
        rmSync(fullPath);
      } else if (!entry.isFile()) {
        throw new Error(`Publish produced a non-regular package entry: ${relative(repositoryRoot, fullPath)}`);
      }
    }
  }
}
