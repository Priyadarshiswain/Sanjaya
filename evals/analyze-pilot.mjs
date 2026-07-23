import {
  mkdirSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const resultsRoot = join(evalRoot, "results", "v0.1.2", "pilot");
const tasks = readJson(join(evalRoot, "tasks", "pilot.json")).tasks;
const runs = readdirSync(join(resultsRoot, "runs"))
  .filter((file) => file.endsWith(".json"))
  .map((file) => readJson(join(resultsRoot, "runs", file)));
const layer0 = readJson(join(evalRoot, "results", "v0.1.2", "layer0.json"));
const byArm = Object.fromEntries(
  ["native", "sanjaya_available"].map(
    (arm) => [arm, summarizeArm(runs.filter((run) => run.arm === arm))],
  ),
);
const completedPairs = pairedRuns(runs);
const pairSummary = {
  completedPairs: completedPairs.length,
  strictNativeWins: completedPairs.filter(
    ([native, treatment]) =>
      native.scores.strictSuccess && !treatment.scores.strictSuccess,
  ).length,
  strictTreatmentWins: completedPairs.filter(
    ([native, treatment]) =>
      !native.scores.strictSuccess && treatment.scores.strictSuccess,
  ).length,
  strictTies: completedPairs.filter(
    ([native, treatment]) =>
      native.scores.strictSuccess === treatment.scores.strictSuccess,
  ).length,
  meanClaimF1Delta: mean(
    completedPairs.map(
      ([native, treatment]) =>
        treatment.scores.claimF1 - native.scores.claimF1,
    ),
  ),
};
const taskRows = tasks.map((task) => ({
  taskId: task.id,
  title: task.title,
  nativeStrict: strictCount(runs, task.id, "native"),
  treatmentStrict: strictCount(runs, task.id, "sanjaya_available"),
  treatmentRunsUsingSanjaya: runs.filter(
    (run) =>
      run.taskId === task.id
      && run.arm === "sanjaya_available"
      && run.metrics.sanjayaToolCalls > 0,
  ).length,
}));
const summary = {
  schemaVersion: "1.0",
  package: "sanjaya-mcp@0.1.2",
  model: "gpt-5.6-terra",
  agent: "codex-cli 0.144.5",
  effort: "medium",
  generatedAt: new Date().toISOString(),
  plannedRuns: runs.length,
  completedRuns: runs.filter((run) => run.status === "completed").length,
  retainedFailures: runs.filter((run) => run.status !== "completed").length,
  byArm,
  pairs: pairSummary,
  taskRows,
};
writeFileSync(
  join(resultsRoot, "summary.json"),
  `${JSON.stringify(summary, null, 2)}\n`,
  "utf8",
);
writeFileSync(join(resultsRoot, "REPORT.md"), report(summary, layer0), "utf8");
console.log(JSON.stringify(summary, null, 2));

function summarizeArm(armRuns) {
  const completed = armRuns.filter((run) => run.status === "completed");
  return {
    planned: armRuns.length,
    completed: completed.length,
    strictSuccesses: armRuns.filter((run) => run.scores?.strictSuccess).length,
    strictSuccessRatePlanned: divide(
      armRuns.filter((run) => run.scores?.strictSuccess).length,
      armRuns.length,
    ),
    meanClaimF1Completed: mean(
      completed.map((run) => run.scores.claimF1),
    ),
    meanCitationValidityCompleted: mean(
      completed.map((run) => run.scores.citationValidity),
    ),
    runsUsingSanjaya: completed.filter(
      (run) => run.metrics.sanjayaToolCalls > 0,
    ).length,
    medianToolCallsCompleted: median(
      completed.map((run) => run.metrics.toolCalls),
    ),
    medianWallTimeMsCompleted: median(
      completed.map((run) => run.metrics.wallTimeMs),
    ),
    medianInputTokensCompleted: median(
      completed.map(
        (run) =>
          run.metrics.uncachedInputTokens + run.metrics.cachedInputTokens,
      ),
    ),
    medianOutputTokensCompleted: median(
      completed.map((run) => run.metrics.outputTokens),
    ),
  };
}

function pairedRuns(allRuns) {
  const byIdentity = new Map(
    allRuns.map(
      (run) => [`${run.taskId}|${run.repetition}|${run.arm}`, run],
    ),
  );
  const pairs = [];
  for (const task of tasks) {
    for (let repetition = 1; repetition <= 3; repetition += 1) {
      const native = byIdentity.get(`${task.id}|${repetition}|native`);
      const treatment = byIdentity.get(
        `${task.id}|${repetition}|sanjaya_available`,
      );
      if (
        native?.status === "completed"
        && treatment?.status === "completed"
      ) {
        pairs.push([native, treatment]);
      }
    }
  }
  return pairs;
}

function strictCount(allRuns, taskId, arm) {
  return allRuns.filter(
    (run) =>
      run.taskId === taskId
      && run.arm === arm
      && run.scores?.strictSuccess,
  ).length;
}

function report(document, installedLayer) {
  const native = document.byArm.native;
  const treatment = document.byArm.sanjaya_available;
  const layerRows = installedLayer.corpus.map(
    (entry) =>
      `| ${entry.repositoryId} | ${entry.indexStatus} | `
      + `${entry.indexedFiles ?? "—"} | ${entry.indexedChunks ?? "—"} | `
      + `${entry.indexErrorCode ?? "—"} |`,
  ).join("\n");
  const taskTable = document.taskRows.map(
    (row) =>
      `| ${row.taskId} | ${row.nativeStrict}/3 | `
      + `${row.treatmentStrict}/3 | ${row.treatmentRunsUsingSanjaya}/3 |`,
  ).join("\n");
  return `# Sanjaya v0.1.2 pilot result

Status: completed pilot; exploratory, not a broad product claim.

## Outcome

This pilot does **not** demonstrate a benefit from merely making Sanjaya
available to GPT-5.6-Terra through Codex CLI. None of the
${treatment.completed} completed treatment sessions called a Sanjaya tool.
The agent used its ordinary shell/search tools in both arms, so the experiment
measured zero treatment uptake rather than the effect of active Sanjaya use.

The preregistered strict score was ${native.strictSuccesses}/36 in the native
arm and ${treatment.strictSuccesses}/36 in the availability arm. Across
${document.pairs.completedPairs} completed pairs there were
${document.pairs.strictTreatmentWins} treatment wins,
${document.pairs.strictNativeWins} native wins, and
${document.pairs.strictTies} ties. No causal performance or efficiency benefit
can be attributed to Sanjaya from this headline comparison.

## Run accounting

- Planned model records: ${document.plannedRuns}
- Completed sessions: ${document.completedRuns}
- Retained harness failures: ${document.retainedFailures}
- Model: ${document.model}, effort ${document.effort}
- Agent: ${document.agent}
- Treatment sessions with a Sanjaya call: ${treatment.runsUsingSanjaya}

The three failures occurred before a usable model answer: one outer harness
permission error and two structured-output schema compatibility errors. They
remain in the planned denominator.

## Mechanical scores and efficiency

| Measure | Native | Sanjaya available |
|---|---:|---:|
| Strict success / planned | ${native.strictSuccesses}/36 | ${treatment.strictSuccesses}/36 |
| Mean claim F1 / completed | ${format(native.meanClaimF1Completed)} | ${format(treatment.meanClaimF1Completed)} |
| Mean citation validity / completed | ${format(native.meanCitationValidityCompleted)} | ${format(treatment.meanCitationValidityCompleted)} |
| Median tool calls / completed | ${native.medianToolCallsCompleted} | ${treatment.medianToolCallsCompleted} |
| Median wall time | ${native.medianWallTimeMsCompleted} ms | ${treatment.medianWallTimeMsCompleted} ms |
| Median input tokens | ${native.medianInputTokensCompleted} | ${treatment.medianInputTokensCompleted} |
| Median output tokens | ${native.medianOutputTokensCompleted} | ${treatment.medianOutputTokensCompleted} |

The source-byte metric is a conservative tool-output-byte proxy because Codex
CLI does not expose actual filesystem bytes read. It must not be described as
precise disk I/O.

## Task-level strict results

| Task | Native | Available | Treatment used Sanjaya |
|---|---:|---:|---:|
${taskTable}

## Installed-artifact layer

| Repository | Index status | Files | Chunks | Error |
|---|---|---:|---:|---|
${layerRows}

The controlled fixture indexed cleanly. FastEndpoints, Vitest, and Kiota
produced ready indexes with explicit partial warnings. Aspire's full index
failed with \`index_source_unreadable\` because a supported source file exceeds
the v0.1.2 one-mebibyte file limit; this negative result is retained.

## Important scoring limitation

Scorer 1.0.0 required exact canonical claim values, while the generation prompt
required the keys but did not tell the agent to emit terse canonical values.
Many substantively correct answers therefore received zero claim credit after
adding explanatory text or Markdown formatting. Citation validity remained
separately measurable. The preregistered scores are preserved, but they should
not be interpreted as absolute answer accuracy.

Any corrected scorer must receive a new version and be applied to both arms.
It cannot silently replace these results. The preregistered contingency is a
separate guided diagnostic, explicitly reported outside the headline
comparison, to test whether Sanjaya helps when orchestration actually selects
it.
`;
}

function median(values) {
  const sorted = [...values].sort((left, right) => left - right);
  if (sorted.length === 0) {
    return 0;
  }
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 0
    ? (sorted[middle - 1] + sorted[middle]) / 2
    : sorted[middle];
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

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}
