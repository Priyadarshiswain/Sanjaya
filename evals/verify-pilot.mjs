import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const schemaRoot = join(evalRoot, "schemas");
const taskSchema = readJson(join(schemaRoot, "task.schema.json"));
const manifestSchema = readJson(
  join(schemaRoot, "repository-manifest.schema.json"),
);
const manifest = readJson(join(evalRoot, "repositories", "manifest.json"));
const pilot = readJson(join(evalRoot, "tasks", "pilot.json"));
const protocol = readJson(join(evalRoot, "protocol", "pilot.json"));
const outputSchema = readJson(
  join(evalRoot, protocol.controls.outputSchema),
);

const ajv = new Ajv2020({ allErrors: true, strict: true });
const validateTask = ajv.compile(taskSchema);
const validateManifest = ajv.compile(manifestSchema);
const validateOutputShape = ajv.compile(outputSchema);
assert.ok(validateManifest(manifest), formatErrors(validateManifest.errors));
assert.equal(typeof validateOutputShape, "function");

assert.equal(pilot.schemaVersion, "1.0");
assert.equal(pilot.tasks.length, 12);
assert.equal(protocol.design.totalRuns, 72);
assert.equal(
  protocol.design.totalRuns,
  pilot.tasks.length
    * protocol.design.arms.length
    * protocol.design.repetitions,
);
assert.equal(protocol.target.version, "0.1.2");
assert.equal(protocol.agent.version, "0.144.5");
assert.equal(protocol.controls.scorerVersion, "1.0.0");

const repositories = new Map(
  manifest.repositories.map((repository) => [repository.id, repository]),
);
const taskIds = new Set();
for (const task of pilot.tasks) {
  assert.ok(validateTask(task), `${task.id}: ${formatErrors(validateTask.errors)}`);
  assert.ok(!taskIds.has(task.id), `Duplicate task ID ${task.id}.`);
  taskIds.add(task.id);
  const repository = repositories.get(task.repository.id);
  assert.ok(repository, `${task.id} references an unknown repository.`);
  assert.equal(task.repository.commit, repository.commit);
  assert.equal(task.sizeBand, repository.sizeBand);
  assert.equal(task.scorerVersion, protocol.controls.scorerVersion);
  assert.equal(
    new Set(task.groundTruth.requiredClaims.map((claim) => claim.key)).size,
    task.groundTruth.requiredClaims.length,
    `${task.id} has duplicate required claim keys.`,
  );
}
assert.deepEqual([...taskIds].sort(), [...protocol.design.taskIds].sort());
assert.ok(
  manifest.repositories.some(
    (repository) =>
      repository.originKind === "public_git"
      && repository.sizeBand === "large",
  ),
  "Pilot must contain a large public repository.",
);

console.log(
  "Verified the frozen 12-task, 72-run v0.1.2 pilot, repository manifest, "
  + "cross-record identities, and deterministic scoring contract.",
);

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function formatErrors(errors) {
  return errors?.map((error) => `${error.instancePath} ${error.message}`).join("; ")
    ?? "unknown schema error";
}
