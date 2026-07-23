import { readFileSync } from "node:fs";
import { resolve } from "node:path";

export const SCORER_VERSION = "1.0.0";

export function scoreAnswer(task, answer, repositoryRoot) {
  const requiredByKey = new Map(
    task.groundTruth.requiredClaims.map((claim) => [claim.key, claim]),
  );
  const answerByKey = new Map();
  for (const claim of answer.claims) {
    if (!answerByKey.has(claim.key)) {
      answerByKey.set(claim.key, claim);
    }
  }

  let correctAnswerClaims = 0;
  let matchedRequiredClaims = 0;
  let requiredCitationsSatisfied = 0;
  let requiredCitationsExpected = 0;
  let validCitations = 0;
  let totalCitations = 0;

  for (const claim of answer.claims) {
    const required = requiredByKey.get(claim.key);
    if (required && valueMatches(required, claim.value)) {
      correctAnswerClaims += 1;
    }
    for (const evidence of claim.evidence) {
      totalCitations += 1;
      if (
        required
        && evidenceMatches(required.acceptableEvidence, evidence)
        && evidenceExists(repositoryRoot, evidence)
      ) {
        validCitations += 1;
      }
    }
  }

  for (const required of task.groundTruth.requiredClaims) {
    const supplied = answerByKey.get(required.key);
    if (supplied && valueMatches(required, supplied.value)) {
      matchedRequiredClaims += 1;
      if (required.acceptableEvidence.length > 0) {
        requiredCitationsExpected += 1;
        if (
          supplied.evidence.some(
            (evidence) =>
              evidenceMatches(required.acceptableEvidence, evidence)
              && evidenceExists(repositoryRoot, evidence),
          )
        ) {
          requiredCitationsSatisfied += 1;
        }
      }
    } else if (required.acceptableEvidence.length > 0) {
      requiredCitationsExpected += 1;
    }
  }

  const criticalForbiddenClaims = countCriticalForbiddenClaims(task, answer);
  const claimPrecision = divide(correctAnswerClaims, answer.claims.length);
  const claimRecall = divide(
    matchedRequiredClaims,
    task.groundTruth.requiredClaims.length,
  );
  const claimF1 = claimPrecision + claimRecall === 0
    ? 0
    : (2 * claimPrecision * claimRecall) / (claimPrecision + claimRecall);
  const citationValidity = totalCitations === 0
    ? (requiredCitationsExpected === 0 ? 1 : 0)
    : divide(validCitations, totalCitations);
  const allRequiredCitations = requiredCitationsSatisfied
    === requiredCitationsExpected;

  return {
    strictSuccess:
      matchedRequiredClaims === task.groundTruth.requiredClaims.length
      && allRequiredCitations
      && criticalForbiddenClaims === 0,
    claimPrecision,
    claimRecall,
    claimF1,
    citationValidity,
    criticalForbiddenClaims,
    scorerVersion: SCORER_VERSION,
  };
}

function valueMatches(required, suppliedValue) {
  const supplied = normalize(suppliedValue);
  const accepted = required.acceptedValues.map(normalize);
  if (required.matchMode === "set_exact") {
    const suppliedSet = new Set(supplied.split("|").map(normalize).filter(Boolean));
    return suppliedSet.size === accepted.length
      && accepted.every((value) => suppliedSet.has(value));
  }
  return accepted.includes(supplied);
}

function evidenceMatches(acceptableEvidence, supplied) {
  return acceptableEvidence.some(
    (accepted) =>
      accepted.path === supplied.path
      && supplied.startLine <= accepted.endLine
      && supplied.endLine >= accepted.startLine,
  );
}

function evidenceExists(repositoryRoot, evidence) {
  if (!repositoryRoot) {
    return true;
  }
  try {
    const path = resolve(repositoryRoot, evidence.path);
    const root = resolve(repositoryRoot);
    if (path !== root && !path.startsWith(`${root}/`)) {
      return false;
    }
    const lineCount = readFileSync(path, "utf8").split(/\r?\n/u).length;
    return evidence.startLine <= evidence.endLine
      && evidence.endLine <= lineCount;
  } catch {
    return false;
  }
}

function countCriticalForbiddenClaims(task, answer) {
  let count = 0;
  for (const forbidden of task.groundTruth.forbiddenClaims) {
    if (
      forbidden.severity !== "critical"
      || !forbidden.claimKey
      || !forbidden.rejectedValues
    ) {
      continue;
    }
    const supplied = answer.claims.find(
      (claim) => claim.key === forbidden.claimKey,
    );
    if (
      supplied
      && forbidden.rejectedValues.map(normalize).includes(normalize(supplied.value))
    ) {
      count += 1;
    }
  }
  return count;
}

function normalize(value) {
  return value.normalize("NFC").trim().replace(/\s+/gu, " ");
}

function divide(numerator, denominator) {
  return denominator === 0 ? 1 : numerator / denominator;
}
