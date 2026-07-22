import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { join, relative, resolve, sep } from "node:path";
import {
  createVsCodeInstallUrl,
  createVsCodeServerConfiguration,
  parseVsCodeInstallUrl,
} from "./vscode-install-contract.mjs";
import {
  assertReleasePackage,
  packageName,
  publicationState,
  releaseVersion,
} from "./release-contract.mjs";

const repositoryRoot = resolve(".");
const packageDocument = JSON.parse(readFileSync(resolve(repositoryRoot, "package.json"), "utf8"));
const reviewedReleaseVersion = releaseVersion;
const expected = {
  name: "sanjaya",
  type: "stdio",
  command: "npx",
  args: ["-y", `${packageName}@${releaseVersion}`, "--root", "${workspaceFolder}"],
};

const configuration = createVsCodeServerConfiguration(reviewedReleaseVersion);
assert.deepEqual(configuration, expected);
assert.deepEqual(Object.keys(configuration), ["name", "type", "command", "args"]);
assert.equal(configuration.args[2], "--root");
assert.equal(configuration.args[3], "${workspaceFolder}");

const installUrl = createVsCodeInstallUrl(reviewedReleaseVersion);
assert.ok(installUrl.startsWith("vscode:mcp/install?"));
assert.deepEqual(parseVsCodeInstallUrl(installUrl), expected);
assert.equal(
  installUrl,
  `vscode:mcp/install?${encodeURIComponent(JSON.stringify(expected))}`,
);

const serialized = JSON.stringify(configuration);
for (const forbidden of [
  "latest",
  "0.0.0-development",
  "&&",
  "||",
  "shell",
  "env",
  "sandbox",
  "network",
  "allowWrite",
]) {
  assert.ok(!serialized.includes(forbidden), `VS Code configuration contains forbidden value: ${forbidden}`);
}

for (const invalidVersion of [
  "0.0.0",
  "0.1",
  "0.1.0-development",
  "latest",
  "^0.1.0",
  " 0.1.0",
  "0.1.0 ",
  "01.0.0",
  "",
  null,
]) {
  assert.throws(
    () => createVsCodeInstallUrl(invalidVersion),
    /exact stable published version/u,
  );
}

assertReleasePackage(packageDocument);
assert.equal(publicationState, "candidate", "The public VS Code link must remain locked before publication.");

for (const publicDocument of ["README.md", ...listPublicMarkdown(resolve(repositoryRoot, "docs"))]) {
  const content = readFileSync(resolve(repositoryRoot, publicDocument), "utf8");
  assert.ok(
    !content.includes("vscode:mcp/install?"),
    `${publicDocument} exposes an active install URL before publication.`,
  );
  for (const match of content.matchAll(/sanjaya-mcp@([^\s`"']+)/gu)) {
    assert.equal(
      match[1],
      releaseVersion,
      `${publicDocument} contains a package command that is not pinned to ${releaseVersion}.`,
    );
  }
}

console.log("VS Code install configuration, v0.1.0 pin, workspace root, and candidate activation lock verified.");

function listPublicMarkdown(root) {
  const result = [];
  const pending = [root];
  while (pending.length > 0) {
    const current = pending.pop();
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const path = join(current, entry.name);
      const relativePath = relative(repositoryRoot, path).split(sep).join("/");
      if (entry.isDirectory()) {
        if (relativePath !== "docs/local") {
          pending.push(path);
        }
      } else if (entry.isFile() && entry.name.endsWith(".md")) {
        result.push(relativePath);
      }
    }
  }
  return result.sort();
}
