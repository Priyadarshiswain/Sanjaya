import assert from "node:assert/strict";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { join, relative, resolve } from "node:path";
import { publishedVersion } from "./release-contract.mjs";

const repositoryRoot = resolve(".");
const contractPath = "docs/skill-distribution.md";
const skillRoot = resolve(
  repositoryRoot,
  "skills",
  "evidence-first-code-discovery",
);
const contract = readFileSync(resolve(repositoryRoot, contractPath), "utf8");
const normalizedContract = normalize(contract);
const readme = readFileSync(resolve(repositoryRoot, "README.md"), "utf8");
const packageDocument = JSON.parse(
  readFileSync(resolve(repositoryRoot, "package.json"), "utf8"),
);
const workflow = readFileSync(
  resolve(repositoryRoot, ".github", "workflows", "ci.yml"),
  "utf8",
);

assert.deepEqual(
  listFiles(skillRoot),
  ["SKILL.md", "agents/openai.yaml"],
  "The pre-plugin canonical skill must remain one reviewed two-file source.",
);

for (const heading of [
  "## Product boundary",
  "## Selected channels",
  "## Why MCP bundling is deferred",
  "## Identity and versioning",
  "## Intended user lifecycle",
  "## Trust and privacy review",
  "## Publication gates",
  "## Current non-publication state",
]) {
  assert.ok(contract.includes(heading), `${contractPath} is missing ${heading}.`);
}

for (const boundary of [
  "No plugin, marketplace entry, installation, or skill publication has been created by this contract.",
  "The preferred Codex distribution is a minimal skills-only plugin.",
  "it must not create a second maintained copy.",
  "the first plugin must not declare mcpServers or include .mcp.json.",
  "Plugin versions and npm server versions are independent SemVer streams.",
  "Exact installation commands and clickable links remain intentionally absent",
  "Approval of a skills-only plugin does not pre-approve that expansion.",
  "It does not authorize installation, marketplace publication, hosted-directory submission, or MCP bundling.",
]) {
  assert.ok(
    normalizedContract.includes(boundary),
    `${contractPath} lost the release boundary: ${boundary}`,
  );
}

assert.ok(
  contract.includes(`sanjaya-mcp@${publishedVersion}`),
  `${contractPath} must name the independently verified npm version.`,
);
assert.ok(
  !contract.includes("sanjaya-mcp@latest"),
  `${contractPath} must not recommend a floating npm version.`,
);
const pluginDocumentationLink = contract.match(
  /\[plugin authoring documentation\]\(([^)\s]+)\)/u,
);
assert.ok(
  pluginDocumentationLink,
  `${contractPath} must contain the primary plugin documentation link.`,
);
assert.equal(
  pluginDocumentationLink[1],
  "https://learn.chatgpt.com/docs/build-plugins",
  `${contractPath} must use the exact trusted plugin documentation URL.`,
);

for (const forbiddenPath of [
  ".codex-plugin/plugin.json",
  ".mcp.json",
  ".agents/plugins/marketplace.json",
  ".agents/skills/evidence-first-code-discovery/SKILL.md",
  "plugins/sanjaya/.codex-plugin/plugin.json",
]) {
  assert.ok(
    !existsSync(resolve(repositoryRoot, forbiddenPath)),
    `Design-only distribution work created forbidden artifact ${forbiddenPath}.`,
  );
}

assert.ok(
  !readme.includes("codex plugin marketplace add Priyadarshiswain/Sanjaya"),
  "README.md must not expose an installation command before plugin publication.",
);
assert.ok(
  readme.includes("[distribution contract](docs/skill-distribution.md)"),
  "README.md must link to the skill distribution contract.",
);
assert.ok(
  packageDocument.files.every(
    (path) =>
      path !== "skills"
      && !path.startsWith("skills/")
      && path !== "plugins"
      && !path.startsWith("plugins/"),
  ),
  "The npm MCP package must not absorb skill or plugin content.",
);
assert.equal(
  packageDocument.scripts["verify:skill-distribution"],
  "node scripts/verify-skill-distribution.mjs",
  "package.json must expose the distribution verifier.",
);
assert.ok(
  workflow.includes("npm run verify:skill-distribution"),
  "CI must enforce the design-only skill distribution contract.",
);

console.log(
  "Skill distribution channels, root-binding deferral, canonical source, "
  + "and non-publication locks verified.",
);

function normalize(value) {
  return value.replace(/[`*]/gu, "").replace(/\s+/gu, " ").trim();
}

function listFiles(root) {
  const files = [];
  visit(root);
  return files.sort();

  function visit(directory) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isSymbolicLink()) {
        throw new Error(`Canonical skill must not contain symlink ${path}.`);
      }
      if (entry.isDirectory()) {
        visit(path);
      } else if (entry.isFile()) {
        files.push(relative(root, path).replaceAll("\\", "/"));
      } else {
        throw new Error(`Canonical skill contains unsupported entry ${path}.`);
      }
    }
  }
}
