import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  assertReleasePackage,
  packageName,
  registryName,
  releaseVersion,
} from "./release-contract.mjs";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const serverPath = resolve(repositoryRoot, "server.json");
const packagePath = resolve(repositoryRoot, "package.json");
const serverSource = readFileSync(serverPath, "utf8");
const packageSource = readFileSync(packagePath, "utf8");

assert.ok(Buffer.byteLength(serverSource, "utf8") <= 4 * 1024, "server.json exceeds the registry's 4 KiB limit.");
assert.ok(!serverSource.startsWith("\uFEFF"), "server.json must not contain a byte-order mark.");

const serverDocument = JSON.parse(serverSource);
const packageDocument = JSON.parse(packageSource);

const expectedSchema = "https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json";
const expectedRegistryName = registryName;
const expectedPackageName = packageName;
const expectedRepositoryUrl = "https://github.com/Priyadarshiswain/Sanjaya";
const expectedRepositoryId = "1307595672";

assertExactKeys(
  serverDocument,
  ["$schema", "description", "name", "packages", "repository", "title", "version", "websiteUrl"],
  "server.json",
);
assert.equal(serverDocument.$schema, expectedSchema);
assert.equal(serverDocument.name, expectedRegistryName);
assert.match(serverDocument.name, /^[a-zA-Z0-9.-]+\/[a-zA-Z0-9._-]+$/u);
assert.equal(serverDocument.title, "Sanjaya");
assertNonEmptyTextWithin(serverDocument.title, 100, "title");
assert.equal(serverDocument.description, packageDocument.description);
assertNonEmptyTextWithin(serverDocument.description, 100, "description");
assert.equal(serverDocument.version, releaseVersion);
assertExactVersion(serverDocument.version, "server version");

assertExactKeys(serverDocument.repository, ["id", "source", "url"], "repository");
assert.equal(serverDocument.repository.url, expectedRepositoryUrl);
assertHttpsUrl(serverDocument.repository.url, "repository URL");
assert.equal(serverDocument.repository.source, "github");
assert.equal(serverDocument.repository.id, expectedRepositoryId);
assert.equal(serverDocument.websiteUrl, expectedRepositoryUrl);
assertHttpsUrl(serverDocument.websiteUrl, "website URL");

assert.ok(Array.isArray(serverDocument.packages));
assert.equal(serverDocument.packages.length, 1, "Sanjaya must advertise exactly one reviewed package.");
const registryPackage = serverDocument.packages[0];
assertExactKeys(
  registryPackage,
  ["identifier", "packageArguments", "registryType", "transport", "version"],
  "registry package",
);
assert.equal(registryPackage.registryType, "npm");
assert.equal(registryPackage.identifier, expectedPackageName);
assert.equal(registryPackage.version, serverDocument.version);
assertExactVersion(registryPackage.version, "package version");
assertExactKeys(registryPackage.transport, ["type"], "transport");
assert.equal(registryPackage.transport.type, "stdio");

assert.ok(Array.isArray(registryPackage.packageArguments));
assert.equal(registryPackage.packageArguments.length, 2, "The root CLI requires exactly two package arguments.");
const [rootFlag, repositoryRootInput] = registryPackage.packageArguments;
assertExactKeys(rootFlag, ["type", "value"], "root flag argument");
assert.deepEqual(rootFlag, { type: "positional", value: "--root" });
assertExactKeys(
  repositoryRootInput,
  ["description", "format", "isRequired", "type", "valueHint"],
  "repository root argument",
);
assert.deepEqual(repositoryRootInput, {
  type: "positional",
  valueHint: "repository_root",
  description: "Absolute path to the repository root Sanjaya may inspect.",
  format: "filepath",
  isRequired: true,
});
assert.ok(!Object.hasOwn(repositoryRootInput, "value"), "The repository path must remain user-configurable.");
assert.ok(!Object.hasOwn(repositoryRootInput, "isSecret"), "The repository path is not a secret input.");

assert.equal(packageDocument.name, expectedPackageName);
assertReleasePackage(packageDocument);
assert.equal(packageDocument.mcpName, expectedRegistryName);
assert.equal(packageDocument.license, "Apache-2.0");
assert.equal(packageDocument.repository?.url, `git+${expectedRepositoryUrl}.git`);
assert.equal(packageDocument.homepage, `${expectedRepositoryUrl}#readme`);
assert.equal(registryPackage.identifier, packageDocument.name);
assert.equal(registryPackage.version, packageDocument.version);
assert.equal(serverDocument.name, packageDocument.mcpName);

console.log("Official MCP Registry metadata identity, package, root input, and v0.1.0 candidate locks verified.");

function assertExactKeys(value, expectedKeys, label) {
  assert.ok(value && typeof value === "object" && !Array.isArray(value), `${label} must be an object.`);
  assert.deepEqual(Object.keys(value).sort(), [...expectedKeys].sort(), `${label} contains unexpected or missing fields.`);
}

function assertNonEmptyTextWithin(value, maximumLength, label) {
  assert.equal(typeof value, "string", `${label} must be a string.`);
  assert.equal(value, value.trim(), `${label} must not have surrounding whitespace.`);
  assert.ok(value.length > 0 && value.length <= maximumLength, `${label} must contain 1-${maximumLength} characters.`);
}

function assertExactVersion(value, label) {
  assert.equal(typeof value, "string", `${label} must be a string.`);
  assert.equal(value, value.trim(), `${label} must not have surrounding whitespace.`);
  assert.ok(value.length > 0, `${label} must not be empty.`);
  assert.ok(!/^(?:latest|[~^<>=*]|v?\d+(?:\.\d+){0,2}\.(?:x|\*))/iu.test(value), `${label} must not be a tag or range.`);
  assert.ok(!/[|\s]/u.test(value), `${label} must be one exact version.`);
}

function assertHttpsUrl(value, label) {
  const url = new URL(value);
  assert.equal(url.protocol, "https:", `${label} must use HTTPS.`);
  assert.equal(url.username, "", `${label} must not contain credentials.`);
  assert.equal(url.password, "", `${label} must not contain credentials.`);
}
