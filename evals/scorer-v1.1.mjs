import {
  readFileSync,
  realpathSync,
} from "node:fs";
import {
  resolve,
  sep,
} from "node:path";

export const SCORER_VERSION = "1.1.0";

export function scoreAnswerV1_1(task, answer, repositoryRoot) {
  const requiredByKey = new Map(
    task.groundTruth.requiredClaims.map((claim) => [claim.key, claim]),
  );
  const answersByKey = new Map();
  for (const claim of answer.claims) {
    const supplied = answersByKey.get(claim.key) ?? [];
    supplied.push(claim);
    answersByKey.set(claim.key, supplied);
  }

  let correctAnswerClaims = 0;
  let matchedRequiredClaims = 0;
  let requiredCitationsSatisfied = 0;
  let requiredCitationsExpected = 0;
  let validCitations = 0;
  let totalCitations = 0;

  for (const claim of answer.claims) {
    const required = requiredByKey.get(claim.key);
    const duplicateRequiredKey = required
      && answersByKey.get(claim.key).length !== 1;
    if (
      required
      && !duplicateRequiredKey
      && valueMatchesV1_1(required, claim.value)
    ) {
      correctAnswerClaims += 1;
    }
    for (const evidence of claim.evidence) {
      totalCitations += 1;
      if (
        required
        && !duplicateRequiredKey
        && evidenceMatches(required.acceptableEvidence, evidence)
        && evidenceExists(repositoryRoot, evidence)
      ) {
        validCitations += 1;
      }
    }
  }

  for (const required of task.groundTruth.requiredClaims) {
    const supplied = answersByKey.get(required.key) ?? [];
    const singleAnswer = supplied.length === 1 ? supplied[0] : null;
    if (singleAnswer && valueMatchesV1_1(required, singleAnswer.value)) {
      matchedRequiredClaims += 1;
      if (required.acceptableEvidence.length > 0) {
        requiredCitationsExpected += 1;
        if (
          singleAnswer.evidence.some(
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

export function valueMatchesV1_1(required, suppliedValue) {
  if (required.matchMode === "citation_only") {
    return true;
  }
  if (required.matchMode === "set_exact") {
    return setMatches(required.acceptedValues, suppliedValue);
  }
  const caseInsensitive = required.matchMode === "one_of"
    && isBooleanAlternativeSet(required.acceptedValues);
  return required.acceptedValues.some(
    (acceptedValue) =>
      canonicalValueMatches(acceptedValue, suppliedValue, { caseInsensitive }),
  );
}

export function canonicalValueMatches(
  acceptedValue,
  suppliedValue,
  { caseInsensitive = false } = {},
) {
  const accepted = normalize(acceptedValue);
  const supplied = normalize(suppliedValue);
  if (!accepted || !supplied) {
    return false;
  }

  const comparableAccepted = caseInsensitive ? foldCase(accepted) : accepted;
  const comparableSupplied = caseInsensitive ? foldCase(supplied) : supplied;
  if (stripOuterFormatting(comparableSupplied) === comparableAccepted) {
    return true;
  }

  let offset = 0;
  while (offset <= comparableSupplied.length - comparableAccepted.length) {
    const index = comparableSupplied.indexOf(comparableAccepted, offset);
    if (index < 0) {
      return false;
    }
    const end = index + comparableAccepted.length;
    if (
      hasCanonicalBoundaries(
        comparableSupplied,
        comparableAccepted,
        index,
        end,
      )
      && !isNegated(comparableSupplied, index)
    ) {
      return true;
    }
    offset = index + 1;
  }
  return false;
}

function setMatches(acceptedValues, suppliedValue) {
  const suppliedParts = suppliedValue
    .split("|")
    .map((value) => stripOuterFormatting(normalize(value)))
    .filter(Boolean);
  const acceptedParts = acceptedValues
    .map((value) => normalize(value));
  const suppliedSet = new Set(suppliedParts);
  return suppliedSet.size === acceptedParts.length
    && acceptedParts.every((value) => suppliedSet.has(value));
}

function isBooleanAlternativeSet(acceptedValues) {
  const booleanAlternatives = new Set(["true", "false", "yes", "no"]);
  return acceptedValues.every(
    (value) => booleanAlternatives.has(foldCase(normalize(value))),
  );
}

function hasCanonicalBoundaries(supplied, accepted, start, end) {
  const before = start === 0 ? "" : supplied[start - 1];
  const after = end === supplied.length ? "" : supplied[end];
  if (before && isWordCharacter(before)) {
    return false;
  }
  if (after && isWordCharacter(after)) {
    return false;
  }
  const afterNext = end + 1 >= supplied.length ? "" : supplied[end + 1];
  if (
    accepted.includes(".")
    && after === "."
    && afterNext
    && isWordCharacter(afterNext)
  ) {
    return false;
  }
  return true;
}

function isWordCharacter(value) {
  return /[\p{L}\p{N}_]/u.test(value);
}

function isNegated(supplied, matchStart) {
  const clause = supplied
    .slice(Math.max(0, matchStart - 80), matchStart)
    .split(/[.;:!?]/u)
    .at(-1);
  return /\b(?:no|not|never|without|isn't|doesn't|didn't|won't|cannot|can't)\b[^,]{0,48}$/iu
    .test(clause);
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
    const root = realpathSync(resolve(repositoryRoot));
    const path = realpathSync(resolve(root, evidence.path));
    if (path !== root && !path.startsWith(`${root}${sep}`)) {
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
    const supplied = answer.claims.filter(
      (claim) => claim.key === forbidden.claimKey,
    );
    if (
      supplied.some(
        (claim) =>
          forbidden.rejectedValues.some(
            (rejected) => canonicalValueMatches(rejected, claim.value),
          ),
      )
    ) {
      count += 1;
    }
  }
  return count;
}

function stripOuterFormatting(value) {
  let result = value.trim();
  const pairs = [
    ["`", "`"],
    ["\"", "\""],
    ["'", "'"],
    ["“", "”"],
    ["‘", "’"],
  ];
  for (const [open, close] of pairs) {
    if (result.startsWith(open) && result.endsWith(close)) {
      result = result.slice(open.length, -close.length).trim();
    }
  }
  return result.replace(/[.,;:]$/u, "").trim();
}

function normalize(value) {
  return value.normalize("NFC").trim().replace(/\s+/gu, " ");
}

function foldCase(value) {
  return value.toLocaleLowerCase("en-US");
}

function divide(numerator, denominator) {
  return denominator === 0 ? 1 : numerator / denominator;
}
