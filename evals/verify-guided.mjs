import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const protocol = readJson(join(evalRoot, "protocol", "guided.json"));
const pilot = readJson(join(evalRoot, "tasks", "pilot.json"));
const tasks = new Map(pilot.tasks.map((task) => [task.id, task]));

assert.equal(protocol.status, "frozen_after_zero_uptake_headline_pilot");
assert.deepEqual(protocol.design.arms, ["sanjaya_guided"]);
assert.equal(protocol.design.taskIds.length, 6);
assert.equal(protocol.design.repetitions, 3);
assert.equal(protocol.design.totalRuns, 18);
assert.equal(
  protocol.design.totalRuns,
  protocol.design.taskIds.length * protocol.design.repetitions,
);
assert.equal(protocol.design.runNumberOffset, 1000);
assert.ok(protocol.design.guidedInstruction.includes("capabilities tool"));
for (const taskId of protocol.design.taskIds) {
  const task = tasks.get(taskId);
  assert.ok(task, `Unknown guided task ${taskId}.`);
  assert.equal(task.indexState, "warm");
}

console.log(
  "Verified the separate 18-run guided contingency after zero headline uptake.",
);

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}
