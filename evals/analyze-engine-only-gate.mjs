import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import {
  mkdirSync,
  readFileSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const protocolText = readFileSync(
  join(evalRoot, "protocol", "engine-only-gate.json"),
  "utf8",
);
const protocol = JSON.parse(protocolText);
const resultsRoot = join(
  evalRoot,
  "results",
  "v0.1.2",
  "engine-only-gate",
);
const recordsText = readFileSync(join(resultsRoot, "records.json"), "utf8");
const records = JSON.parse(recordsText);
assert.equal(records.protocolSha256, sha256(protocolText));
assert.equal(records.controls.modelCalls, 0);

const taskById = new Map(
  protocol.repositories.flatMap((repository) =>
    repository.tasks.map((task) => [task.id, task])),
);
const repositorySummaries = [];
const taskSummaries = [];

for (const repository of records.repositories) {
  const protocolRepository = protocol.repositories.find(
    (candidate) => candidate.id === repository.id,
  );
  assert.ok(protocolRepository, `Unexpected repository ${repository.id}.`);
  assert.equal(repository.commit, protocolRepository.commit);
  const repositoryTasks = [];
  for (const record of repository.tasks) {
    const task = taskById.get(record.id);
    assert.ok(task, `Unexpected task ${record.id}.`);
    const native = summarizeRoute(
      record.native,
      task.targets,
      protocol.controls.queryRepetitions,
    );
    const sanjaya = summarizeRoute(
      record.sanjaya,
      task.targets,
      protocol.controls.queryRepetitions,
    );
    const summary = {
      id: task.id,
      repositoryId: repository.id,
      intent: task.intent,
      targetCount: task.targets.length,
      native,
      sanjaya,
    };
    repositoryTasks.push(summary);
    taskSummaries.push(summary);
  }
  assert.equal(repositoryTasks.length, protocolRepository.tasks.length);
  repositorySummaries.push({
    id: repository.id,
    index: repository.index,
    taskCount: repositoryTasks.length,
    nativeQueryDurationMs: sum(
      repositoryTasks.map((task) => task.native.medianDurationMs),
    ),
    sanjayaQueryDurationMs: sum(
      repositoryTasks.map((task) => task.sanjaya.medianDurationMs),
    ),
    sanjayaAmortizedDurationMs:
      repository.index.durationMs
      + sum(
        repositoryTasks.map((task) => task.sanjaya.medianDurationMs),
      ),
  });
}

assert.equal(taskSummaries.length, taskById.size);
const native = aggregate(taskSummaries.map((task) => task.native));
const sanjaya = aggregate(taskSummaries.map((task) => task.sanjaya));
const criteria = {
  indexReadiness: repositorySummaries.every(
    (repository) => repository.index.state === "ready",
  ),
  recallNonInferiority:
    sanjaya.meanTargetRecall >= native.meanTargetRecall - 0.05,
  precisionBenefit:
    sanjaya.meanCandidatePrecision
    >= native.meanCandidatePrecision + 0.15,
  responseByteBenefit:
    sanjaya.medianResponseBytes <= native.medianResponseBytes * 0.75,
};
criteria.materialBenefit = criteria.precisionBenefit
  || criteria.responseByteBenefit;
criteria.proceed = criteria.indexReadiness
  && criteria.recallNonInferiority
  && criteria.materialBenefit;

const summary = {
  schemaVersion: "1.0",
  status: criteria.proceed
    ? "proceed_to_agent_study_design"
    : "stop_current_product_hypothesis",
  package: records.package,
  inputFingerprint: sha256(`${protocolText}\0${recordsText}`),
  controls: records.controls,
  environment: records.environment,
  taskCount: taskSummaries.length,
  criteria,
  comparison: { native, sanjaya },
  repositories: repositorySummaries,
  tasks: taskSummaries,
};
const outputs = new Map([
  ["summary.json", `${JSON.stringify(summary, null, 2)}\n`],
  ["REPORT.md", report(summary)],
]);

if (process.argv.includes("--check")) {
  for (const [file, expected] of outputs) {
    assert.equal(
      readFileSync(join(resultsRoot, file), "utf8"),
      expected,
      `${file} is not the reproducible engine-only analysis.`,
    );
  }
  process.stdout.write(
    `Verified engine-only analysis of ${summary.taskCount} tasks; `
    + `decision: ${summary.status}.\n`,
  );
} else {
  mkdirSync(resultsRoot, { recursive: true });
  for (const [file, content] of outputs) {
    writeFileSync(join(resultsRoot, file), content, "utf8");
  }
  process.stdout.write(`${JSON.stringify(summary, null, 2)}\n`);
}

function summarizeRoute(observations, targets, repetitions) {
  assert.equal(observations.length, repetitions);
  const first = observations[0];
  for (const observation of observations.slice(1)) {
    assert.equal(
      observation.outputFingerprint,
      first.outputFingerprint,
      "Measured route output changed between timing repetitions.",
    );
    assert.deepEqual(
      observation.candidates,
      first.candidates,
      "Measured candidates changed between timing repetitions.",
    );
  }
  const targetHits = targets.filter((target) =>
    first.candidates.some((candidate) => overlaps(candidate, target))).length;
  const relevantCandidates = first.candidates.filter((candidate) =>
    targets.some((target) => overlaps(candidate, target))).length;
  return {
    status: first.status,
    targetHits,
    targetRecall: divide(targetHits, targets.length),
    candidateCount: first.candidateCount,
    relevantCandidates,
    candidatePrecision: divide(
      relevantCandidates,
      first.candidateCount,
    ),
    responseBytes: first.responseBytes,
    medianResponseBytes: median(
      observations.map((observation) => observation.responseBytes),
    ),
    medianDurationMs: median(
      observations.map((observation) => observation.durationMs),
    ),
  };
}

function aggregate(routes) {
  return {
    meanTargetRecall: mean(routes.map((route) => route.targetRecall)),
    meanCandidatePrecision: mean(
      routes.map((route) => route.candidatePrecision),
    ),
    medianResponseBytes: median(
      routes.map((route) => route.medianResponseBytes),
    ),
    medianQueryDurationMs: median(
      routes.map((route) => route.medianDurationMs),
    ),
    tasksWithFullRecall: routes.filter((route) => route.targetRecall === 1)
      .length,
  };
}

function overlaps(left, right) {
  return normalizePath(left.path) === normalizePath(right.path)
    && left.startLine <= right.endLine
    && left.endLine >= right.startLine;
}

function report(document) {
  const rows = document.tasks.map((task) =>
    `| ${task.id} | ${format(task.native.targetRecall)} | `
    + `${format(task.sanjaya.targetRecall)} | `
    + `${format(task.native.candidatePrecision)} | `
    + `${format(task.sanjaya.candidatePrecision)} | `
    + `${task.native.medianResponseBytes} | `
    + `${task.sanjaya.medianResponseBytes} | `
    + `${format(task.native.medianDurationMs)} | `
    + `${format(task.sanjaya.medianDurationMs)} |`,
  ).join("\n");
  const repositoryRows = document.repositories.map((repository) =>
    `| ${repository.id} | ${format(repository.index.durationMs)} | `
    + `${repository.index.indexBytes} | `
    + `${format(repository.nativeQueryDurationMs)} | `
    + `${format(repository.sanjayaQueryDurationMs)} | `
    + `${format(repository.sanjayaAmortizedDurationMs)} |`,
  ).join("\n");
  return `# Engine-only hypothesis gate

Status: **${document.status}**

This model-free gate compares one frozen native ripgrep route with one frozen
Sanjaya route for each of ${document.taskCount} structural evidence targets.
It does not measure answer writing, implicit skill activation, or model quality.

## Decision

| Criterion | Result |
|---|---|
| Every index ready | ${yesNo(document.criteria.indexReadiness)} |
| Target recall non-inferior | ${yesNo(document.criteria.recallNonInferiority)} |
| Precision gain >= 0.15 | ${yesNo(document.criteria.precisionBenefit)} |
| Median response bytes <= 75% of native | ${yesNo(document.criteria.responseByteBenefit)} |
| Material benefit | ${yesNo(document.criteria.materialBenefit)} |
| Proceed | ${yesNo(document.criteria.proceed)} |

## Aggregate comparison

| Measure | Native | Sanjaya |
|---|---:|---:|
| Mean target recall | ${format(document.comparison.native.meanTargetRecall)} | ${format(document.comparison.sanjaya.meanTargetRecall)} |
| Mean candidate precision | ${format(document.comparison.native.meanCandidatePrecision)} | ${format(document.comparison.sanjaya.meanCandidatePrecision)} |
| Median response bytes | ${document.comparison.native.medianResponseBytes} | ${document.comparison.sanjaya.medianResponseBytes} |
| Median query duration (ms) | ${format(document.comparison.native.medianQueryDurationMs)} | ${format(document.comparison.sanjaya.medianQueryDurationMs)} |
| Tasks with full recall | ${document.comparison.native.tasksWithFullRecall}/${document.taskCount} | ${document.comparison.sanjaya.tasksWithFullRecall}/${document.taskCount} |

## Index-build amortization

The measured repetitions stabilize query timing only. Index cost is amortized
over each repository's five unique queries, never over repetitions.

| Repository | Index build ms | Index bytes | Native five-query ms | Sanjaya query-only ms | Sanjaya including index ms |
|---|---:|---:|---:|---:|---:|
${repositoryRows}

## Task detail

| Task | Native recall | Sanjaya recall | Native precision | Sanjaya precision | Native bytes | Sanjaya bytes | Native ms | Sanjaya ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
${rows}

## Interpretation boundary

The result applies only to the pinned repositories, frozen queries, public
package version, current index envelope, and recorded machine. Response bytes
are a token-pressure proxy, not model-token measurements. A proceed result
authorizes designing a separate agent study; it does not establish a public
product-benefit claim. A stop result rejects further routing work for this
specific product hypothesis until the engine or delivery shape changes.
`;
}

function normalizePath(path) {
  return path.replaceAll("\\", "/").replace(/^\.\//u, "");
}

function mean(values) {
  return values.length === 0
    ? 0
    : sum(values) / values.length;
}

function median(values) {
  if (values.length === 0) {
    return 0;
  }
  const sorted = [...values].sort((left, right) => left - right);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 1
    ? sorted[middle]
    : (sorted[middle - 1] + sorted[middle]) / 2;
}

function sum(values) {
  return values.reduce((total, value) => total + value, 0);
}

function divide(numerator, denominator) {
  return denominator === 0 ? 0 : numerator / denominator;
}

function format(value) {
  return value.toFixed(3);
}

function yesNo(value) {
  return value ? "yes" : "no";
}

function sha256(value) {
  return createHash("sha256").update(value, "utf8").digest("hex");
}
