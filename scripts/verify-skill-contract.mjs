import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { join, relative, resolve } from "node:path";

const repositoryRoot = resolve(".");
const contractPath = "docs/evidence-first-skill.md";
const skillRoot = resolve(
  repositoryRoot,
  "skills",
  "evidence-first-code-discovery",
);
const skillPath = relative(repositoryRoot, join(skillRoot, "SKILL.md"));
const interfacePath = relative(
  repositoryRoot,
  join(skillRoot, "agents", "openai.yaml"),
);
const expectedDescription = "Discover and explain unfamiliar codebases with "
  + "verifiable repository-relative evidence. Use when locating "
  + "implementations, declarations, candidate references, structure, recent "
  + "changes, or code evidence for an explanation, review, or planned edit; "
  + "choose capability-fitting Sanjaya MCP tools when available and fall back "
  + "to native exact search, file reading, and read-only Git when they are not.";

const contract = readFileSync(resolve(repositoryRoot, contractPath), "utf8");
const normalizedContract = normalize(contract);
const skill = readFileSync(resolve(repositoryRoot, skillPath), "utf8");
const normalizedSkill = normalize(skill);
const interfaceDocument = readFileSync(
  resolve(repositoryRoot, interfacePath),
  "utf8",
);
const readme = readFileSync(resolve(repositoryRoot, "README.md"), "utf8");
const normalizedReadme = normalize(readme);
const packageDocument = JSON.parse(
  readFileSync(resolve(repositoryRoot, "package.json"), "utf8"),
);

assert.deepEqual(
  listSkillFiles(skillRoot),
  ["SKILL.md", "agents/openai.yaml"],
  "The initial portable skill must contain exactly SKILL.md and agents/openai.yaml.",
);

const frontmatter = skill.match(
  /^---\r?\nname: ([^\r\n]+)\r?\ndescription: ([^\r\n]+)\r?\n---\r?\n/u,
);
assert.ok(frontmatter, `${skillPath} must have only name and description frontmatter.`);
assert.equal(frontmatter[1], "evidence-first-code-discovery");
assert.equal(frontmatter[2], expectedDescription);
assert.ok(
  skill.split(/\r?\n/u).length <= 150,
  `${skillPath} must remain concise enough for progressive disclosure.`,
);
assert.ok(!skill.includes("TODO"), `${skillPath} contains template text.`);

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
    skill.includes(`\`${tool}\``),
    `${skillPath} does not classify the public ${tool} tool.`,
  );
}

for (const boundary of [
  "When Sanjaya is available, `capabilities` is exposed",
  "call it once. Reuse the result.",
  "If `capabilities` is not exposed, use the client’s live tool schemas or metadata as the fallback capability boundary.",
  "ask for approval before calling `index_codebase`",
  "Do not retry the same call unchanged.",
  "use native tools only for the missing evidence; do not re-query facts already established.",
  "After three Sanjaya discovery calls, reassess",
  "Treat three as a review point, not a hard limit",
  "Never expose an absolute repository path.",
  "Omit author names, email addresses, remote URLs, Git configuration, commit bodies, and change statistics unless the user explicitly requests them",
  "Do not treat this skill invocation as permission to install software, contact the network, edit source",
]) {
  assert.ok(
    normalizedSkill.includes(boundary),
    `${skillPath} lost the behavior boundary: ${boundary}`,
  );
}

const expectedInterface = [
  "interface:",
  '  display_name: "Evidence-First Code Discovery"',
  '  short_description: "Ground code discovery in verifiable evidence"',
  '  default_prompt: "Use $evidence-first-code-discovery to locate the relevant implementation and explain it with repository-relative evidence."',
  "",
].join("\n");
assert.equal(
  interfaceDocument.replace(/\r\n/gu, "\n"),
  expectedInterface,
  `${interfacePath} drifted from the generated reviewed interface metadata.`,
);

for (const heading of [
  "## Why this is separate from the MCP server",
  "## Implemented identity and trigger",
  "## Required workflow",
  "## Cost and stopping rules",
  "## Failure and fallback contract",
  "## Initial packaging boundary",
  "## Initial qualitative forward test",
  "## Evaluation contract",
  "## Implementation status and remaining gates",
]) {
  assert.ok(contract.includes(heading), `${contractPath} is missing ${heading}.`);
}
for (const boundary of [
  "the skill is not installed or published.",
  "The skill must not create or rebuild the index silently.",
  "This small qualitative check validates workflow comprehension only; it is not a preregistered benchmark or evidence of product benefit.",
  "Implementation approval does not authorize a paid model run or publication.",
  "Installation, model evaluation, and publication remain later explicit decisions",
]) {
  assert.ok(
    normalizedContract.includes(boundary),
    `${contractPath} lost the approval boundary: ${boundary}`,
  );
}
assert.ok(
  contract.includes(`description: ${expectedDescription}`),
  `${contractPath} and ${skillPath} metadata disagree.`,
);

assert.ok(
  normalizedReadme.includes(
    "The skill is separate from the npm server package and is not automatically",
  ),
  "README.md must distinguish source availability from installation.",
);
assert.ok(
  readme.includes("[skill contract](docs/evidence-first-skill.md)"),
  "README.md must link to the skill contract.",
);
assert.ok(
  contract.includes("[distribution contract](skill-distribution.md)"),
  `${contractPath} must link to the distribution contract.`,
);
assert.ok(
  packageDocument.files.every(
    (path) => path !== "skills" && !path.startsWith("skills/"),
  ),
  "The npm server package must not include the portable skill implicitly.",
);

console.log(
  "Evidence-First skill layout, metadata, tool routing, approval gates, "
  + "and npm exclusion verified.",
);

function normalize(value) {
  return value.replace(/\s+/gu, " ").trim();
}

function listSkillFiles(root) {
  const files = [];
  visit(root);
  return files.sort();

  function visit(directory) {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = join(directory, entry.name);
      if (entry.isSymbolicLink()) {
        throw new Error(`Skill package must not contain symlink ${path}.`);
      }
      if (entry.isDirectory()) {
        visit(path);
      } else if (entry.isFile()) {
        files.push(relative(root, path).replaceAll("\\", "/"));
      } else {
        throw new Error(`Skill package contains unsupported entry ${path}.`);
      }
    }
  }
}
