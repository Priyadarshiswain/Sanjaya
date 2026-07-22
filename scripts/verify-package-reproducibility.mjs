import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { tmpdir } from "node:os";
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
const temporaryRoot = mkdtempSync(join(canonicalTemporaryRoot(), "sanjaya-package-repro-"));

try {
  verifyRejectedBoundaryArgument();
  const first = createSnapshot("first");
  const second = createSnapshot("second");

  assertEqual(second.manifest, first.manifest, "Clean package builds produced different per-file SHA-256 manifests.");
  assertEqual(second.filePaths, first.filePaths, "Clean package builds produced different file paths.");
  if (second.integrity !== first.integrity
      || second.shasum !== first.shasum
      || second.tarballSha256 !== first.tarballSha256) {
    throw new Error("Repeated npm packs produced different integrity or tarball hashes.");
  }

  console.log(
    `Reproduced ${first.filePaths.length}-file npm package with integrity ${first.integrity}.`,
  );
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}

function verifyRejectedBoundaryArgument() {
  const canary = join(repositoryRoot, "dist", "dotnet", "rejected-argument-canary.txt");
  mkdirSync(join(repositoryRoot, "dist", "dotnet"), { recursive: true });
  writeFileSync(canary, "Rejected arguments must not clean package staging.\n");

  const rejected = spawnSync(process.execPath, [
    "scripts/build-package.mjs",
    "--runtime",
    "linux-x64",
  ], {
    cwd: repositoryRoot,
    encoding: "utf8",
    windowsHide: true,
  });
  if (rejected.error) {
    throw rejected.error;
  }
  if (rejected.status === 0 || !rejected.stderr.includes("outside the package allowlist")) {
    throw new Error("Package builder did not reject a release-boundary override.");
  }
  if (!existsSync(canary)) {
    throw new Error("Rejected package argument modified existing staging content.");
  }
}

function createSnapshot(label) {
  const staleFile = join(repositoryRoot, "dist", "dotnet", "stale-package-file.txt");
  mkdirSync(join(repositoryRoot, "dist", "dotnet"), { recursive: true });
  writeFileSync(staleFile, "This file must not survive a clean package build.\n");
  run(process.execPath, [
    "scripts/build-package.mjs",
    "--no-restore",
    "--disable-build-servers",
    "-m:1",
  ]);
  if (existsSync(staleFile)) {
    throw new Error("Clean package build retained a stale staging file.");
  }
  run(process.execPath, ["scripts/verify-package-contents.mjs"]);

  const destination = join(temporaryRoot, label);
  mkdirSync(destination);
  const pack = spawnSync(process.execPath, [
    npmCli,
    "pack",
    "--json",
    "--ignore-scripts",
    "--pack-destination",
    destination,
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
    throw new Error(`npm pack failed during the ${label} reproducibility pass: ${pack.stderr.trim()}`);
  }

  const reports = JSON.parse(pack.stdout);
  if (!Array.isArray(reports) || reports.length !== 1) {
    throw new Error(`npm pack returned an unexpected ${label} report.`);
  }
  const report = reports[0];
  const filePaths = report.files.map((file) => file.path).sort();
  assertEqual(filePaths, approvedPackageFiles, `The ${label} package drifted from the allowlist.`);
  const tarball = readFileSync(join(destination, report.filename));

  return {
    filePaths,
    integrity: report.integrity,
    shasum: report.shasum,
    tarballSha256: createHash("sha256").update(tarball).digest("hex"),
    manifest: verifyPackedFiles(repositoryRoot, filePaths),
  };
}

function run(command, argumentsToPass) {
  const result = spawnSync(command, argumentsToPass, {
    cwd: repositoryRoot,
    env: process.env,
    stdio: "inherit",
    windowsHide: true,
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`${argumentsToPass[0]} failed with exit code ${result.status}.`);
  }
}

function canonicalTemporaryRoot() {
  const root = resolve(tmpdir());
  return process.platform === "darwin" && root.startsWith("/var/")
    ? `/private${root}`
    : root;
}
