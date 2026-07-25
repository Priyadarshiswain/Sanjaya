import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const protocolText = readFileSync(
  join(evalRoot, "protocol", "engine-only-gate.json"),
  "utf8",
);
const protocol = JSON.parse(protocolText);
const manifest = JSON.parse(
  readFileSync(join(evalRoot, "repositories", "manifest.json"), "utf8"),
);
const manifestById = new Map(
  manifest.repositories.map((repository) => [repository.id, repository]),
);
const allowedTools = new Set([
  "find_definition",
  "find_references",
  "search_code",
]);

assert.equal(protocol.schemaVersion, "1.0");
assert.equal(protocol.status, "frozen_before_execution");
assert.equal(protocol.package.name, "sanjaya-mcp");
assert.equal(protocol.package.version, "0.1.2");
assert.equal(protocol.controls.modelCalls, 0);
assert.equal(protocol.controls.queryRepetitions, 5);
assert.equal(protocol.repositories.length, 3);

const taskIds = new Set();
let taskCount = 0;
for (const repository of protocol.repositories) {
  const frozen = manifestById.get(repository.id);
  assert.ok(frozen, `${repository.id} is absent from the frozen manifest.`);
  assert.equal(repository.commit, frozen.commit);
  assert.equal(repository.tasks.length, 5);
  for (const task of repository.tasks) {
    taskCount += 1;
    assert.ok(!taskIds.has(task.id), `Duplicate task ${task.id}.`);
    taskIds.add(task.id);
    assert.ok(task.intent.trim());
    assert.ok(task.native.pattern.trim());
    assert.ok(task.native.globs.length > 0);
    assert.ok(task.native.scopes.length > 0);
    assert.ok(allowedTools.has(task.sanjaya.tool));
    assert.ok(task.targets.length > 0);
    for (const path of [
      ...task.native.scopes,
      ...task.targets.map((target) => target.path),
    ]) {
      assert.ok(!path.startsWith("/") && !path.includes(".."));
      assert.equal(path.includes("\\"), false);
    }
    for (const target of task.targets) {
      assert.ok(target.startLine > 0);
      assert.ok(target.endLine >= target.startLine);
    }
  }
}
assert.equal(taskCount, 15);
assert.equal(
  protocol.interpretationBoundary.some((line) => line.includes("not natural-language")),
  true,
);

const sha256 = createHash("sha256")
  .update(protocolText, "utf8")
  .digest("hex");
process.stdout.write(
  `Verified frozen engine-only gate: ${taskCount} tasks, `
  + `${protocol.repositories.length} repositories, protocol ${sha256}.\n`,
);
