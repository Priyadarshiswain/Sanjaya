import assert from "node:assert/strict";
import { scoreAnswer } from "./scorer.mjs";

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

const correct = scoreAnswer(task, {
  claims: [
    {
      key: "target",
      value: " Correct.Type ",
      evidence: [{ path: "source.cs", startLine: 3, endLine: 3 }],
    },
  ],
}, null);
assert.equal(correct.strictSuccess, true);
assert.equal(correct.claimPrecision, 1);
assert.equal(correct.claimRecall, 1);
assert.equal(correct.citationValidity, 1);

const wrong = scoreAnswer(task, {
  claims: [
    {
      key: "target",
      value: "Wrong.Type",
      evidence: [{ path: "source.cs", startLine: 3, endLine: 3 }],
    },
    {
      key: "invented",
      value: "unsupported",
      evidence: [],
    },
  ],
}, null);
assert.equal(wrong.strictSuccess, false);
assert.equal(wrong.claimPrecision, 0);
assert.equal(wrong.claimRecall, 0);
assert.equal(wrong.criticalForbiddenClaims, 1);

const invalidCitation = scoreAnswer(task, {
  claims: [
    {
      key: "target",
      value: "Correct.Type",
      evidence: [{ path: "other.cs", startLine: 3, endLine: 3 }],
    },
  ],
}, null);
assert.equal(invalidCitation.strictSuccess, false);
assert.equal(invalidCitation.claimRecall, 1);
assert.equal(invalidCitation.citationValidity, 0);

console.log("Verified deterministic scorer matching, citations, and forbidden values.");
