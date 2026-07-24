import assert from "node:assert/strict";
import {
  mkdtempSync,
  readFileSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from "node:fs";
import {
  dirname,
  join,
  resolve,
} from "node:path";
import { tmpdir } from "node:os";
import { fileURLToPath } from "node:url";
import {
  SCORER_VERSION,
  scoreAnswerV1_1,
  valueMatchesV1_1,
} from "./scorer-v1.1.mjs";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const fixture = JSON.parse(
  readFileSync(
    join(evalRoot, "fixtures", "scorer-v1.1", "cases.json"),
    "utf8",
  ),
);

assert.equal(SCORER_VERSION, "1.1.0");
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
  const actual = valueMatchesV1_1(
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

const task = {
  groundTruth: {
    requiredClaims: [
      {
        key: "target",
        matchMode: "exact",
        acceptedValues: ["Correct.Type"],
        acceptableEvidence: [
          { path: "source.cs", startLine: 2, endLine: 4 },
        ],
      },
    ],
    forbiddenClaims: [
      {
        severity: "critical",
        claimKey: "target",
        rejectedValues: ["Wrong.Type"],
      },
    ],
  },
};

const explanatory = scoreAnswerV1_1(task, {
  claims: [
    {
      key: "target",
      value: "`Correct.Type` is the production implementation.",
      evidence: [{ path: "source.cs", startLine: 3, endLine: 3 }],
    },
  ],
}, null);
assert.equal(explanatory.strictSuccess, true);
assert.equal(explanatory.claimPrecision, 1);
assert.equal(explanatory.claimRecall, 1);
assert.equal(explanatory.citationValidity, 1);

const forbidden = scoreAnswerV1_1(task, {
  claims: [
    {
      key: "target",
      value: "The implementation is Wrong.Type.",
      evidence: [{ path: "source.cs", startLine: 3, endLine: 3 }],
    },
  ],
}, null);
assert.equal(forbidden.strictSuccess, false);
assert.equal(forbidden.criticalForbiddenClaims, 1);

const duplicate = scoreAnswerV1_1(task, {
  claims: [
    {
      key: "target",
      value: "Correct.Type",
      evidence: [{ path: "source.cs", startLine: 3, endLine: 3 }],
    },
    {
      key: "target",
      value: "Correct.Type",
      evidence: [{ path: "source.cs", startLine: 3, endLine: 3 }],
    },
  ],
}, null);
assert.equal(duplicate.strictSuccess, false);
assert.equal(duplicate.claimPrecision, 0);
assert.equal(duplicate.claimRecall, 0);
assert.equal(duplicate.citationValidity, 0);

const repositoryRoot = mkdtempSync(join(tmpdir(), "sanjaya-scorer-root-"));
const outsideRoot = mkdtempSync(join(tmpdir(), "sanjaya-scorer-outside-"));
try {
  writeFileSync(
    join(repositoryRoot, "source.cs"),
    "namespace Example;\nclass CorrectType\n{\n}\n",
    "utf8",
  );
  const verifiedCitation = scoreAnswerV1_1(task, {
    claims: [
      {
        key: "target",
        value: "Correct.Type",
        evidence: [{ path: "source.cs", startLine: 3, endLine: 3 }],
      },
    ],
  }, repositoryRoot);
  assert.equal(verifiedCitation.strictSuccess, true);

  writeFileSync(join(outsideRoot, "outside.cs"), "secret\n", "utf8");
  symlinkSync(join(outsideRoot, "outside.cs"), join(repositoryRoot, "linked.cs"));
  const linkedTask = structuredClone(task);
  linkedTask.groundTruth.requiredClaims[0].acceptableEvidence = [
    { path: "linked.cs", startLine: 1, endLine: 1 },
  ];
  const escapedCitation = scoreAnswerV1_1(linkedTask, {
    claims: [
      {
        key: "target",
        value: "Correct.Type",
        evidence: [{ path: "linked.cs", startLine: 1, endLine: 1 }],
      },
    ],
  }, repositoryRoot);
  assert.equal(escapedCitation.strictSuccess, false);
  assert.equal(escapedCitation.citationValidity, 0);
} finally {
  rmSync(repositoryRoot, { recursive: true, force: true });
  rmSync(outsideRoot, { recursive: true, force: true });
}

console.log(
  `Verified scorer ${SCORER_VERSION} against ${fixture.cases.length} `
  + "arm-hidden and adversarial fixtures, citations, forbidden values, "
  + "duplicate-claim rejection, and real-path containment.",
);
