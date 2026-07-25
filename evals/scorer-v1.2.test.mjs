import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  CLAIM_AMENDMENTS_V1_2,
  SCORER_VERSION,
  amendTaskV1_2,
  scoreAnswerV1_2,
  valueMatchesV1_2,
} from "./scorer-v1.2.mjs";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const fixture = JSON.parse(
  readFileSync(
    join(evalRoot, "fixtures", "scorer-v1.2", "cases.json"),
    "utf8",
  ),
);

assert.equal(SCORER_VERSION, "1.2.0");
assert.equal(fixture.scorerVersion, SCORER_VERSION);
assert.equal(
  new Set(fixture.cases.map((testCase) => testCase.id)).size,
  fixture.cases.length,
  "Fixture IDs must be unique.",
);
assert.ok(
  fixture.cases.some((testCase) => testCase.sourceKind === "observed_arm_hidden"),
);
assert.ok(
  fixture.cases.some((testCase) => testCase.sourceKind === "semantic_holdout"),
);

for (const testCase of fixture.cases) {
  const actual = valueMatchesV1_2(
    {
      matchMode: testCase.matchMode,
      acceptedValues: testCase.acceptedValues,
    },
    testCase.suppliedValue,
  );
  assert.equal(
    actual,
    testCase.expectedMatch,
    `${testCase.id}: ${testCase.rationale}`,
  );
}

const tasks = JSON.parse(
  readFileSync(join(evalRoot, "tasks", "pilot.json"), "utf8"),
).tasks;
const taskById = new Map(tasks.map((task) => [task.id, task]));

for (const key of CLAIM_AMENDMENTS_V1_2.keys()) {
  const [taskId, claimKey] = key.split(":");
  const task = taskById.get(taskId);
  assert.ok(task, `${key} amends an unknown task.`);
  assert.ok(
    task.groundTruth.requiredClaims.some((claim) => claim.key === claimKey),
    `${key} amends an unknown claim key.`,
  );
}

for (const task of tasks) {
  const amended = amendTaskV1_2(task);
  for (const [index, claim] of task.groundTruth.requiredClaims.entries()) {
    const amendedClaim = amended.groundTruth.requiredClaims[index];
    if (CLAIM_AMENDMENTS_V1_2.has(`${task.id}:${claim.key}`)) {
      for (const original of claim.acceptedValues) {
        assert.equal(
          valueMatchesV1_2(amendedClaim, original),
          true,
          `${task.id}:${claim.key} must keep accepting its frozen phrase.`,
        );
      }
    } else {
      assert.deepEqual(
        amendedClaim,
        claim,
        `${task.id}:${claim.key} must be untouched by the amendment table.`,
      );
    }
  }
}

const amended0010 = amendTaskV1_2(taskById.get("SJ-EVAL-0010"));
const cleanupClaim = amended0010.groundTruth.requiredClaims.find(
  (claim) => claim.key === "child_cleanup",
);
const scored = scoreAnswerV1_2(
  {
    groundTruth: {
      requiredClaims: [{ ...cleanupClaim, acceptableEvidence: [] }],
      forbiddenClaims: [],
    },
  },
  {
    claims: [
      {
        key: "child_cleanup",
        value: "In finally, ends childProcess.stdin (when present) and calls childProcess.kill().",
        evidence: [],
      },
    ],
  },
  null,
);
assert.equal(scored.strictSuccess, true);
assert.equal(scored.claimRecall, 1);

console.log(
  `Verified scorer ${SCORER_VERSION} against ${fixture.cases.length} `
  + "arm-hidden and holdout fixtures, frozen-phrase additivity, "
  + "and amendment-table integrity.",
);