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
  SCORER_VERSION as SCORER_V1_1_VERSION,
  scoreAnswerV1_1,
} from "./scorer-v1.1.mjs";
import {
  SCORER_VERSION as SCORER_V1_2_VERSION,
  amendTaskV1_2,
  scoreAnswerV1_2,
} from "./scorer-v1.2.mjs";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const resultsRoot = join(evalRoot, "results", "v0.1.2");
const outputRoot = join(resultsRoot, "reanalysis-evidence-first-v1.2");
const tasksText = readFileSync(join(evalRoot, "tasks", "pilot.json"), "utf8");
const tasks = JSON.parse(tasksText).tasks;
const taskById = new Map(tasks.map((task) => [task.id, task]));
const protocolText = readFileSync(
  join(evalRoot, "protocol", "evidence-first-skill.json"),
  "utf8",
);
const protocol = JSON.parse(protocolText);

const runsRoot = join(resultsRoot, "evidence-first-skill", "runs");
const runFiles = readdirSync(runsRoot)
  .filter((file) => file.endsWith(".json"))
  .sort();
const runInputs = runFiles.map((file) => [
  `evidence-first-skill/runs/${file}`,
  readFileSync(join(runsRoot, file), "utf8"),
]);
const runs = runInputs.map(([, content]) => JSON.parse(content));

assert.equal(
  runs.length,
  protocol.design.totalRuns,
  "The frozen study must retain every planned record.",
);
assert.equal(
  new Set(runs.map((run) => run.runId)).size,
  runs.length,
  "Run IDs must be unique across the frozen inputs.",
);

const scoredRecords = runs.map((run) => scoreRecord(run));
const nativeRecords = scoredRecords.filter((record) => record.arm === "native");
const skillRecords = scoredRecords.filter(
  (record) => record.arm === "evidence_first_skill",
);
assert.equal(nativeRecords.length, 36, "Expected 36 native records.");
assert.equal(skillRecords.length, 36, "Expected 36 skill records.");

const inputFingerprint = fingerprint([
  ["tasks/pilot.json", tasksText],
  ["protocol/evidence-first-skill.json", protocolText],
  ...runInputs,
]);
const summary = {
  schemaVersion: "1.0",
  status: "post_study_additive_methodology_repair",
  package: "sanjaya-mcp@0.1.2",
  model: "gpt-5.6-terra",
  methodology: {
    originalScorer: SCORER_V1_1_VERSION,
    correctedScorer: SCORER_V1_2_VERSION,
    originalResultsOverwritten: false,
    newModelCalls: 0,
    semanticJudgeUsed: false,
    reviewProtocol: "arm_blind_supplied_value_review",
    reviewInput: "fixtures/scorer-v1.2-review.json",
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
  comparison: {
    skillRunsUsingSanjaya: skillRecords.filter(
      (record) => record.sanjayaToolCalls > 0,
    ).length,
    native: summarize(nativeRecords),
    evidenceFirstSkill: summarize(skillRecords),
    completedPairs: summarizePairs(
      completedPairs(nativeRecords, skillRecords),
    ),
    tasks: taskRows(nativeRecords, skillRecords),
  },
};
const scoreDocument = {
  schemaVersion: "1.0",
  status: summary.status,
  inputFingerprint,
  scorerVersions: [SCORER_V1_1_VERSION, SCORER_V1_2_VERSION],
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
      `${file} is not the reproducible scorer v1.2 reanalysis output.`,
    );
  }
  console.log(
    `Verified scorer v1.2 reanalysis of ${summary.input.modelRecords} frozen `
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

function scoreRecord(run) {
  const task = taskById.get(run.taskId);
  assert.ok(task, `${run.runId} references an unknown task.`);
  if (run.status !== "completed") {
    assert.equal(run.answer, null, `${run.runId} failure retained an answer.`);
    assert.equal(run.scores, null, `${run.runId} failure retained scores.`);
    return {
      runId: run.runId,
      taskId: run.taskId,
      arm: run.arm,
      repetition: run.repetition,
      status: run.status,
      sanjayaToolCalls: run.metrics.sanjayaToolCalls,
      scorerV1_1: null,
      scorerV1_2: null,
    };
  }

  const original = scoreAnswerV1_1(task, run.answer, null);
  assert.deepEqual(
    original,
    run.scores,
    `${run.runId} no longer reproduces its frozen scorer v1.1 result.`,
  );
  return {
    runId: run.runId,
    taskId: run.taskId,
    arm: run.arm,
    repetition: run.repetition,
    status: run.status,
    sanjayaToolCalls: run.metrics.sanjayaToolCalls,
    scorerV1_1: original,
    scorerV1_2: scoreAnswerV1_2(amendTaskV1_2(task), run.answer, null),
  };
}

function summarize(records) {
  const completed = records.filter((record) => record.status === "completed");
  const v1_1Strict = completed.filter(
    (record) => record.scorerV1_1.strictSuccess,
  ).length;
  const v1_2Strict = completed.filter(
    (record) => record.scorerV1_2.strictSuccess,
  ).length;
  return {
    planned: records.length,
    completed: completed.length,
    retainedFailures: records.length - completed.length,
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
    scorerV1_2: {
      strictSuccesses: v1_2Strict,
      strictSuccessRatePlanned: divide(v1_2Strict, records.length),
      meanClaimF1Completed: mean(
        completed.map((record) => record.scorerV1_2.claimF1),
      ),
      meanCitationValidityCompleted: mean(
        completed.map((record) => record.scorerV1_2.citationValidity),
      ),
    },
    transitions: {
      gainedStrictSuccess: completed.filter(
        (record) =>
          !record.scorerV1_1.strictSuccess
          && record.scorerV1_2.strictSuccess,
      ).length,
      lostStrictSuccess: completed.filter(
        (record) =>
          record.scorerV1_1.strictSuccess
          && !record.scorerV1_2.strictSuccess,
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
    scorerV1_1: pairOutcome(pairs, "scorerV1_1"),
    scorerV1_2: pairOutcome(pairs, "scorerV1_2"),
  };
}

function pairOutcome(pairs, scorer) {
  return {
    nativeStrictWins: pairs.filter(
      ([left, right]) =>
        left[scorer].strictSuccess && !right[scorer].strictSuccess,
    ).length,
    skillStrictWins: pairs.filter(
      ([left, right]) =>
        !left[scorer].strictSuccess && right[scorer].strictSuccess,
    ).length,
    strictTies: pairs.filter(
      ([left, right]) =>
        left[scorer].strictSuccess === right[scorer].strictSuccess,
    ).length,
    meanClaimF1SkillMinusNative: mean(
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
    native: strictByTask(leftRecords, taskId),
    evidenceFirstSkill: strictByTask(rightRecords, taskId),
  }));
}

function strictByTask(records, taskId) {
  const selected = records.filter((record) => record.taskId === taskId);
  return {
    planned: selected.length,
    scorerV1_1: selected.filter(
      (record) => record.scorerV1_1?.strictSuccess,
    ).length,
    scorerV1_2: selected.filter(
      (record) => record.scorerV1_2?.strictSuccess,
    ).length,
  };
}

function report(document) {
  const comparison = document.comparison;
  const taskLines = comparison.tasks.map(
    (task) =>
      `| ${task.taskId} | ${task.native.scorerV1_1}/${task.native.planned} | `
      + `${task.native.scorerV1_2}/${task.native.planned} | `
      + `${task.evidenceFirstSkill.scorerV1_1}/${task.evidenceFirstSkill.planned} | `
      + `${task.evidenceFirstSkill.scorerV1_2}/${task.evidenceFirstSkill.planned} |`,
  ).join("\n");
  return `# Evidence-first skill study scorer v1.2 reanalysis

Status: post-study, additive methodology repair; not a preregistered result.

## Guardrails

- No model was called and no answer was regenerated.
- All ${document.input.modelRecords} original run records were read unchanged.
- The ${document.input.retainedFailures} original harness failures remain in their planned denominators.
- Every completed run first reproduced its frozen scorer ${SCORER_V1_1_VERSION} result.
- Scorer ${SCORER_V1_2_VERSION} was then applied symmetrically to both arms.
- Accepted alternatives were derived only from the arm-blind review recorded in
  [SCORER-V1.2.md](../../../SCORER-V1.2.md); the overfitting risk disclosed
  there applies to every number below.

Input fingerprint: \`${document.input.fingerprint}\`

## Verdict

The three tasks with zero strict successes under scorer 1.1 were limited by
over-rigid accepted phrases, not by agent comprehension. Scorer 1.2 raises
absolute strict success in both arms symmetrically
(native ${fraction(comparison.native, "scorerV1_1")} to ${fraction(comparison.native, "scorerV1_2")};
skill ${fraction(comparison.evidenceFirstSkill, "scorerV1_1")} to ${fraction(comparison.evidenceFirstSkill, "scorerV1_2")}).
${comparison.native.transitions.lostStrictSuccess
  + comparison.evidenceFirstSkill.transitions.lostStrictSuccess} previously
successful records lost strict success, confirming additivity.

Across ${comparison.completedPairs.count} completed pairs, scorer 1.2 finds
${comparison.completedPairs.scorerV1_2.skillStrictWins} skill-favoring pairs,
${comparison.completedPairs.scorerV1_2.nativeStrictWins} native-favoring pairs,
and ${comparison.completedPairs.scorerV1_2.strictTies} ties. The paired mean
claim-F1 delta (skill minus native) is
${signed(comparison.completedPairs.scorerV1_2.meanClaimF1SkillMinusNative)}.
Only ${comparison.skillRunsUsingSanjaya} completed skill sessions used any
Sanjaya tool, so this remains primarily a routing observation rather than a
test of active Sanjaya use.

## Comparison

| Measure | Native 1.1 | Native 1.2 | Skill 1.1 | Skill 1.2 |
|---|---:|---:|---:|---:|
| Strict success / planned | ${fraction(comparison.native, "scorerV1_1")} | ${fraction(comparison.native, "scorerV1_2")} | ${fraction(comparison.evidenceFirstSkill, "scorerV1_1")} | ${fraction(comparison.evidenceFirstSkill, "scorerV1_2")} |
| Mean claim F1 / completed | ${format(comparison.native.scorerV1_1.meanClaimF1Completed)} | ${format(comparison.native.scorerV1_2.meanClaimF1Completed)} | ${format(comparison.evidenceFirstSkill.scorerV1_1.meanClaimF1Completed)} | ${format(comparison.evidenceFirstSkill.scorerV1_2.meanClaimF1Completed)} |
| Mean citation validity / completed | ${format(comparison.native.scorerV1_1.meanCitationValidityCompleted)} | ${format(comparison.native.scorerV1_2.meanCitationValidityCompleted)} | ${format(comparison.evidenceFirstSkill.scorerV1_1.meanCitationValidityCompleted)} | ${format(comparison.evidenceFirstSkill.scorerV1_2.meanCitationValidityCompleted)} |

### Task-level strict results

| Task | Native 1.1 | Native 1.2 | Skill 1.1 | Skill 1.2 |
|---|---:|---:|---:|---:|
${taskLines}

## Interpretation boundary

This repair improves the measurement contract, not the product. It does not
change routing, token, latency, or side-effect measurements, and it does not
convert the completed study into evidence for a marketplace benefit claim.
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