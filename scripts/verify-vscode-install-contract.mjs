import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { join, relative, resolve, sep } from "node:path";
import {
  createBoundVsCodeServerConfiguration,
  createVsCodeInstallUrl,
  createVsCodeServerConfiguration,
  parseVsCodeInstallUrl,
} from "./vscode-install-contract.mjs";
import {
  assertReleasePackage,
  packageName,
  publishedVersion,
  publicationState,
  registryPublicationState,
  releaseVersion,
  vsCodeInstallState,
} from "./release-contract.mjs";

const repositoryRoot = resolve(".");
const historicalRunbooks = new Set([
  "docs/releasing.md",
  "docs/releasing-0.1.1.md",
  "docs/releasing-0.1.2.md",
]);
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
assert.ok(Object.isFrozen(configuration));
assert.ok(Object.isFrozen(configuration.args));

const firstWorkspace = resolve("synthetic-workspaces", "first");
const secondWorkspace = resolve("synthetic-workspaces", "second");
const firstConfiguration = createBoundVsCodeServerConfiguration(
  reviewedReleaseVersion,
  firstWorkspace,
);
const secondConfiguration = createBoundVsCodeServerConfiguration(
  reviewedReleaseVersion,
  secondWorkspace,
);
assert.equal(firstConfiguration.args[3], firstWorkspace);
assert.equal(secondConfiguration.args[3], secondWorkspace);
assert.notEqual(firstConfiguration.args[3], secondConfiguration.args[3]);
assert.equal(firstConfiguration.args.filter((argument) => argument === firstWorkspace).length, 1);
assert.equal(secondConfiguration.args.filter((argument) => argument === secondWorkspace).length, 1);
assert.ok(!JSON.stringify(firstConfiguration).includes(secondWorkspace));
assert.ok(!JSON.stringify(secondConfiguration).includes(firstWorkspace));
for (const invalidWorkspace of ["", ".", "relative/path", "\0", null]) {
  assert.throws(
    () => createBoundVsCodeServerConfiguration(reviewedReleaseVersion, invalidWorkspace),
    /one absolute folder/u,
  );
}

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
assert.equal(publicationState, "published", "VS Code must target an independently verified npm release.");
assert.equal(
  publishedVersion,
  releaseVersion,
  "VS Code must target the exact independently verified npm release.",
);
assert.equal(
  registryPublicationState,
  "unpublished",
  "Update the registry state only after its public record is independently verified.",
);
assert.equal(
  vsCodeInstallState,
  "registry_pending",
  "The public VS Code link must remain locked until registry verification.",
);

for (const publicDocument of ["README.md", ...listPublicMarkdown(resolve(repositoryRoot, "docs"))]) {
  const content = readFileSync(resolve(repositoryRoot, publicDocument), "utf8");
  assert.ok(
    !content.includes("vscode:mcp/install?"),
    `${publicDocument} exposes an active install URL before registry verification.`,
  );
  for (const match of content.matchAll(/sanjaya-mcp@([^\s`"']+)/gu)) {
    assert.ok(
      match[1] === publishedVersion,
      `${publicDocument} contains a package command that is not the published ${publishedVersion}.`,
    );
  }
}

const readme = readFileSync(resolve(repositoryRoot, "README.md"), "utf8");
assert.ok(
  readme.includes(`${packageName}@${publishedVersion}`),
  `README.md must contain the verified ${publishedVersion} installation command.`,
);

console.log(`VS Code install configuration, v${releaseVersion} pin, two-workspace binding, and registry-pending activation lock verified.`);

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
      } else if (entry.isFile()
          && entry.name.endsWith(".md")
          && !historicalRunbooks.has(relativePath)) {
        result.push(relativePath);
      }
    }
  }
  return result.sort();
}
