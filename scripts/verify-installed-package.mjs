import { spawnSync } from "node:child_process";
import { mkdtempSync, mkdirSync, readFileSync, readdirSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import {
  approvedPackageFiles,
  assertEqual,
  verifyPackedFiles,
} from "./package-contract.mjs";

const npmCli = process.env.npm_execpath;
if (!npmCli) {
  throw new Error("npm_execpath is unavailable; run this check through npm.");
}

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const temporaryRoot = mkdtempSync(join(canonicalTemporaryRoot(), "sanjaya-installed-package-"));
const artifactRoot = join(temporaryRoot, "artifact");
const consumerRoot = join(temporaryRoot, "consumer");
mkdirSync(artifactRoot);
mkdirSync(consumerRoot);

try {
  const report = packTarball();
  const tarballPath = join(artifactRoot, report.filename);
  installTarball(tarballPath);

  const rootPackage = JSON.parse(readFileSync(join(repositoryRoot, "package.json"), "utf8"));
  const installedRoot = join(consumerRoot, "node_modules", rootPackage.name);
  const installedFiles = listFiles(installedRoot);
  assertEqual(installedFiles, approvedPackageFiles, "Installed package contents differ from the packed allowlist.");
  verifyPackedFiles(installedRoot, installedFiles);

  const launcherPath = join(
    consumerRoot,
    "node_modules",
    ".bin",
    process.platform === "win32" ? "sanjaya-mcp.cmd" : "sanjaya-mcp",
  );
  verifyInstalledDiagnostic(
    launcherPath,
    ["--version"],
    0,
    `sanjaya-mcp ${rootPackage.version}`,
    repositoryRoot,
  );
  verifyInstalledDiagnostic(
    launcherPath,
    ["--diagnose", "--root", repositoryRoot],
    0,
    "Result: ready",
    repositoryRoot,
  );
  const verification = spawnSync(process.execPath, ["scripts/verify-mcp-launcher.mjs"], {
    cwd: repositoryRoot,
    env: {
      ...process.env,
      SANJAYA_VERIFY_LAUNCHER_PATH: launcherPath,
    },
    stdio: "inherit",
    windowsHide: true,
  });
  if (verification.error) {
    throw verification.error;
  }
  if (verification.status !== 0) {
    throw new Error(`Installed package MCP verification failed with exit code ${verification.status}.`);
  }

  console.log("Installed the local npm tarball offline and verified its diagnostics and MCP launcher.");
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}

function verifyInstalledDiagnostic(launcherPath, argumentsToPass, expectedStatus, expectedOutput, privatePath) {
  const result = spawnSync(launcherPath, argumentsToPass, {
    cwd: consumerRoot,
    encoding: "utf8",
    shell: process.platform === "win32",
    windowsHide: true,
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== expectedStatus || !result.stdout.includes(expectedOutput)) {
    throw new Error("Installed package diagnostic command did not satisfy its public contract.");
  }
  if (result.stderr !== "") {
    throw new Error("Installed package diagnostic command wrote unexpected stderr.");
  }
  if (result.stdout.includes(privatePath)) {
    throw new Error("Installed package diagnostics exposed the absolute repository root.");
  }
}

function packTarball() {
  const pack = spawnSync(process.execPath, [
    npmCli,
    "pack",
    "--json",
    "--ignore-scripts",
    "--pack-destination",
    artifactRoot,
    "--cache",
    join(temporaryRoot, "npm-cache"),
  ], {
    cwd: repositoryRoot,
    encoding: "utf8",
    windowsHide: true,
  });
  if (pack.error) {
    throw pack.error;
  }
  if (pack.status !== 0) {
    throw new Error(`npm pack failed for installed-package verification: ${pack.stderr.trim()}`);
  }

  const reports = JSON.parse(pack.stdout);
  if (!Array.isArray(reports) || reports.length !== 1) {
    throw new Error("npm pack returned an unexpected installed-package report.");
  }
  const report = reports[0];
  assertEqual(
    report.files.map((file) => file.path).sort(),
    approvedPackageFiles,
    "Installed-package tarball drifted from the exact allowlist.",
  );
  return report;
}

function installTarball(tarballPath) {
  const install = spawnSync(process.execPath, [
    npmCli,
    "install",
    "--prefix",
    consumerRoot,
    "--ignore-scripts",
    "--offline",
    "--no-audit",
    "--no-fund",
    "--package-lock=false",
    "--cache",
    join(temporaryRoot, "install-cache"),
    tarballPath,
  ], {
    cwd: consumerRoot,
    encoding: "utf8",
    windowsHide: true,
  });
  if (install.error) {
    throw install.error;
  }
  if (install.status !== 0) {
    throw new Error(`Offline tarball installation failed: ${install.stderr.trim()}`);
  }
}

function listFiles(root) {
  const files = [];
  const pending = [root];
  while (pending.length > 0) {
    const current = pending.pop();
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const path = join(current, entry.name);
      if (entry.isDirectory()) {
        pending.push(path);
      } else if (entry.isFile()) {
        files.push(relative(root, path).split(sep).join("/"));
      } else {
        throw new Error(`Installed package contains a non-regular entry: ${entry.name}`);
      }
    }
  }
  return files.sort();
}

function canonicalTemporaryRoot() {
  const root = resolve(tmpdir());
  return process.platform === "darwin" && root.startsWith("/var/")
    ? `/private${root}`
    : root;
}
