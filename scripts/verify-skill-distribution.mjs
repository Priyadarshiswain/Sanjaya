import assert from "node:assert/strict";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { join, relative, resolve } from "node:path";
import { publishedVersion } from "./release-contract.mjs";

const repositoryRoot = resolve(".");
const contractPath = "docs/skill-distribution.md";
const pluginRoot = resolve(repositoryRoot, "plugins", "sanjaya");
const skillRoot = resolve(
  pluginRoot,
  "skills",
  "evidence-first-code-discovery",
);
const manifestPath = resolve(pluginRoot, ".codex-plugin", "plugin.json");
const contract = readFileSync(resolve(repositoryRoot, contractPath), "utf8");
const normalizedContract = normalize(contract);
const manifest = JSON.parse(readFileSync(manifestPath, "utf8"));
const readme = readFileSync(resolve(repositoryRoot, "README.md"), "utf8");
const lifecycleDocument = readFileSync(
  resolve(repositoryRoot, "docs", "plugin-lifecycle.md"),
  "utf8",
);
const normalizedLifecycleDocument = normalize(lifecycleDocument);
const lifecycleScript = readFileSync(
  resolve(repositoryRoot, "scripts", "verify-plugin-lifecycle.mjs"),
  "utf8",
);
const packageDocument = JSON.parse(
  readFileSync(resolve(repositoryRoot, "package.json"), "utf8"),
);
const workflow = readFileSync(
  resolve(repositoryRoot, ".github", "workflows", "ci.yml"),
  "utf8",
);

assert.deepEqual(
  listFiles(pluginRoot),
  [
    ".codex-plugin/plugin.json",
    "skills/evidence-first-code-discovery/SKILL.md",
    "skills/evidence-first-code-discovery/agents/openai.yaml",
  ],
  "The local plugin must remain one manifest plus one reviewed two-file skill.",
);
assert.deepEqual(
  listFiles(skillRoot),
  ["SKILL.md", "agents/openai.yaml"],
  "The canonical plugin skill must remain one reviewed two-file source.",
);
assert.deepEqual(
  manifest,
  {
    name: "sanjaya",
    version: "0.1.0",
    description: "Evidence-first codebase discovery workflow for AI coding agents.",
    author: {
      name: "Priyadarshi Swain",
      url: "https://github.com/Priyadarshiswain",
    },
    homepage: "https://github.com/Priyadarshiswain/Sanjaya#readme",
    repository: "https://github.com/Priyadarshiswain/Sanjaya",
    license: "Apache-2.0",
    keywords: ["code-discovery", "evidence", "coding-agents"],
    skills: "./skills/",
    interface: {
      displayName: "Sanjaya",
      shortDescription: "Ground code discovery in verifiable evidence",
      longDescription: "Guides AI coding agents to choose fitting Sanjaya or "
        + "native discovery tools and report repository-relative evidence.",
      developerName: "Priyadarshi Swain",
      category: "Developer Tools",
      capabilities: ["Skills"],
      websiteURL: "https://github.com/Priyadarshiswain/Sanjaya",
      defaultPrompt: [
        "Use $evidence-first-code-discovery to investigate this codebase with "
          + "repository-relative evidence.",
      ],
    },
  },
  "The local plugin manifest drifted from the reviewed skills-only identity.",
);

for (const heading of [
  "## Product boundary",
  "## Selected channels",
  "## Why MCP bundling is deferred",
  "## Identity and versioning",
  "## Intended user lifecycle",
  "## Disposable marketplace verification",
  "## Clean-environment lifecycle verification",
  "## Trust and privacy review",
  "## Publication gates",
  "## Current local-only state",
]) {
  assert.ok(contract.includes(heading), `${contractPath} is missing ${heading}.`);
}

for (const boundary of [
  "No marketplace entry, installation, or skill publication has been created.",
  "The preferred Codex distribution is a minimal skills-only plugin.",
  "it did not create a second maintained copy.",
  "the first plugin must not declare mcpServers or include .mcp.json.",
  "Plugin versions and npm server versions are independent SemVer streams.",
  "Exact installation commands and clickable links remain intentionally absent",
  "This check does not invoke Codex, register a marketplace, install or enable the plugin, mutate personal configuration, contact a network, or create a publishable marketplace artifact.",
  "Public marketplace creation remains blocked until interactive disable/re-enable is reviewed separately and the preregistered Evidence-First skill evaluation completes.",
  "Approval of a skills-only plugin does not pre-approve that expansion.",
  "The local plugin exists only as reviewed repository source.",
  "Approval of this implementation does not authorize installation, marketplace publication, hosted-directory submission, or MCP bundling.",
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
  ".mcp.json",
  "skills/evidence-first-code-discovery/SKILL.md",
  "plugins/sanjaya/.mcp.json",
  "plugins/sanjaya/.app.json",
  "plugins/sanjaya/hooks.json",
  "plugins/sanjaya/scripts",
  "plugins/sanjaya/assets",
]) {
  assert.ok(
    !existsSync(resolve(repositoryRoot, forbiddenPath)),
    `Local-only plugin work created forbidden artifact ${forbiddenPath}.`,
  );
}

for (const forbiddenField of [
  "apps",
  "mcpServers",
  "hooks",
]) {
  assert.ok(
    !Object.hasOwn(manifest, forbiddenField),
    `The skills-only manifest must not declare ${forbiddenField}.`,
  );
}

for (const forbiddenPath of [
  ".agents/plugins/marketplace.json",
  ".agents/skills/evidence-first-code-discovery",
]) {
  assert.ok(
    !existsSync(resolve(repositoryRoot, forbiddenPath)),
    `Repository auto-discovery or marketplace artifact exists at ${forbiddenPath}.`,
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
  readme.includes(
    "[`evidence-first-code-discovery`](plugins/sanjaya/skills/evidence-first-code-discovery/SKILL.md)",
  ),
  "README.md must link to the single canonical plugin skill.",
);
assert.ok(
  readme.includes("[local lifecycle evidence](docs/plugin-lifecycle.md)"),
  "README.md must link to the local lifecycle evidence.",
);
for (const boundary of [
  "Interactive disable/re-enable and an authenticated agent invocation remain unverified manual gates.",
  "No host Codex configuration, cache, credential, home directory, or repository is mounted into the container.",
  "The lifecycle verifier intentionally is not run in hosted CI",
  "The test does not edit config.toml by hand to manufacture a passing result.",
  "These two manual checks remain required before a public Git marketplace entry or active installation instructions can be approved.",
]) {
  assert.ok(
    normalizedLifecycleDocument.includes(boundary),
    `docs/plugin-lifecycle.md lost the verification boundary: ${boundary}`,
  );
}
for (const implementationLock of [
  "@openai/codex@0.144.5",
  "network\", \"disconnect\", \"bridge",
  "--cachebuster-helper",
  "update_plugin_cachebuster.py",
  "Interactive disable remains",
]) {
  assert.ok(
    lifecycleScript.includes(implementationLock),
    `The lifecycle verifier lost its implementation lock: ${implementationLock}`,
  );
}
assert.ok(
  !lifecycleScript.includes("writeFileSync"),
  "The lifecycle verifier must not edit Codex configuration or marketplace metadata directly.",
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
assert.equal(
  packageDocument.scripts["verify:plugin-marketplace"],
  "node scripts/verify-plugin-marketplace.mjs",
  "package.json must expose the disposable marketplace verifier.",
);
assert.equal(
  packageDocument.scripts["verify:plugin-lifecycle"],
  "node scripts/verify-plugin-lifecycle.mjs",
  "package.json must expose the Docker lifecycle verifier.",
);
assert.ok(
  workflow.includes("npm run verify:skill-distribution"),
  "CI must enforce the local skills-only plugin contract.",
);
assert.ok(
  workflow.includes("npm run verify:plugin-marketplace"),
  "CI must enforce the disposable marketplace contract.",
);
assert.ok(
  workflow.includes("node --check scripts/verify-plugin-lifecycle.mjs"),
  "CI must check the lifecycle verifier without running Docker.",
);

console.log(
  "Local skills-only plugin identity, exact layout, npm separation, "
  + "root-binding deferral, and non-publication locks verified.",
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
