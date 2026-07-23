import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import {
  readFileSync,
  readdirSync,
} from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import { scoreAnswer } from "./scorer.mjs";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const schemaRoot = join(evalRoot, "schemas");
const resultsRoot = join(evalRoot, "results", "v0.1.2", "pilot");
const runsRoot = join(resultsRoot, "runs");
const tasks = readJson(join(evalRoot, "tasks", "pilot.json")).tasks;
const taskById = new Map(tasks.map((task) => [task.id, task]));
const answerSchema = readJson(join(schemaRoot, "answer.schema.json"));
const runSchema = readJson(join(schemaRoot, "run.schema.json"));
const traceSchema = readJson(join(schemaRoot, "trace-event.schema.json"));
const ajv = new Ajv2020({ allErrors: true, strict: true });
ajv.addSchema(answerSchema);
const validateRun = ajv.compile(runSchema);
const validateTrace = ajv.compile(traceSchema);
const runFiles = readdirSync(runsRoot).filter((file) => file.endsWith(".json"));

assert.equal(runFiles.length, 72, "Expected all 72 planned run records.");
const identities = new Set();
const runIds = new Set();
let completed = 0;
let failures = 0;

for (const file of runFiles) {
  const run = readJson(join(runsRoot, file));
  assert.ok(validateRun(run), `${file}: ${formatErrors(validateRun.errors)}`);
  assert.equal(file, `${run.runId}.json`);
  assert.ok(!runIds.has(run.runId), `Duplicate run ID ${run.runId}.`);
  runIds.add(run.runId);
  const identity = `${run.taskId}|${run.arm}|${run.repetition}`;
  assert.ok(!identities.has(identity), `Duplicate run identity ${identity}.`);
  identities.add(identity);
  const task = taskById.get(run.taskId);
  assert.ok(task, `${run.runId} references an unknown task.`);
  assert.equal(run.controls.repositoryCommit, task.repository.commit);

  const tracePath = join(resultsRoot, run.trace.path);
  const traceText = readFileSync(tracePath, "utf8");
  assert.equal(sha256(traceText), run.trace.sha256);
  const traceEvents = traceText
    .split(/\r?\n/u)
    .filter(Boolean)
    .map((line) => JSON.parse(line));
  assert.equal(traceEvents.length, run.trace.eventCount);
  for (const [index, event] of traceEvents.entries()) {
    assert.ok(
      validateTrace(event),
      `${run.runId} trace ${index + 1}: ${formatErrors(validateTrace.errors)}`,
    );
    assert.equal(event.sequence, index + 1);
  }

  if (run.status === "completed") {
    completed += 1;
    assert.deepEqual(run.scores, scoreAnswer(task, run.answer, null));
  } else {
    failures += 1;
  }
}

for (const task of tasks) {
  for (const arm of ["native", "sanjaya_available"]) {
    for (let repetition = 1; repetition <= 3; repetition += 1) {
      assert.ok(
        identities.has(`${task.id}|${arm}|${repetition}`),
        `Missing ${task.id}|${arm}|${repetition}.`,
      );
    }
  }
}

const serializedResults = runFiles
  .map((file) => readFileSync(join(runsRoot, file), "utf8"))
  .join("\n");
const macHomePrefix = ["", "Users", ""].join("/");
const windowsHomePrefix = ["C:", "Users", ""].join("\\");
assert.ok(!serializedResults.includes(macHomePrefix));
assert.ok(!serializedResults.includes(windowsHomePrefix));

console.log(
  `Verified 72 run records, ${completed} completed sessions, ${failures} `
  + "retained failures, strict rescoring, trace digests, and privacy guards.",
);

function sha256(value) {
  return createHash("sha256").update(value, "utf8").digest("hex");
}

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function formatErrors(errors) {
  return errors?.map((error) => `${error.instancePath} ${error.message}`).join("; ")
    ?? "unknown schema error";
}
