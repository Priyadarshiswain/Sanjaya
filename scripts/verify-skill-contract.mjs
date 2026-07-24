import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const repositoryRoot = resolve(".");
const contractPath = "docs/evidence-first-skill.md";
const contract = readFileSync(resolve(repositoryRoot, contractPath), "utf8");
const normalizedContract = contract.replace(/\s+/gu, " ");
const readme = readFileSync(resolve(repositoryRoot, "README.md"), "utf8");

const requiredHeadings = [
  "## Why this is separate from the MCP server",
  "## Proposed identity and trigger",
  "## Required workflow",
  "## Cost and stopping rules",
  "## Failure and fallback contract",
  "## Initial packaging boundary",
  "## Evaluation contract",
  "## Review gates before implementation",
];
for (const heading of requiredHeadings) {
  assert.ok(contract.includes(heading), `${contractPath} is missing ${heading}.`);
}

const publicTools = [
  "capabilities",
  "health_check",
  "file_outline",
  "search_text",
  "recent_changes",
  "index_codebase",
  "search_code",
  "find_definition",
  "find_references",
  "get_source",
];
for (const tool of publicTools) {
  assert.ok(
    contract.includes(`\`${tool}\``),
    `${contractPath} does not classify the public ${tool} tool.`,
  );
}

for (const boundary of [
  "Status: design only; no installable skill is included or published.",
  "The skill must not create or rebuild the index silently.",
  "Three is a review point, not a hard limit",
  "Implementation approval does not authorize a paid model run or publication.",
  "Skill installation or publication is a later explicit decision.",
]) {
  assert.ok(
    normalizedContract.includes(boundary),
    `${contractPath} lost the approval boundary: ${boundary}`,
  );
}

assert.ok(
  readme.includes("[skill contract](docs/evidence-first-skill.md)"),
  "README.md must link to the design-only skill contract.",
);
assert.ok(
  readme.includes("not-yet-implemented agent-orchestration boundary"),
  "README.md must distinguish the skill design from shipped functionality.",
);

const trackedFiles = execFileSync(
  "git",
  ["ls-files", "-z"],
  { cwd: repositoryRoot, encoding: "utf8" },
).split("\0").filter(Boolean);
assert.deepEqual(
  trackedFiles.filter((path) => path.endsWith("/SKILL.md") || path === "SKILL.md"),
  [],
  "The design-only review must not include an installable SKILL.md.",
);

console.log(
  "Evidence-First skill design, tool coverage, approval gates, and "
  + "no-implementation boundary verified.",
);
