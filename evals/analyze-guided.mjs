import {
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const guidedRoot = join(evalRoot, "results", "v0.1.2", "guided");
const pilotRoot = join(evalRoot, "results", "v0.1.2", "pilot");
const protocol = readJson(join(evalRoot, "protocol", "guided.json"));
const allTasks = readJson(join(evalRoot, "tasks", "pilot.json")).tasks;
const taskById = new Map(allTasks.map((task) => [task.id, task]));
const guided = loadRuns(join(guidedRoot, "runs"));
const pilot = loadRuns(join(pilotRoot, "runs"));
const native = pilot.filter(
  (run) =>
    run.arm === "native"
    && protocol.design.taskIds.includes(run.taskId),
);
const completedNative = native.filter((run) => run.status === "completed");
const completedGuided = guided.filter((run) => run.status === "completed");
const pairs = completedPairs(native, guided);
const summary = {
  schemaVersion: "1.0",
  package: "sanjaya-mcp@0.1.2",
  model: "gpt-5.6-terra",
  agent: "codex-cli 0.144.5",
  effort: "medium",
  generatedAt: new Date().toISOString(),
  relationship: "diagnostic_only_not_headline",
  native: summarize(native),
  guided: summarize(guided),
  completedPairs: pairs.length,
  meanPairedClaimF1Delta: mean(
    pairs.map(
      ([nativeRun, guidedRun]) =>
        guidedRun.scores.claimF1 - nativeRun.scores.claimF1,
    ),
  ),
  meanPairedCitationDelta: mean(
    pairs.map(
      ([nativeRun, guidedRun]) =>
        guidedRun.scores.citationValidity
        - nativeRun.scores.citationValidity,
    ),
  ),
  tasks: protocol.design.taskIds.map((taskId) => ({
    taskId,
    title: taskById.get(taskId).title,
    nativeStrict: strictCount(native, taskId),
    guidedStrict: strictCount(guided, taskId),
    guidedSanjayaCalls: guided
      .filter((run) => run.taskId === taskId)
      .reduce((total, run) => total + run.metrics.sanjayaToolCalls, 0),
  })),
};
writeFileSync(
  join(guidedRoot, "summary.json"),
  `${JSON.stringify(summary, null, 2)}\n`,
  "utf8",
);
writeFileSync(join(guidedRoot, "REPORT.md"), report(summary), "utf8");
console.log(JSON.stringify(summary, null, 2));

function summarize(runs) {
  const completed = runs.filter((run) => run.status === "completed");
  return {
    planned: runs.length,
    completed: completed.length,
    strictSuccesses: runs.filter((run) => run.scores?.strictSuccess).length,
    meanClaimF1Completed: mean(completed.map((run) => run.scores.claimF1)),
    meanCitationValidityCompleted: mean(
      completed.map((run) => run.scores.citationValidity),
    ),
    runsUsingSanjaya: completed.filter(
      (run) => run.metrics.sanjayaToolCalls > 0,
    ).length,
    meanSanjayaToolCalls: mean(
      completed.map((run) => run.metrics.sanjayaToolCalls),
    ),
    medianTotalToolCalls: median(
      completed.map((run) => run.metrics.toolCalls),
    ),
    medianWallTimeMs: median(
      completed.map((run) => run.metrics.wallTimeMs),
    ),
    medianInputTokens: median(
      completed.map(
        (run) =>
          run.metrics.uncachedInputTokens + run.metrics.cachedInputTokens,
      ),
    ),
    medianOutputTokens: median(
      completed.map((run) => run.metrics.outputTokens),
    ),
  };
}

function completedPairs(nativeRuns, guidedRuns) {
  const nativeById = new Map(
    nativeRuns.map(
      (run) => [`${run.taskId}|${run.repetition}`, run],
    ),
  );
  return guidedRuns.flatMap((guidedRun) => {
    const nativeRun = nativeById.get(
      `${guidedRun.taskId}|${guidedRun.repetition}`,
    );
    return nativeRun?.status === "completed" && guidedRun.status === "completed"
      ? [[nativeRun, guidedRun]]
      : [];
  });
}

function strictCount(runs, taskId) {
  return runs.filter(
    (run) => run.taskId === taskId && run.scores?.strictSuccess,
  ).length;
}

function report(document) {
  const taskRows = document.tasks.map(
    (task) =>
      `| ${task.taskId} | ${task.nativeStrict}/3 | ${task.guidedStrict}/3 | `
      + `${task.guidedSanjayaCalls} |`,
  ).join("\n");
  return `# Sanjaya v0.1.2 guided diagnostic

Status: completed diagnostic; separate from the headline availability result.

## Outcome

All ${document.guided.completed} guided sessions used Sanjaya, averaging
${format(document.guided.meanSanjayaToolCalls)} Sanjaya calls per run. The
instruction therefore corrected the zero-uptake problem observed in the
headline pilot.

Active Sanjaya use did not demonstrate an accuracy or efficiency advantage in
this six-task diagnostic. The preregistered mechanical strict score was
${document.native.strictSuccesses}/${document.native.planned} for the matching
native records and ${document.guided.strictSuccesses}/${document.guided.planned}
for guided runs. Because scorer 1.0.0 under-credits explanatory but correct
claim values, these counts are not reliable absolute accuracy estimates.

Citation validity increased from
${format(document.native.meanCitationValidityCompleted)} to
${format(document.guided.meanCitationValidityCompleted)} on average, while
median wall time increased from ${document.native.medianWallTimeMs} ms to
${document.guided.medianWallTimeMs} ms and median input tokens increased from
${document.native.medianInputTokens} to ${document.guided.medianInputTokens}.
This is diagnostic evidence that the current guided orchestration adds
discovery overhead; it is not evidence of universal regression or benefit.

## Comparison

| Measure | Matching native | Sanjaya guided |
|---|---:|---:|
| Planned records | ${document.native.planned} | ${document.guided.planned} |
| Completed | ${document.native.completed} | ${document.guided.completed} |
| Runs using Sanjaya | ${document.native.runsUsingSanjaya} | ${document.guided.runsUsingSanjaya} |
| Mean Sanjaya calls | ${format(document.native.meanSanjayaToolCalls)} | ${format(document.guided.meanSanjayaToolCalls)} |
| Mean claim F1 | ${format(document.native.meanClaimF1Completed)} | ${format(document.guided.meanClaimF1Completed)} |
| Mean citation validity | ${format(document.native.meanCitationValidityCompleted)} | ${format(document.guided.meanCitationValidityCompleted)} |
| Median total tool calls | ${document.native.medianTotalToolCalls} | ${document.guided.medianTotalToolCalls} |
| Median wall time | ${document.native.medianWallTimeMs} ms | ${document.guided.medianWallTimeMs} ms |
| Median input tokens | ${document.native.medianInputTokens} | ${document.guided.medianInputTokens} |
| Median output tokens | ${document.native.medianOutputTokens} | ${document.guided.medianOutputTokens} |

There were ${document.completedPairs} completed same-task, same-repetition
pairs. Their mean claim-F1 delta was
${signed(document.meanPairedClaimF1Delta)} and their mean citation-validity
delta was ${signed(document.meanPairedCitationDelta)}.

## Task-level strict results

| Task | Native | Guided | Guided Sanjaya calls |
|---|---:|---:|---:|
${taskRows}

## Interpretation

The product-level lesson is an orchestration problem, not a basis for a
marketing claim:

- availability alone produced no tool adoption;
- a short instruction produced consistent adoption;
- current guided use consumed more context and time on these tasks; and
- evidence citations improved modestly, while the frozen claim scorer needs a
  versioned redesign before any broader accuracy study.

The next evaluation should first repair and independently review the answer
normalization/scoring contract, then compare targeted Sanjaya strategies
against native discovery on tasks where indexing can plausibly repay its cost.
`;
}

function loadRuns(root) {
  return readdirSync(root)
    .filter((file) => file.endsWith(".json"))
    .map((file) => readJson(join(root, file)));
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

function format(value) {
  return value.toFixed(3);
}

function signed(value) {
  return `${value >= 0 ? "+" : ""}${value.toFixed(3)}`;
}

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}
