import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import {
  approvedPackageFiles,
  assertEqual,
  forbiddenLifecycleScripts,
  maximumCompressedBytes,
  maximumPackageEntries,
  maximumUnpackedBytes,
  verifyPackedFiles,
} from "./package-contract.mjs";

const npmCli = process.env.npm_execpath;
if (!npmCli) {
  throw new Error("npm_execpath is unavailable; run this check through npm.");
}

const repositoryRoot = process.cwd();
const rootPackage = JSON.parse(readFileSync("package.json", "utf8"));
if (rootPackage.private !== true || rootPackage.version !== "0.0.0-development") {
  throw new Error("Package release safety locks changed.");
}
for (const dependencyField of [
  "dependencies",
  "devDependencies",
  "optionalDependencies",
  "peerDependencies",
  "bundleDependencies",
  "bundledDependencies",
]) {
  if (rootPackage[dependencyField]) {
    throw new Error(`The Sanjaya npm launcher package must not declare ${dependencyField}.`);
  }
}
for (const script of forbiddenLifecycleScripts) {
  if (rootPackage.scripts?.[script]) {
    throw new Error(`The package must not define the ${script} lifecycle script.`);
  }
}

const pack = spawnSync(
  process.execPath,
  [npmCli, "pack", "--dry-run", "--json", "--ignore-scripts", "--cache", ".npm-cache"],
  { cwd: repositoryRoot, encoding: "utf8", windowsHide: true },
);
if (pack.error) {
  throw pack.error;
}
if (pack.status !== 0) {
  throw new Error(`npm package dry run failed: ${pack.stderr.trim()}`);
}

const reports = JSON.parse(pack.stdout);
if (!Array.isArray(reports) || reports.length !== 1) {
  throw new Error("npm pack returned an unexpected report count.");
}
const report = reports[0];
const actualFiles = report.files.map((file) => file.path).sort();
assertEqual(actualFiles, approvedPackageFiles, "npm package contents drifted from the exact allowlist.");
if (report.entryCount !== approvedPackageFiles.length || report.entryCount > maximumPackageEntries) {
  throw new Error(`npm package entry count exceeded the ${maximumPackageEntries}-entry review ceiling.`);
}
if (report.size > maximumCompressedBytes) {
  throw new Error(`npm package exceeded the ${maximumCompressedBytes}-byte compressed review ceiling.`);
}
if (report.unpackedSize > maximumUnpackedBytes) {
  throw new Error(`npm package exceeded the ${maximumUnpackedBytes}-byte unpacked review ceiling.`);
}

const manifest = verifyPackedFiles(repositoryRoot, actualFiles);
const manifestBytes = manifest.reduce((total, file) => total + file.bytes, 0);
if (manifestBytes !== report.unpackedSize) {
  throw new Error("npm report unpacked size disagrees with the verified package manifest.");
}

console.log(
  `Verified exact ${report.entryCount}-file npm payload; ${formatMiB(report.size)} MiB compressed and ${formatMiB(report.unpackedSize)} MiB unpacked.`,
);

function formatMiB(bytes) {
  return (bytes / 1024 / 1024).toFixed(1);
}
