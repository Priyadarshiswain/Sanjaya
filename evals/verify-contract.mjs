import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import {
  readFileSync,
  readdirSync,
  statSync,
} from "node:fs";
import {
  dirname,
  join,
  relative,
  resolve,
} from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const schemaRoot = join(evalRoot, "schemas");
const exampleRoot = join(evalRoot, "examples");

const schemaFiles = [
  "answer.schema.json",
  "repository-manifest.schema.json",
  "task.schema.json",
  "trace-event.schema.json",
  "run.schema.json",
];

const schemas = new Map(
  schemaFiles.map((file) => [file, readJson(join(schemaRoot, file))]),
);

const ajv = new Ajv2020({
  allErrors: true,
  strict: true,
});

for (const [file, schema] of schemas) {
  assert.equal(
    schema.$schema,
    "https://json-schema.org/draft/2020-12/schema",
    `${file} must use JSON Schema 2020-12.`,
  );
  assert.equal(
    schema.$id,
    `https://raw.githubusercontent.com/Priyadarshiswain/Sanjaya/main/evals/schemas/${file}`,
    `${file} has an unexpected public identifier.`,
  );
  ajv.addSchema(schema);
}

const validators = new Map(
  schemaFiles.map((file) => {
    const schema = schemas.get(file);
    const validator = ajv.getSchema(schema.$id);
    assert.ok(validator, `${file} did not compile.`);
    return [file, validator];
  }),
);

const task = readJson(join(exampleRoot, "task.example.json"));
const answer = readJson(join(exampleRoot, "answer.example.json"));
const run = readJson(join(exampleRoot, "run.example.json"));
const manifest = readJson(join(exampleRoot, "repository-manifest.example.json"));
const traceEvents = readJsonLines(join(exampleRoot, "trace.example.jsonl"));

assertValid("task.schema.json", task, "task example");
assertValid("answer.schema.json", answer, "answer example");
assertValid("run.schema.json", run, "run example");
assertValid(
  "repository-manifest.schema.json",
  manifest,
  "repository manifest example",
);

for (const [index, event] of traceEvents.entries()) {
  assertValid(
    "trace-event.schema.json",
    event,
    `trace event ${index + 1}`,
  );
  assert.equal(
    event.sequence,
    index + 1,
    "Trace sequence must be contiguous and one-based.",
  );
}

assertUnique(
  task.groundTruth.requiredClaims.map((claim) => claim.key),
  "Task required claim keys",
);
assertUnique(
  answer.claims.map((claim) => claim.key),
  "Answer claim keys",
);
assertUnique(
  manifest.repositories.map((repository) => repository.id),
  "Repository manifest IDs",
);
assertLineRanges(task);
assertLineRanges(answer);
assertLineRanges(run);

assert.equal(answer.taskId, task.id, "Answer example must target the task example.");
assert.equal(run.taskId, task.id, "Run example must target the task example.");
assert.equal(
  run.answer.taskId,
  run.taskId,
  "Nested run answer must target the run task.",
);
assert.equal(
  run.trace.eventCount,
  traceEvents.length,
  "Run trace count must match the JSONL example.",
);
assert.equal(
  run.trace.sha256,
  sha256(join(exampleRoot, "trace.example.jsonl")),
  "Run trace digest must match the exact JSONL example.",
);
assert.ok(
  run.metrics.nativeToolCalls + run.metrics.sanjayaToolCalls
    <= run.metrics.toolCalls,
  "Tool-family counts cannot exceed total tool calls.",
);

const taskRepository = manifest.repositories.find(
  (repository) => repository.id === task.repository.id,
);
assert.ok(taskRepository, "Task repository must exist in the manifest.");
assert.equal(
  taskRepository.commit,
  task.repository.commit,
  "Task and manifest commit must match.",
);
assert.equal(
  taskRepository.sizeBand,
  task.sizeBand,
  "Task and manifest size band must match.",
);

for (const repository of manifest.repositories) {
  if (repository.originKind === "public_git") {
    const expectedBand = sizeBand(repository.size.sourceFiles);
    assert.equal(
      repository.sizeBand,
      expectedBand,
      `${repository.id} size band does not match its source-file count.`,
    );
  }
}

expectInvalid(
  "task.schema.json",
  mutate(task, (value) => {
    value.repository.commit = "main";
  }),
  "moving repository reference",
);
expectInvalid(
  "task.schema.json",
  mutate(task, (value) => {
    value.groundTruth.requiredClaims[0].acceptableEvidence[0].path
      = "/private/RetryPolicy.cs";
  }),
  "absolute ground-truth path",
);
expectInvalid(
  "answer.schema.json",
  mutate(answer, (value) => {
    value.claims[0].evidence[0].path = "../outside.cs";
  }),
  "traversal answer path",
);
expectInvalid(
  "answer.schema.json",
  mutate(answer, (value) => {
    value.claims[0].evidence[0].path = "src/Relay/..";
  }),
  "terminal traversal answer path",
);
expectInvalid(
  "run.schema.json",
  mutate(run, (value) => {
    value.controls.networkAccess = true;
  }),
  "network-enabled measured run",
);
expectInvalid(
  "run.schema.json",
  mutate(run, (value) => {
    value.rawTranscript = "unreviewed";
  }),
  "unreviewed raw transcript field",
);
expectInvalid(
  "repository-manifest.schema.json",
  mutate(manifest, (value) => {
    value.repositories[1].commit = "latest";
  }),
  "moving public repository reference",
);
expectInvalid(
  "trace-event.schema.json",
  {
    ...traceEvents[0],
    content: "raw source content is not part of the compact trace contract",
  },
  "raw trace content",
);

for (const file of listFiles(evalRoot)) {
  if (file.includes(`${join(evalRoot, "node_modules")}`)) {
    continue;
  }
  const source = readFileSync(file, "utf8");
  const macHomePrefix = ["", "Users", ""].join("/");
  const windowsHomePrefix = ["C:", "Users", ""].join("\\");
  assert.ok(
    !source.includes(macHomePrefix) && !source.includes(windowsHomePrefix),
    `${relative(evalRoot, file)} contains a machine-specific home path.`,
  );
}

console.log(
  `Verified ${schemaFiles.length} eval schemas, `
  + `${4 + traceEvents.length} examples, cross-record invariants, `
  + "privacy guards, and representative rejection cases without running a model.",
);

function assertValid(schemaFile, value, label) {
  const validator = validators.get(schemaFile);
  const valid = validator(value);
  assert.ok(valid, `${label} failed ${schemaFile}: ${formatErrors(validator.errors)}`);
}

function expectInvalid(schemaFile, value, label) {
  const validator = validators.get(schemaFile);
  assert.equal(
    validator(value),
    false,
    `${label} was unexpectedly accepted by ${schemaFile}.`,
  );
}

function mutate(value, action) {
  const copy = structuredClone(value);
  action(copy);
  return copy;
}

function assertUnique(values, label) {
  assert.equal(new Set(values).size, values.length, `${label} must be unique.`);
}

function assertLineRanges(value) {
  if (Array.isArray(value)) {
    for (const item of value) {
      assertLineRanges(item);
    }
    return;
  }
  if (!value || typeof value !== "object") {
    return;
  }
  if (
    Number.isInteger(value.startLine)
    && Number.isInteger(value.endLine)
  ) {
    assert.ok(
      value.endLine >= value.startLine,
      "Evidence endLine must be greater than or equal to startLine.",
    );
  }
  for (const child of Object.values(value)) {
    assertLineRanges(child);
  }
}

function sizeBand(sourceFiles) {
  if (sourceFiles < 500) {
    return "small";
  }
  if (sourceFiles < 5000) {
    return "medium";
  }
  return "large";
}

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function readJsonLines(path) {
  return readFileSync(path, "utf8")
    .split(/\r?\n/u)
    .filter((line) => line.length > 0)
    .map((line) => JSON.parse(line));
}

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

function listFiles(root) {
  const files = [];
  for (const entry of readdirSync(root)) {
    const path = join(root, entry);
    if (statSync(path).isDirectory()) {
      files.push(...listFiles(path));
    } else {
      files.push(path);
    }
  }
  return files;
}

function formatErrors(errors) {
  return (errors ?? [])
    .map((error) => `${error.instancePath || "/"} ${error.message}`)
    .join("; ");
}
