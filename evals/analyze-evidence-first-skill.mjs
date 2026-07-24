import {
  existsSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const resultRoot = join(
  evalRoot,
  "results",
  "v0.1.2",
  "evidence-first-skill",
);
const protocol = readJson(
  join(evalRoot, "protocol", "evidence-first-skill.json"),
);
const tasks = readJson(join(evalRoot, "tasks", "pilot.json")).tasks;
const taskById = new Map(tasks.map((task) => [task.id, task]));
const runs = loadRuns(join(resultRoot, "runs"));
const native = runs.filter((run) => run.arm === "native");
const skill = runs.filter((run) => run.arm === "evidence_first_skill");
const pairs = completedPairs(native, skill);
const summary = {
  schemaVersion: "1.0",
  status: runs.length === protocol.design.totalRuns
    ? "full_study_complete"
    : "partial_stage",
  package: `${protocol.target.package}@${protocol.target.version}`,
  plugin:
    `${protocol.target.plugin.name}@${protocol.target.plugin.version}`,
  skill: protocol.target.plugin.skill,
  model: protocol.agent.model,
  agent: `${protocol.agent.name} ${protocol.agent.version}`,
  effort: protocol.agent.reasoningEffort,
  generatedAt: new Date().toISOString(),
  plannedRuns: protocol.design.totalRuns,
  recordedRuns: runs.length,
  native: summarize(native),
  evidenceFirstSkill: summarize(skill),
  completedPairs: summarizePairs(pairs),
  routing: {
    nativeOnlySkillRuns: skill.filter(
      (run) =>
        run.status === "completed"
        && run.metrics.sanjayaToolCalls === 0
        && run.metrics.nativeToolCalls > 0,
    ).length,
    sanjayaUsingSkillRuns: skill.filter(
      (run) =>
        run.status === "completed" && run.metrics.sanjayaToolCalls > 0,
    ).length,
    indexWrites: skill.reduce(
      (total, run) => total + (run.metrics.indexWrites ?? 0),
      0,
    ),
  },
  tasks: protocol.design.taskIds.map((taskId) => ({
    taskId,
    title: taskById.get(taskId).title,
    nativeStrict: strictCount(native, taskId),
    skillStrict: strictCount(skill, taskId),
    skillRuns: skill.filter((run) => run.taskId === taskId).length,
    skillRunsUsingSanjaya: skill.filter(
      (run) =>
        run.taskId === taskId
        && run.status === "completed"
        && run.metrics.sanjayaToolCalls > 0,
    ).length,
  })),
};

writeFileSync(
  join(resultRoot, "summary.json"),
  `${JSON.stringify(summary, null, 2)}\n`,
  "utf8",
);
writeFileSync(join(resultRoot, "REPORT.md"), report(summary), "utf8");
console.log(JSON.stringify(summary, null, 2));

function summarize(records) {
  const completed = records.filter((run) => run.status === "completed");
  return {
    planned: records.length,
    completed: completed.length,
    retainedFailures: records.length - completed.length,
    strictSuccesses: records.filter((run) => run.scores?.strictSuccess).length,
    meanClaimF1: mean(completed.map((run) => run.scores.claimF1)),
    meanCitationValidity: mean(
      completed.map((run) => run.scores.citationValidity),
    ),
    medianToolCalls: median(completed.map((run) => run.metrics.toolCalls)),
    medianWallTimeMs: median(completed.map((run) => run.metrics.wallTimeMs)),
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

function summarizePairs(pairs) {
  return {
    count: pairs.length,
    skillStrictWins: pairs.filter(
      ([nativeRun, skillRun]) =>
        !nativeRun.scores.strictSuccess && skillRun.scores.strictSuccess,
    ).length,
    nativeStrictWins: pairs.filter(
      ([nativeRun, skillRun]) =>
        nativeRun.scores.strictSuccess && !skillRun.scores.strictSuccess,
    ).length,
    strictTies: pairs.filter(
      ([nativeRun, skillRun]) =>
        nativeRun.scores.strictSuccess === skillRun.scores.strictSuccess,
    ).length,
    meanClaimF1Delta: mean(
      pairs.map(
        ([nativeRun, skillRun]) =>
          skillRun.scores.claimF1 - nativeRun.scores.claimF1,
      ),
    ),
    meanCitationValidityDelta: mean(
      pairs.map(
        ([nativeRun, skillRun]) =>
          skillRun.scores.citationValidity
          - nativeRun.scores.citationValidity,
      ),
    ),
  };
}

function completedPairs(nativeRuns, skillRuns) {
  const nativeByIdentity = new Map(
    nativeRuns.map(
      (run) => [`${run.taskId}|${run.repetition}`, run],
    ),
  );
  return skillRuns.flatMap((skillRun) => {
    const nativeRun = nativeByIdentity.get(
      `${skillRun.taskId}|${skillRun.repetition}`,
    );
    return nativeRun?.status === "completed" && skillRun.status === "completed"
      ? [[nativeRun, skillRun]]
      : [];
  });
}

function strictCount(records, taskId) {
  return records.filter(
    (run) => run.taskId === taskId && run.scores?.strictSuccess,
  ).length;
}

function report(document) {
  const taskRows = document.tasks.map(
    (task) =>
      `| ${task.taskId} | ${task.nativeStrict} | ${task.skillStrict} | `
      + `${task.skillRunsUsingSanjaya}/${task.skillRuns} |`,
  ).join("\n");
  return `# Evidence-First Code Discovery skill evaluation

Status: ${document.status.replaceAll("_", " ")}. This is a separate treatment
and does not alter the frozen v0.1.2 availability or guided records.

## What this study tests

The same ${document.model} agent receives the same task, repository snapshot,
native tools, MCP server, scorer, and limits in both arms. The treatment
difference is the exact installed \`${document.skill}\` skill. The task prompt
does not name the skill or tell the agent to use Sanjaya.

## Current outcome

${document.recordedRuns}/${document.plannedRuns} planned records are present.
There are ${document.completedPairs.count} completed same-task,
same-repetition pairs. The skill arm has
${document.completedPairs.skillStrictWins} strict wins,
${document.completedPairs.nativeStrictWins} strict losses, and
${document.completedPairs.strictTies} ties.

Selective routing is measured rather than assuming maximum MCP usage:
${document.routing.nativeOnlySkillRuns} completed skill sessions used native
tools without Sanjaya, and ${document.routing.sanjayaUsingSkillRuns} used at
least one Sanjaya tool. Measured index writes: ${document.routing.indexWrites}.

## Comparison

| Measure | Fresh native | Evidence-first skill |
|---|---:|---:|
| Recorded | ${document.native.planned} | ${document.evidenceFirstSkill.planned} |
| Completed | ${document.native.completed} | ${document.evidenceFirstSkill.completed} |
| Strict success | ${document.native.strictSuccesses} | ${document.evidenceFirstSkill.strictSuccesses} |
| Mean claim F1 | ${format(document.native.meanClaimF1)} | ${format(document.evidenceFirstSkill.meanClaimF1)} |
| Mean citation validity | ${format(document.native.meanCitationValidity)} | ${format(document.evidenceFirstSkill.meanCitationValidity)} |
| Median tool calls | ${document.native.medianToolCalls} | ${document.evidenceFirstSkill.medianToolCalls} |
| Median wall time | ${document.native.medianWallTimeMs} ms | ${document.evidenceFirstSkill.medianWallTimeMs} ms |
| Median input tokens | ${document.native.medianInputTokens} | ${document.evidenceFirstSkill.medianInputTokens} |
| Median output tokens | ${document.native.medianOutputTokens} | ${document.evidenceFirstSkill.medianOutputTokens} |

Paired mean claim-F1 delta (skill minus native):
${signed(document.completedPairs.meanClaimF1Delta)}. Paired mean citation
delta: ${signed(document.completedPairs.meanCitationValidityDelta)}.

## Task-level routing and strict results

| Task | Native strict | Skill strict | Skill runs using Sanjaya |
|---|---:|---:|---:|
${taskRows}

## Interpretation boundary

Failures, neutral results, regressions, and index writes remain visible. A
partial stage is a harness and routing check, not a marketplace claim. Even a
completed 72-run study supports conclusions only for the pinned agent, model,
repositories, tasks, MCP package, and skill version recorded here.
`;
}

function loadRuns(root) {
  if (!existsSync(root)) {
    return [];
  }
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
