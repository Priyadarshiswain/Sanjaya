import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";

const npmCli = process.env.npm_execpath;
if (!npmCli) {
  throw new Error("npm_execpath is unavailable; run this check through npm.");
}

const rootPackage = JSON.parse(readFileSync("package.json", "utf8"));
if (rootPackage.private !== true || rootPackage.version !== "0.0.0-development") {
  throw new Error("Package release safety locks changed.");
}
if (rootPackage.dependencies || rootPackage.devDependencies || rootPackage.optionalDependencies) {
  throw new Error("The Sanjaya npm launcher package must remain dependency-free.");
}

const pack = spawnSync(
  process.execPath,
  [npmCli, "pack", "--dry-run", "--json", "--cache", ".npm-cache"],
  { cwd: process.cwd(), encoding: "utf8" },
);
if (pack.status !== 0) {
  throw new Error(`npm package dry run failed: ${pack.stderr.trim()}`);
}

const report = JSON.parse(pack.stdout)[0];
const files = new Set(report.files.map((file) => file.path));
const required = [
  "LICENSE",
  "NOTICE",
  "THIRD-PARTY-NOTICES.txt",
  "dist/dotnet/third_party/typescript/PROVENANCE.json",
  "dist/dotnet/third_party/typescript/package/LICENSE.txt",
  "dist/dotnet/third_party/typescript/package/ThirdPartyNoticeText.txt",
  "dist/dotnet/third_party/typescript/package/lib/typescript.js",
  "dist/dotnet/third_party/typescript/package/package.json",
];
for (const path of required) {
  if (!files.has(path)) {
    throw new Error(`Required package file is missing: ${path}`);
  }
}

const forbidden = [...files].filter(
  (path) =>
    path.includes("/third_party/typescript/package/bin/") ||
    path.endsWith(".d.ts") ||
    path.endsWith(".map") ||
    path.endsWith(".node") ||
    (path.includes("/third_party/typescript/package/lib/") && !path.endsWith("/lib/typescript.js")),
);
if (forbidden.length > 0) {
  throw new Error(`Non-allowlisted TypeScript package files were included: ${forbidden.join(", ")}`);
}

console.log(
  `npm package dry run verified ${report.entryCount} files; ${Math.round(report.size / 1024 / 1024 * 10) / 10} MB compressed.`,
);
