import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import {
  copyFileSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { approvedPackageFiles, assertEqual, verifyPackedFiles } from "./package-contract.mjs";
import {
  assertReleasePackage,
  publicationState,
  releaseArtifactDirectory,
  releaseTag,
  releaseTarballName,
  releaseVersion,
} from "./release-contract.mjs";

const npmCli = process.env.npm_execpath;
if (!npmCli) {
  throw new Error("npm_execpath is unavailable; run this build through npm.");
}

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const outputRoot = resolve(repositoryRoot, releaseArtifactDirectory);
const expectedOutputRoot = resolve(repositoryRoot, "dist", "release");
if (outputRoot !== expectedOutputRoot) {
  throw new Error("The release output escaped its reviewed directory.");
}

rejectSymlink(resolve(repositoryRoot, "dist"));
rejectSymlinksRecursively(outputRoot);
rmSync(outputRoot, { recursive: true, force: true });
mkdirSync(outputRoot, { recursive: true });

const temporaryRoot = mkdtempSync(join(canonicalTemporaryRoot(), "sanjaya-release-candidate-"));
try {
  runNpm("run", "verify:reproducible-package");
  runNpm("run", "verify:package");

  const pack = spawnSync(process.execPath, [
    npmCli,
    "pack",
    "--json",
    "--ignore-scripts",
    "--pack-destination",
    temporaryRoot,
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
    throw new Error(`npm pack failed for the release candidate: ${pack.stderr.trim()}`);
  }

  const reports = JSON.parse(pack.stdout);
  if (!Array.isArray(reports) || reports.length !== 1) {
    throw new Error("npm pack returned an unexpected release-candidate report.");
  }
  const report = reports[0];
  assertEqual(
    report.files.map((file) => file.path).sort(),
    approvedPackageFiles,
    "The release-candidate tarball drifted from the exact package allowlist.",
  );
  if (report.filename !== releaseTarballName) {
    throw new Error(`npm produced ${report.filename}; expected ${releaseTarballName}.`);
  }

  const packageDocument = JSON.parse(readFileSync(join(repositoryRoot, "package.json"), "utf8"));
  assertReleasePackage(packageDocument);
  const tarball = readFileSync(join(temporaryRoot, report.filename));
  const sha256 = createHash("sha256").update(tarball).digest("hex");
  const sha512 = createHash("sha512").update(tarball).digest("hex");
  const sourceCommit = runGit("rev-parse", "HEAD").trim();
  const sourceTreeClean = runGit("status", "--porcelain", "--untracked-files=all").trim() === "";
  const manifest = verifyPackedFiles(
    repositoryRoot,
    report.files.map((file) => file.path).sort(),
  );

  copyFileSync(join(temporaryRoot, report.filename), join(outputRoot, releaseTarballName));
  writeJson(join(outputRoot, "manifest.json"), manifest);
  writeJson(join(outputRoot, "candidate.json"), {
    schemaVersion: 1,
    package: packageDocument.name,
    version: releaseVersion,
    tag: releaseTag,
    publicationState,
    sourceCommit,
    sourceTreeClean,
    artifact: releaseTarballName,
    bytes: tarball.length,
    sha256,
    sha512,
    npmIntegrity: report.integrity,
    npmShasum: report.shasum,
    manifest: "manifest.json",
  });
  writeFileSync(join(outputRoot, "SHA256SUMS"), `${sha256}  ${releaseTarballName}\n`);
  writeFileSync(join(outputRoot, "SHA512SUMS"), `${sha512}  ${releaseTarballName}\n`);

  console.log(`Built ${releaseTarballName} with SHA-256 ${sha256}.`);
  console.log(`Release evidence: ${relative(repositoryRoot, outputRoot).split(sep).join("/")}`);
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}

function runNpm(...argumentsToPass) {
  const result = spawnSync(process.execPath, [npmCli, ...argumentsToPass], {
    cwd: repositoryRoot,
    env: process.env,
    stdio: "inherit",
    windowsHide: true,
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`npm ${argumentsToPass.join(" ")} failed with exit code ${result.status}.`);
  }
}

function runGit(...argumentsToPass) {
  const result = spawnSync("git", argumentsToPass, {
    cwd: repositoryRoot,
    encoding: "utf8",
    windowsHide: true,
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`git ${argumentsToPass.join(" ")} failed: ${result.stderr.trim()}`);
  }
  return result.stdout;
}

function writeJson(path, value) {
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`);
}

function rejectSymlink(path) {
  try {
    if (lstatSync(path).isSymbolicLink()) {
      throw new Error(`Release output boundary must not be a symlink: ${path}`);
    }
  } catch (error) {
    if (error?.code !== "ENOENT") {
      throw error;
    }
  }
}

function rejectSymlinksRecursively(root) {
  try {
    const metadata = lstatSync(root);
    if (metadata.isSymbolicLink()) {
      throw new Error(`Release output contains a symlink: ${root}`);
    }
    if (!metadata.isDirectory()) {
      throw new Error(`Release output is not a directory: ${root}`);
    }
  } catch (error) {
    if (error?.code === "ENOENT") {
      return;
    }
    throw error;
  }

  const pending = [root];
  while (pending.length > 0) {
    const current = pending.pop();
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const path = join(current, entry.name);
      if (entry.isSymbolicLink()) {
        throw new Error(`Release output contains a symlink: ${path}`);
      }
      if (entry.isDirectory()) {
        pending.push(path);
      }
    }
  }
}

function canonicalTemporaryRoot() {
  const root = resolve(tmpdir());
  return process.platform === "darwin" && root.startsWith("/var/")
    ? `/private${root}`
    : root;
}
