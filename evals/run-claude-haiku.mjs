// Exploratory harness: runs the frozen 12 pilot tasks with Claude Code
// headless (claude -p) on claude-haiku-4-5, native arm vs Sanjaya-guided arm.
// Results are written under results/exploratory/ and are NOT preregistered
// evidence; they answer "does Sanjaya help a small model" as a smoke study.
import { spawn } from "node:child_process";
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { dirname, join, resolve } from "node:path";
import { tmpdir } from "node:os";
import { performance } from "node:perf_hooks";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import { withSanjaya } from "./mcp-client.mjs";
import { spawnSync } from "node:child_process";
import { prepareControlledFixture } from "./prepare-controlled-fixture.mjs";
import {
  SCORER_VERSION,
  amendTaskV1_2,
  scoreAnswerV1_2,
} from "./scorer-v1.2.mjs";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const manifest = readJson(join(evalRoot, "repositories", "manifest.json"));
const pilot = readJson(join(evalRoot, "tasks", "pilot.json"));
const guidedInstruction = readJson(
  join(evalRoot, "protocol", "guided.json"),
).design.guidedInstruction;
const launcherPath = join(
  evalRoot,
  "node_modules",
  "sanjaya-mcp",
  "bin",
  "sanjaya-mcp.js",
);
const validateAnswer = new Ajv2020({ allErrors: true, strict: true })
  .compile(readJson(join(evalRoot, "schemas", "answer.schema.json")));

const corpusRoot = resolve(requiredArgument("--corpus-root"));
const limitValue = optionalArgument("--limit");
const limit = limitValue
  ? Number.parseInt(limitValue, 10)
  : Number.POSITIVE_INFINITY;
const model = optionalArgument("--model") ?? "claude-haiku-4-5";
const arms = ["native", "sanjaya_guided"];

const resultsRoot = join(evalRoot, "results", "exploratory", "haiku-v1");
const runsRoot = join(resultsRoot, "runs");
mkdirSync(runsRoot, { recursive: true });
const workRoot = mkdtempSync(join(tmpdir(), "sanjaya-haiku-"));
const tasksById = new Map(pilot.tasks.map((task) => [task.id, task]));
const snapshots = new Map();

try {
  prepareSnapshots();
  await prepareWarmIndexes();
  let executed = 0;
  for (const task of pilot.tasks) {
    for (const arm of arms) {
      if (executed >= limit) {
        break;
      }
      const runId = `HAIKU-${task.id.slice(-4)}-${arm.toUpperCase()}`;
      const outputPath = join(runsRoot, `${runId}.json`);
      if (existsSync(outputPath)) {
        process.stdout.write(`skip ${runId} existing\n`);
        continue;
      }
      const snapshot = snapshots.get(task.repository.id);
      const treatmentRoot = snapshot.treatments.get(task.indexState);
      if (task.indexState === "none") {
        rmSync(join(treatmentRoot, ".sanjaya"), {
          recursive: true,
          force: true,
        });
      }
      process.stdout.write(`start ${runId}\n`);
      const run = await executeRun({
        runId,
        task,
        arm,
        agentRoot: snapshot.agent,
        treatmentRoot,
      });
      writeFileSync(outputPath, `${JSON.stringify(run, null, 2)}\n`, "utf8");
      process.stdout.write(
        `done ${runId} status=${run.status} `
        + `strict=${run.scores?.strictSuccess ?? "n/a"} `
        + `sanjayaCalls=${run.metrics.sanjayaToolCalls} `
        + `costUsd=${run.metrics.totalCostUsd ?? "n/a"} `
        + `wallMs=${run.metrics.wallTimeMs}\n`,
      );
      executed += 1;
    }
  }
} finally {
  rmSync(workRoot, { recursive: true, force: true });
}

function prepareSnapshots() {
  for (const repository of manifest.repositories) {
    const states = new Set(
      pilot.tasks
        .filter((task) => task.repository.id === repository.id)
        .map((task) => task.indexState),
    );
    const agent = createSnapshot(repository, `${repository.id}-agent`);
    const treatments = new Map();
    for (const state of states) {
      treatments.set(
        state,
        createSnapshot(repository, `${repository.id}-treatment-${state}`),
      );
    }
    snapshots.set(repository.id, { agent, treatments });
  }
}

function createSnapshot(repository, name) {
  const target = join(workRoot, name);
  if (repository.originKind === "controlled_fixture") {
    return prepareControlledFixture({
      profile: "core",
      output: target,
    }).repositoryRoot;
  }
  const source = join(corpusRoot, repository.id);
  runGit(["clone", "--quiet", "--no-hardlinks", source, target]);
  runGit(["checkout", "--quiet", "--detach", repository.commit], target);
  return target;
}

async function prepareWarmIndexes() {
  for (const [, snapshot] of snapshots) {
    const warmRoot = snapshot.treatments.get("warm");
    if (!warmRoot) {
      continue;
    }
    const result = await withSanjaya(
      { launcherPath, repositoryRoot: warmRoot },
      (client) => client.call("index_codebase"),
    );
    if (result?.data?.state !== "ready") {
      throw new Error(`Warm index failed for ${warmRoot}.`);
    }
  }
}

async function executeRun({ runId, task, arm, agentRoot, treatmentRoot }) {
  const prompt = buildPrompt(task, arm);
  const mcpConfigPath = join(workRoot, `${runId}-mcp.json`);
  const args = [
    "-p",
    "--model",
    model,
    "--output-format",
    "stream-json",
    "--verbose",
    "--max-turns",
    "40",
    "--setting-sources",
    "project",
    "--strict-mcp-config",
    "--disallowedTools",
    "Bash,Write,Edit,NotebookEdit,WebFetch,WebSearch,Task,TodoWrite,KillShell,BashOutput,SlashCommand,Skill",
  ];
  const allowed = ["Read", "Grep", "Glob", "LS"];
  if (arm === "sanjaya_guided") {
    writeFileSync(
      mcpConfigPath,
      JSON.stringify({
        mcpServers: {
          sanjaya: {
            command: process.execPath,
            args: [launcherPath, "--root", treatmentRoot],
          },
        },
      }),
      "utf8",
    );
    args.push("--mcp-config", mcpConfigPath);
    allowed.push("mcp__sanjaya", "mcp__sanjaya__*");
  }
  args.push("--allowedTools", allowed.join(","));

  const started = performance.now();
  const execution = await runClaude(args, agentRoot, prompt);
  const wallTimeMs = Math.round(performance.now() - started);
  const lines = execution.stdout
    .split("\n")
    .filter((line) => line.trim().startsWith("{"))
    .flatMap((line) => {
      try {
        return [JSON.parse(line)];
      } catch {
        return [];
      }
    });
  let nativeToolCalls = 0;
  let sanjayaToolCalls = 0;
  const sanjayaToolNames = [];
  for (const event of lines) {
    if (event.type !== "assistant") {
      continue;
    }
    for (const block of event.message?.content ?? []) {
      if (block.type !== "tool_use") {
        continue;
      }
      if (block.name?.startsWith("mcp__sanjaya__")) {
        sanjayaToolCalls += 1;
        sanjayaToolNames.push(block.name.replace("mcp__sanjaya__", ""));
      } else {
        nativeToolCalls += 1;
      }
    }
  }
  const final = lines.findLast((event) => event.type === "result");

  let status = "agent_error";
  let answer = null;
  let scores = null;
  let validationErrors = null;
  if (execution.timedOut) {
    status = "timeout";
  } else if (final && !final.is_error) {
    const parsed = extractJson(final.result ?? "");
    if (!parsed) {
      status = "invalid_output";
    } else if (!validateAnswer(parsed)) {
      status = "invalid_output";
      validationErrors = validateAnswer.errors?.slice(0, 5) ?? null;
    } else if (parsed.taskId !== task.id) {
      status = "invalid_output";
      validationErrors = [{ message: `taskId ${parsed.taskId} != ${task.id}` }];
    } else {
      status = "completed";
      answer = parsed;
      scores = scoreAnswerV1_2(amendTaskV1_2(task), answer, agentRoot);
    }
  }

  return {
    schemaVersion: "exploratory-1.0",
    studyStatus: "exploratory_unregistered",
    runId,
    taskId: task.id,
    arm,
    repetition: 1,
    model: {
      requested: model,
      agent: `claude-code ${final?.uuid ? "2.1.220" : "2.1.220"}`,
      scorerVersion: SCORER_VERSION,
    },
    status,
    answer,
    scores,
    validationErrors,
    metrics: {
      turns: final?.num_turns ?? null,
      nativeToolCalls,
      sanjayaToolCalls,
      sanjayaToolNames,
      inputTokens: final?.usage?.input_tokens ?? null,
      cacheCreationInputTokens:
        final?.usage?.cache_creation_input_tokens ?? null,
      cacheReadInputTokens: final?.usage?.cache_read_input_tokens ?? null,
      outputTokens: final?.usage?.output_tokens ?? null,
      totalCostUsd: final?.total_cost_usd ?? null,
      apiDurationMs: final?.duration_api_ms ?? null,
      wallTimeMs,
      permissionDenials: final?.permission_denials?.length ?? 0,
    },
  };
}

function runClaude(args, cwd, prompt) {
  return new Promise((resolvePromise) => {
    const child = spawn("claude", args, {
      cwd,
      env: { ...process.env, CLAUDE_CODE_ENTRYPOINT: "sanjaya-eval" },
      stdio: ["pipe", "pipe", "pipe"],
    });
    child.stdin.write(prompt);
    child.stdin.end();
    let stdout = "";
    let stderr = "";
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", (chunk) => {
      stdout += chunk;
    });
    child.stderr.on("data", (chunk) => {
      stderr += chunk;
    });
    const timeout = setTimeout(() => {
      child.kill("SIGKILL");
    }, 420_000);
    child.once("close", (exitCode) => {
      clearTimeout(timeout);
      resolvePromise({
        exitCode,
        stdout,
        stderr,
        timedOut: exitCode === null || exitCode === 137,
      });
    });
    child.once("error", () => {
      clearTimeout(timeout);
      resolvePromise({ exitCode: null, stdout, stderr, timedOut: false });
    });
  });
}

function buildPrompt(task, arm) {
  const lines = [
    "You are participating in a frozen code-discovery evaluation.",
    "Investigate only the read-only repository snapshot in the current directory.",
    "Do not modify files. Do not use network access.",
    "Use repository-relative paths and exact one-based line ranges for evidence.",
    "Use exactly the requested claim keys. Put uncertainty in unknowns rather than inventing evidence.",
  ];
  if (arm === "sanjaya_guided") {
    lines.push(guidedInstruction);
  }
  lines.push(
    "Respond with ONLY one JSON object and no markdown fences or prose, "
    + "shaped exactly like: "
    + '{"schemaVersion":"1.0","taskId":"<task id>","answer":"<one-sentence answer>",'
    + '"claims":[{"key":"<claim key>","value":"<claim value>",'
    + '"evidence":[{"path":"<repo-relative path>","startLine":1,"endLine":2}]}],'
    + '"unknowns":[],"confidence":0.9}',
    `Task ID: ${task.id}`,
    `Required claim keys: ${task.groundTruth.requiredClaims.map((claim) => claim.key).join(", ")}`,
    `Question: ${task.question}`,
  );
  return lines.join("\n");
}

function extractJson(text) {
  const trimmed = text.trim().replace(/^```(?:json)?/u, "").replace(/```$/u, "");
  const start = trimmed.indexOf("{");
  const end = trimmed.lastIndexOf("}");
  if (start < 0 || end <= start) {
    return null;
  }
  try {
    return JSON.parse(trimmed.slice(start, end + 1));
  } catch {
    return null;
  }
}

function requiredArgument(name) {
  const value = optionalArgument(name);
  if (!value) {
    throw new Error(`${name} is required.`);
  }
  return value;
}

function optionalArgument(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : null;
}

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function runGit(args, cwd) {
  const result = spawnSync("git", args, { cwd, encoding: "utf8" });
  if (result.status !== 0) {
    throw new Error(`git ${args.join(" ")} failed: ${result.stderr}`);
  }
  return result.stdout;
}
