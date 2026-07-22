import { createHash } from "node:crypto";
import { createRequire } from "node:module";
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative, resolve, sep } from "node:path";

const repositoryRoot = process.cwd();
const componentRoot = join(repositoryRoot, "third_party", "typescript");
const packageRoot = join(componentRoot, "package");
const provenance = JSON.parse(readFileSync(join(componentRoot, "PROVENANCE.json"), "utf8"));
const approvedPaths = [
  "package/LICENSE.txt",
  "package/ThirdPartyNoticeText.txt",
  "package/lib/typescript.js",
  "package/package.json",
];

assert(provenance.schemaVersion === "1", "Unexpected provenance schema version.");
assert(provenance.package === "typescript", "Unexpected upstream package name.");
assert(provenance.version === "6.0.3", "Unexpected upstream TypeScript version.");
assert(provenance.license === "Apache-2.0", "Unexpected upstream license.");
assert(
  /^[a-f0-9]{64}$/.test(provenance.source?.tarballSha256 ?? ""),
  "Tarball SHA-256 is missing or invalid.",
);

const manifestPaths = provenance.files.map((file) => file.path).sort();
assertEqual(manifestPaths, approvedPaths, "Provenance file allowlist drifted.");
assertEqual(
  listFiles(packageRoot).map((path) => `package/${path}`).sort(),
  approvedPaths,
  "Vendored package contains a non-allowlisted or missing file.",
);

for (const file of provenance.files) {
  verifyFile(join(componentRoot, ...file.path.split("/")), file);
}

const packageMetadata = JSON.parse(readFileSync(join(packageRoot, "package.json"), "utf8"));
assert(packageMetadata.name === "typescript", "Vendored package identity is not TypeScript.");
assert(packageMetadata.version === provenance.version, "Vendored package version disagrees with provenance.");
assert(packageMetadata.license === provenance.license, "Vendored package license disagrees with provenance.");
assert(packageMetadata.main === "./lib/typescript.js", "Vendored package main entry changed.");

const runtimePath = resolve(packageRoot, "lib", "typescript.js");
const require = createRequire(import.meta.url);
assert(require.resolve(runtimePath) === runtimePath, "TypeScript did not resolve from the fixed bundled path.");
const ts = require(runtimePath);
assert(ts.version === provenance.version, "Loaded TypeScript runtime version disagrees with provenance.");
verifyParse(ts, "sample.ts", "export interface Item { id: number }", ts.ScriptKind.TS, ts.SyntaxKind.InterfaceDeclaration);
verifyParse(ts, "sample.js", "export class Item { run() {} }", ts.ScriptKind.JS, ts.SyntaxKind.ClassDeclaration);

const publishedRoot = join(repositoryRoot, "dist", "dotnet", "third_party", "typescript");
assert(statSync(publishedRoot).isDirectory(), "Published TypeScript artifact directory is missing.");
assertEqual(
  listFiles(join(publishedRoot, "package")).map((path) => `package/${path}`).sort(),
  approvedPaths,
  "Published TypeScript package allowlist drifted.",
);
for (const file of provenance.files) {
  verifyFile(join(publishedRoot, ...file.path.split("/")), file);
}
verifyFile(join(publishedRoot, "PROVENANCE.json"), {
  bytes: statSync(join(componentRoot, "PROVENANCE.json")).size,
  sha256: sha256(join(componentRoot, "PROVENANCE.json")),
});

console.log("TypeScript 6.0.3 provenance, fixed-path loading, parsing, and publish output verified.");

function verifyParse(ts, fileName, source, scriptKind, expectedKind) {
  const sourceFile = ts.createSourceFile(fileName, source, ts.ScriptTarget.Latest, true, scriptKind);
  assert(sourceFile.parseDiagnostics.length === 0, `${fileName} produced an unexpected parse diagnostic.`);
  assert(sourceFile.statements.some((statement) => statement.kind === expectedKind), `${fileName} AST shape was not found.`);
}

function verifyFile(path, expected) {
  const size = statSync(path).size;
  assert(size === expected.bytes, `${display(path)} byte count disagrees with provenance.`);
  assert(sha256(path) === expected.sha256, `${display(path)} SHA-256 disagrees with provenance.`);
}

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

function listFiles(root) {
  const files = [];
  const directories = [root];
  while (directories.length > 0) {
    const current = directories.pop();
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const fullPath = join(current, entry.name);
      if (entry.isDirectory()) {
        directories.push(fullPath);
      } else if (entry.isFile()) {
        files.push(relative(root, fullPath).split(sep).join("/"));
      } else {
        throw new Error(`${display(fullPath)} is not a regular file.`);
      }
    }
  }
  return files;
}

function display(path) {
  return relative(repositoryRoot, path).split(sep).join("/");
}

function assertEqual(actual, expected, message) {
  assert(JSON.stringify(actual) === JSON.stringify(expected), message);
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
