import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import {
  mkdirSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  SCORER_VERSION as SCORER_V1_VERSION,
  scoreAnswer,
} from "./scorer.mjs";
import {
  SCORER_VERSION as SCORER_V1_1_VERSION,
  scoreAnswerV1_1,
} from "./scorer-v1.1.mjs";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const resultsRoot = join(evalRoot, "results", "v0.1.2");
const outputRoot = join(resultsRoot, "reanalysis-scorer-v1.1");
const tasksPath = join(evalRoot, "tasks", "pilot.json");
const tasksText = readFileSync(tasksPath, "utf8");
const tasks = JSON.parse(tasksText).tasks;
const taskById = new Map(tasks.map((task) => [task.id, task]));
const pilotProtocolText = readFileSync(
  join(evalRoot, "protocol", "pilot.json"),
  "utf8",
);
const guidedProtocolText = readFileSync(
  join(evalRoot, "protocol", "guided.json"),
  "utf8",
);
const pilotProtocol = JSON.parse(pilotProtocolText);
const guidedProtocol = JSON.parse(guidedProtocolText);
const guidedTaskIds = new Set(guidedProtocol.design.taskIds);
const pilot = loadStudy("pilot");
const guided = loadStudy("guided");
const allRecords = [...pilot.records, ...guided.records];

assert.equal(
  pilot.records.length,
  pilotProtocol.design.totalRuns,
  "The frozen pilot must retain every planned record.",
);
assert.equal(
  guided.records.length,
  guidedProtocol.design.totalRuns,
  "The frozen guided diagnostic must retain every planned record.",
);

const scoredRecords = allRecords.map(({ study, run }) =>
  scoreRecord(study, run)
);
const pilotScored = scoredRecords.filter((record) => record.study === "pilot");
const guidedScored = scoredRecords.filter(
  (record) => record.study === "guided",
);
const pilotNative = pilotScored.filter((record) => record.arm === "native");
const pilotAvailable = pilotScored.filter(
  (record) => record.arm === "sanjaya_available",
);
const matchingNative = pilotNative.filter(
  (record) => guidedTaskIds.has(record.taskId),
);
const guidedTreatment = guidedScored.filter(
  (record) => record.arm === "sanjaya_guided",
);
assert.equal(pilotNative.length, 36, "Expected 36 native pilot records.");
assert.equal(
  pilotAvailable.length,
  36,
  "Expected 36 Sanjaya-available pilot records.",
);
assert.equal(
  matchingNative.length,
  18,
  "Expected 18 matching native diagnostic records.",
);
assert.equal(
  guidedTreatment.length,
  18,
  "Expected 18 Sanjaya-guided diagnostic records.",
);
assert.equal(
  new Set(scoredRecords.map((record) => record.runId)).size,
  scoredRecords.length,
  "Run IDs must be unique across the frozen inputs.",
);
const inputFingerprint = fingerprint([
  ["tasks/pilot.json", tasksText],
  ["protocol/pilot.json", pilotProtocolText],
  ["protocol/guided.json", guidedProtocolText],
  ...pilot.inputs,
  ...guided.inputs,
]);
const summary = {
  schemaVersion: "1.0",
  status: "post_pilot_additive_methodology_repair",
  package: "sanjaya-mcp@0.1.2",
  model: "gpt-5.6-terra",
  agent: "codex-cli 0.144.5",
  effort: "medium",
  methodology: {
    originalScorer: SCORER_V1_VERSION,
    correctedScorer: SCORER_V1_1_VERSION,
    originalResultsOverwritten: false,
    newModelCalls: 0,
    semanticJudgeUsed: false,
    reviewProtocol: "independent_arm_blind",
  },
  input: {
    fingerprintAlgorithm: "sha256",
    fingerprint: inputFingerprint,
    modelRecords: scoredRecords.length,
    completedRecords: scoredRecords.filter(
      (record) => record.status === "completed",
    ).length,
    retainedFailures: scoredRecords.filter(
      (record) => record.status !== "completed",
    ).length,
  },
  headlineAvailability: {
    relationship: "post_pilot_rescore_of_original_headline",
    treatmentRunsUsingSanjaya: pilotAvailable.filter(
      (record) => record.sanjayaToolCalls > 0,
    ).length,
    native: summarize(pilotNative),
    sanjayaAvailable: summarize(pilotAvailable),
    completedPairs: summarizePairs(
      completedPairs(pilotNative, pilotAvailable),
    ),
    tasks: taskRows(pilotNative, pilotAvailable),
  },
  guidedDiagnostic: {
    relationship: "post_pilot_rescore_of_separate_diagnostic",
    guidedRunsUsingSanjaya: guidedTreatment.filter(
      (record) => record.sanjayaToolCalls > 0,
    ).length,
    matchingNative: summarize(matchingNative),
    sanjayaGuided: summarize(guidedTreatment),
    completedPairs: summarizePairs(
      completedPairs(matchingNative, guidedTreatment),
    ),
    tasks: taskRows(matchingNative, guidedTreatment),
  },
};
const scoreDocument = {
  schemaVersion: "1.0",
  status: summary.status,
  inputFingerprint,
  scorerVersions: [SCORER_V1_VERSION, SCORER_V1_1_VERSION],
  records: scoredRecords,
};
const outputs = new Map([
  ["summary.json", `${JSON.stringify(summary, null, 2)}\n`],
  ["scores.json", `${JSON.stringify(scoreDocument, null, 2)}\n`],
  ["REPORT.md", report(summary)],
]);

if (process.argv.includes("--check")) {
  for (const [file, expected] of outputs) {
    assert.equal(
      readFileSync(join(outputRoot, file), "utf8"),
      expected,
      `${file} is not the reproducible scorer v1.1 reanalysis output.`,
    );
  }
  console.log(
    `Verified scorer v1.1 reanalysis of ${summary.input.modelRecords} frozen `
    + `records (${summary.input.completedRecords} completed, `
    + `${summary.input.retainedFailures} retained failures).`,
  );
} else {
  mkdirSync(outputRoot, { recursive: true });
  for (const [file, content] of outputs) {
    writeFileSync(join(outputRoot, file), content, "utf8");
  }
  console.log(JSON.stringify(summary, null, 2));
}

function loadStudy(study) {
  const runsRoot = join(resultsRoot, study, "runs");
  const files = readdirSync(runsRoot)
    .filter((file) => file.endsWith(".json"))
    .sort();
  return {
    records: files.map((file) => ({
      study,
      run: readJson(join(runsRoot, file)),
    })),
    inputs: files.map((file) => [
      `${study}/runs/${file}`,
      readFileSync(join(runsRoot, file), "utf8"),
    ]),
  };
}

function scoreRecord(study, run) {
  const task = taskById.get(run.taskId);
  assert.ok(task, `${run.runId} references an unknown task.`);
  if (run.status !== "completed") {
    assert.equal(run.answer, null, `${run.runId} failure retained an answer.`);
    assert.equal(run.scores, null, `${run.runId} failure retained scores.`);
    return {
      study,
      runId: run.runId,
      taskId: run.taskId,
      arm: run.arm,
      repetition: run.repetition,
      status: run.status,
      sanjayaToolCalls: run.metrics.sanjayaToolCalls,
      scorerV1_0: null,
      scorerV1_1: null,
    };
  }

  const original = scoreAnswer(task, run.answer, null);
  assert.deepEqual(
    original,
    run.scores,
    `${run.runId} no longer reproduces its frozen scorer v1.0 result.`,
  );
  return {
    study,
    runId: run.runId,
    taskId: run.taskId,
    arm: run.arm,
    repetition: run.repetition,
    status: run.status,
    sanjayaToolCalls: run.metrics.sanjayaToolCalls,
    scorerV1_0: original,
    scorerV1_1: scoreAnswerV1_1(task, run.answer, null),
  };
}

function summarize(records) {
  const completed = records.filter((record) => record.status === "completed");
  const v1Strict = completed.filter(
    (record) => record.scorerV1_0.strictSuccess,
  ).length;
  const v1_1Strict = completed.filter(
    (record) => record.scorerV1_1.strictSuccess,
  ).length;
  return {
    planned: records.length,
    completed: completed.length,
    retainedFailures: records.length - completed.length,
    scorerV1_0: {
      strictSuccesses: v1Strict,
      strictSuccessRatePlanned: divide(v1Strict, records.length),
      meanClaimF1Completed: mean(
        completed.map((record) => record.scorerV1_0.claimF1),
      ),
      meanCitationValidityCompleted: mean(
        completed.map((record) => record.scorerV1_0.citationValidity),
      ),
    },
    scorerV1_1: {
      strictSuccesses: v1_1Strict,
      strictSuccessRatePlanned: divide(v1_1Strict, records.length),
      meanClaimF1Completed: mean(
        completed.map((record) => record.scorerV1_1.claimF1),
      ),
      meanCitationValidityCompleted: mean(
        completed.map((record) => record.scorerV1_1.citationValidity),
      ),
    },
    transitions: {
      gainedStrictSuccess: completed.filter(
        (record) =>
          !record.scorerV1_0.strictSuccess
          && record.scorerV1_1.strictSuccess,
      ).length,
      lostStrictSuccess: completed.filter(
        (record) =>
          record.scorerV1_0.strictSuccess
          && !record.scorerV1_1.strictSuccess,
      ).length,
      unchangedStrictSuccess: completed.filter(
        (record) =>
          record.scorerV1_0.strictSuccess
          && record.scorerV1_1.strictSuccess,
      ).length,
      unchangedStrictFailure: completed.filter(
        (record) =>
          !record.scorerV1_0.strictSuccess
          && !record.scorerV1_1.strictSuccess,
      ).length,
    },
  };
}

function completedPairs(leftRecords, rightRecords) {
  const leftByIdentity = new Map(
    leftRecords.map(
      (record) => [`${record.taskId}|${record.repetition}`, record],
    ),
  );
  return rightRecords.flatMap((right) => {
    const left = leftByIdentity.get(`${right.taskId}|${right.repetition}`);
    return left?.status === "completed" && right.status === "completed"
      ? [[left, right]]
      : [];
  });
}

function summarizePairs(pairs) {
  return {
    count: pairs.length,
    scorerV1_0: pairOutcome(pairs, "scorerV1_0"),
    scorerV1_1: pairOutcome(pairs, "scorerV1_1"),
  };
}

function pairOutcome(pairs, scorer) {
  return {
    leftStrictWins: pairs.filter(
      ([left, right]) =>
        left[scorer].strictSuccess && !right[scorer].strictSuccess,
    ).length,
    rightStrictWins: pairs.filter(
      ([left, right]) =>
        !left[scorer].strictSuccess && right[scorer].strictSuccess,
    ).length,
    strictTies: pairs.filter(
      ([left, right]) =>
        left[scorer].strictSuccess === right[scorer].strictSuccess,
    ).length,
    meanClaimF1RightMinusLeft: mean(
      pairs.map(
        ([left, right]) => right[scorer].claimF1 - left[scorer].claimF1,
      ),
    ),
  };
}

function taskRows(leftRecords, rightRecords) {
  const taskIds = [...new Set(
    [...leftRecords, ...rightRecords].map((record) => record.taskId),
  )].sort();
  return taskIds.map((taskId) => ({
    taskId,
    title: taskById.get(taskId).title,
    left: strictByTask(leftRecords, taskId),
    right: strictByTask(rightRecords, taskId),
  }));
}

function strictByTask(records, taskId) {
  const selected = records.filter((record) => record.taskId === taskId);
  return {
    planned: selected.length,
    scorerV1_0: selected.filter(
      (record) => record.scorerV1_0?.strictSuccess,
    ).length,
    scorerV1_1: selected.filter(
      (record) => record.scorerV1_1?.strictSuccess,
    ).length,
  };
}

function report(document) {
  const headline = document.headlineAvailability;
  const guidedResult = document.guidedDiagnostic;
  const nativeV1 = fraction(headline.native, "scorerV1_0");
  const nativeV1_1 = fraction(headline.native, "scorerV1_1");
  const availableV1 = fraction(headline.sanjayaAvailable, "scorerV1_0");
  const availableV1_1 = fraction(headline.sanjayaAvailable, "scorerV1_1");
  const matchingNativeV1 = fraction(
    guidedResult.matchingNative,
    "scorerV1_0",
  );
  const matchingNativeV1_1 = fraction(
    guidedResult.matchingNative,
    "scorerV1_1",
  );
  const guidedV1 = fraction(guidedResult.sanjayaGuided, "scorerV1_0");
  const guidedV1_1 = fraction(guidedResult.sanjayaGuided, "scorerV1_1");
  const headlineTasks = document.headlineAvailability.tasks.map(
    (task) =>
      `| ${task.taskId} | ${task.left.scorerV1_0}/${task.left.planned} | `
      + `${task.left.scorerV1_1}/${task.left.planned} | `
      + `${task.right.scorerV1_0}/${task.right.planned} | `
      + `${task.right.scorerV1_1}/${task.right.planned} |`,
  ).join("\n");
  const guidedTasks = document.guidedDiagnostic.tasks.map(
    (task) =>
      `| ${task.taskId} | ${task.left.scorerV1_0}/${task.left.planned} | `
      + `${task.left.scorerV1_1}/${task.left.planned} | `
      + `${task.right.scorerV1_0}/${task.right.planned} | `
      + `${task.right.scorerV1_1}/${task.right.planned} |`,
  ).join("\n");
  return `# Sanjaya v0.1.2 scorer v1.1 reanalysis

Status: post-pilot, additive methodology repair; not a preregistered result.

## Guardrails

- No model was called and no answer was regenerated.
- All 90 original run records were read unchanged.
- The 3 original harness failures remain in their planned denominators.
- Every completed run first reproduced its frozen scorer 1.0.0 result.
- Scorer 1.1.0 was then applied symmetrically to every arm.
- The original [pilot](../pilot/REPORT.md) and
  [guided](../guided/REPORT.md) artifacts remain unchanged.
- This deterministic scorer does not use an LLM judge or infer paraphrases.

Input fingerprint: \`${document.input.fingerprint}\`

## Verdict

Scorer 1.0.0 materially under-counted answers that included a canonical value
inside ordinary explanatory formatting. Scorer 1.1.0 raises measured absolute
accuracy in both headline arms, but it does not create evidence that simply
making Sanjaya available improved performance.

The headline native arm changes from ${nativeV1} to ${nativeV1_1} strict successes.
The Sanjaya-available arm changes from ${availableV1} to ${availableV1_1}.
None of the ${headline.sanjayaAvailable.completed} completed availability sessions called Sanjaya,
so this remains a zero-uptake comparison rather than a test of active Sanjaya use.

Across ${headline.completedPairs.count} completed headline pairs, scorer 1.1.0 finds
${headline.completedPairs.scorerV1_1.rightStrictWins} availability-favoring pairs,
${headline.completedPairs.scorerV1_1.leftStrictWins} native-favoring pairs, and
${headline.completedPairs.scorerV1_1.strictTies} ties. The paired mean claim-F1 delta
(available minus native) is
${signed(headline.completedPairs.scorerV1_1.meanClaimF1RightMinusLeft)}.

## Headline availability comparison

| Measure | Native 1.0 | Native 1.1 | Available 1.0 | Available 1.1 |
|---|---:|---:|---:|---:|
| Strict success / planned | ${fraction(headline.native, "scorerV1_0")} | ${fraction(headline.native, "scorerV1_1")} | ${fraction(headline.sanjayaAvailable, "scorerV1_0")} | ${fraction(headline.sanjayaAvailable, "scorerV1_1")} |
| Mean claim F1 / completed | ${format(headline.native.scorerV1_0.meanClaimF1Completed)} | ${format(headline.native.scorerV1_1.meanClaimF1Completed)} | ${format(headline.sanjayaAvailable.scorerV1_0.meanClaimF1Completed)} | ${format(headline.sanjayaAvailable.scorerV1_1.meanClaimF1Completed)} |
| Mean citation validity / completed | ${format(headline.native.scorerV1_0.meanCitationValidityCompleted)} | ${format(headline.native.scorerV1_1.meanCitationValidityCompleted)} | ${format(headline.sanjayaAvailable.scorerV1_0.meanCitationValidityCompleted)} | ${format(headline.sanjayaAvailable.scorerV1_1.meanCitationValidityCompleted)} |

Planned/completed/failure accounting is ${headline.native.planned}/${headline.native.completed}/${headline.native.retainedFailures}
for native and ${headline.sanjayaAvailable.planned}/${headline.sanjayaAvailable.completed}/${headline.sanjayaAvailable.retainedFailures}
for available.

### Task-level strict results

| Task | Native 1.0 | Native 1.1 | Available 1.0 | Available 1.1 |
|---|---:|---:|---:|---:|
${headlineTasks}

## Guided diagnostic

The separate guided diagnostic successfully caused all
${guidedResult.guidedRunsUsingSanjaya} treatment sessions to use Sanjaya.
Its matching native records change from ${matchingNativeV1} to
${matchingNativeV1_1} strict successes, while guided records change from
${guidedV1} to ${guidedV1_1}.

Across ${guidedResult.completedPairs.count} completed guided pairs, scorer 1.1.0 finds
${guidedResult.completedPairs.scorerV1_1.rightStrictWins} guided-favoring pairs,
${guidedResult.completedPairs.scorerV1_1.leftStrictWins} native-favoring pairs, and
${guidedResult.completedPairs.scorerV1_1.strictTies} ties. The paired mean claim-F1
delta (guided minus native) is
${signed(guidedResult.completedPairs.scorerV1_1.meanClaimF1RightMinusLeft)}.

| Measure | Native 1.0 | Native 1.1 | Guided 1.0 | Guided 1.1 |
|---|---:|---:|---:|---:|
| Strict success / planned | ${fraction(guidedResult.matchingNative, "scorerV1_0")} | ${fraction(guidedResult.matchingNative, "scorerV1_1")} | ${fraction(guidedResult.sanjayaGuided, "scorerV1_0")} | ${fraction(guidedResult.sanjayaGuided, "scorerV1_1")} |
| Mean claim F1 / completed | ${format(guidedResult.matchingNative.scorerV1_0.meanClaimF1Completed)} | ${format(guidedResult.matchingNative.scorerV1_1.meanClaimF1Completed)} | ${format(guidedResult.sanjayaGuided.scorerV1_0.meanClaimF1Completed)} | ${format(guidedResult.sanjayaGuided.scorerV1_1.meanClaimF1Completed)} |
| Mean citation validity / completed | ${format(guidedResult.matchingNative.scorerV1_0.meanCitationValidityCompleted)} | ${format(guidedResult.matchingNative.scorerV1_1.meanCitationValidityCompleted)} | ${format(guidedResult.sanjayaGuided.scorerV1_0.meanCitationValidityCompleted)} | ${format(guidedResult.sanjayaGuided.scorerV1_1.meanCitationValidityCompleted)} |

### Task-level strict results

| Task | Native 1.0 | Native 1.1 | Guided 1.0 | Guided 1.1 |
|---|---:|---:|---:|---:|
${guidedTasks}

## Interpretation boundary

This repair improves the measurement contract, not the product. It supports
three narrow conclusions:

1. scorer 1.0.0 was too brittle for explanatory structured answers;
2. the corrected headline scores remain nearly symmetric and still contain
   zero Sanjaya adoption; and
3. the guided diagnostic shows adoption, but does not establish a broad
   accuracy or efficiency advantage.

The next product experiment should improve capability-aware orchestration
before spending money on another model run.
`;
}

function fraction(cohort, scorer) {
  return `${cohort[scorer].strictSuccesses}/${cohort.planned}`;
}

function fingerprint(inputs) {
  const hash = createHash("sha256");
  for (const [name, content] of inputs) {
    hash.update(`${name}\0${Buffer.byteLength(content, "utf8")}\0`, "utf8");
    hash.update(content, "utf8");
  }
  return hash.digest("hex");
}

function mean(values) {
  return values.length === 0
    ? 0
    : values.reduce((total, value) => total + value, 0) / values.length;
}

function divide(numerator, denominator) {
  return denominator === 0 ? 0 : numerator / denominator;
}

function format(value) {
  return value.toFixed(3);
}

function signed(value) {
  return `${value >= 0 ? "+" : ""}${format(value)}`;
}

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}
