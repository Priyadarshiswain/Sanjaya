import { createHash } from "node:crypto";
import { spawnSync } from "node:child_process";
import { readFileSync, readdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  approvedPackageFiles,
  assertEqual,
} from "./package-contract.mjs";
import {
  packageName,
  publicationState,
  releaseArtifactDirectory,
  releaseTag,
  releaseTarballName,
  releaseVersion,
} from "./release-contract.mjs";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const requireCleanSource = process.argv.includes("--require-clean");
const unexpectedArguments = process.argv.slice(2).filter((value) => value !== "--require-clean");
if (unexpectedArguments.length > 0) {
  throw new Error(`Unsupported release-candidate verifier argument: ${unexpectedArguments[0]}`);
}

const artifactRoot = resolve(repositoryRoot, releaseArtifactDirectory);
const expectedFiles = [
  "SHA256SUMS",
  "SHA512SUMS",
  "candidate.json",
  "manifest.json",
  releaseTarballName,
].sort();
const actualFiles = readdirSync(artifactRoot, { withFileTypes: true }).map((entry) => {
  if (!entry.isFile() || entry.isSymbolicLink()) {
    throw new Error(`Release evidence contains a non-regular entry: ${entry.name}`);
  }
  return entry.name;
}).sort();
if (JSON.stringify(actualFiles) !== JSON.stringify(expectedFiles)) {
  throw new Error("Release evidence files drifted from the exact allowlist.");
}

const evidence = JSON.parse(readFileSync(join(artifactRoot, "candidate.json"), "utf8"));
const manifest = JSON.parse(readFileSync(join(artifactRoot, "manifest.json"), "utf8"));
const tarball = readFileSync(join(artifactRoot, releaseTarballName));
const sha256 = createHash("sha256").update(tarball).digest("hex");
const sha512 = createHash("sha512").update(tarball).digest("hex");
const npmIntegrity = `sha512-${createHash("sha512").update(tarball).digest("base64")}`;
const npmShasum = createHash("sha1").update(tarball).digest("hex");
const expectedEvidence = {
  schemaVersion: 1,
  package: packageName,
  version: releaseVersion,
  tag: releaseTag,
  publicationState,
  artifact: releaseTarballName,
};
for (const [key, value] of Object.entries(expectedEvidence)) {
  if (evidence[key] !== value) {
    throw new Error(`Release evidence field ${key} does not match the reviewed contract.`);
  }
}
if (!/^[0-9a-f]{40}$/u.test(evidence.sourceCommit)) {
  throw new Error("Release evidence does not contain a full Git source commit.");
}
if (typeof evidence.sourceTreeClean !== "boolean") {
  throw new Error("Release evidence does not declare whether the source tree was clean.");
}
if (requireCleanSource && evidence.sourceTreeClean !== true) {
  throw new Error("Release publication requires evidence built from a clean source tree.");
}
if (requireCleanSource) {
  const git = spawnSync("git", ["rev-parse", "HEAD"], {
    cwd: repositoryRoot,
    encoding: "utf8",
    windowsHide: true,
  });
  if (git.error) {
    throw git.error;
  }
  if (git.status !== 0 || git.stdout.trim() !== evidence.sourceCommit) {
    throw new Error("Release evidence was not built from the checked-out source commit.");
  }
}
if (evidence.bytes !== tarball.length || evidence.sha256 !== sha256 || evidence.sha512 !== sha512) {
  throw new Error("Release tarball bytes or hashes disagree with candidate.json.");
}
if (evidence.npmIntegrity !== npmIntegrity || evidence.npmShasum !== npmShasum) {
  throw new Error("Release tarball npm integrity values disagree with candidate.json.");
}
if (evidence.manifest !== "manifest.json" || !Array.isArray(manifest)) {
  throw new Error("Release evidence contains an invalid package manifest.");
}
assertEqual(
  manifest.map((entry) => entry.path),
  approvedPackageFiles,
  "Release evidence manifest paths drifted from the exact package allowlist.",
);
for (const entry of manifest) {
  if (JSON.stringify(Object.keys(entry)) !== JSON.stringify(["path", "bytes", "sha256"])
      || !Number.isSafeInteger(entry.bytes)
      || entry.bytes < 0
      || !/^[0-9a-f]{64}$/u.test(entry.sha256)) {
    throw new Error(`Release evidence contains an invalid manifest entry: ${entry.path}`);
  }
}
if (readFileSync(join(artifactRoot, "SHA256SUMS"), "utf8") !== `${sha256}  ${releaseTarballName}\n`) {
  throw new Error("SHA256SUMS does not match the release tarball.");
}
if (readFileSync(join(artifactRoot, "SHA512SUMS"), "utf8") !== `${sha512}  ${releaseTarballName}\n`) {
  throw new Error("SHA512SUMS does not match the release tarball.");
}

console.log(`Verified ${packageName}@${releaseVersion} candidate ${sha256}.`);
