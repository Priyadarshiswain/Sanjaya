import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import {
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  realpathSync,
  rmSync,
  statSync,
  symlinkSync,
  writeFileSync,
} from "node:fs";
import { homedir, tmpdir } from "node:os";
import {
  delimiter,
  dirname,
  join,
  resolve,
} from "node:path";
import { fileURLToPath } from "node:url";
import Ajv2020 from "ajv/dist/2020.js";
import {
  createMarketplaceFixture,
  expectedPluginFiles,
  fileManifest,
  marketplaceName,
  removeMarketplaceFixture,
} from "../scripts/plugin-marketplace-contract.mjs";
import { withSanjaya } from "./mcp-client.mjs";
import { prepareControlledFixture } from "./prepare-controlled-fixture.mjs";
import { scoreAnswer } from "./scorer.mjs";
import { scoreAnswerV1_1 } from "./scorer-v1.1.mjs";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const repositoryRoot = dirname(evalRoot);
const study = optionalArgument("--study") ?? "pilot";
if (!["pilot", "guided", "evidence-first-skill"].includes(study)) {
  throw new Error(
    "--study must be pilot, guided, or evidence-first-skill.",
  );
}
const protocol = readJson(join(evalRoot, "protocol", `${study}.json`));
const manifest = readJson(join(evalRoot, "repositories", "manifest.json"));
const pilot = readJson(join(evalRoot, "tasks", "pilot.json"));
const launcherPath = join(
  evalRoot,
  "node_modules",
  "sanjaya-mcp",
  "bin",
  "sanjaya-mcp.js",
);
const answerSchemaPath = join(
  evalRoot,
  protocol.controls.outputSchema,
);
const validateAnswer = new Ajv2020({
  allErrors: true,
  strict: true,
}).compile(readJson(join(evalRoot, protocol.controls.validationSchema)));
const verifySetupOnly = process.argv.includes("--verify-skill-setup-only");
const corpusRootValue = optionalArgument("--corpus-root");
if (!verifySetupOnly && !corpusRootValue) {
  throw new Error("--corpus-root is required.");
}
const corpusRoot = corpusRootValue ? resolve(corpusRootValue) : null;
const limitValue = optionalArgument("--limit");
const limit = limitValue ? Number.parseInt(limitValue, 10) : Number.POSITIVE_INFINITY;
const repetitionsValue = optionalArgument("--repetitions");
const repetitions = repetitionsValue
  ? Number.parseInt(repetitionsValue, 10)
  : protocol.design.repetitions;
const maxTotalTokensValue = optionalArgument("--max-total-tokens");
const maxTotalTokens = maxTotalTokensValue
  ? Number.parseInt(maxTotalTokensValue, 10)
  : null;
const dryRun = process.argv.includes("--dry-run");
if (!Number.isInteger(limit) && limit !== Number.POSITIVE_INFINITY) {
  throw new Error("--limit must be a positive integer.");
}
if (
  !Number.isInteger(repetitions)
  || repetitions < 1
  || repetitions > protocol.design.repetitions
) {
  throw new Error(
    `--repetitions must be between 1 and ${protocol.design.repetitions}.`,
  );
}
if (maxTotalTokens !== null && (!Number.isInteger(maxTotalTokens) || maxTotalTokens < 1)) {
  throw new Error("--max-total-tokens must be a positive integer.");
}
if (
  study === "evidence-first-skill"
  && !dryRun
  && !verifySetupOnly
  && maxTotalTokens === null
) {
  throw new Error(
    "--max-total-tokens is required for an Evidence-First skill model run.",
  );
}

const resultsRoot = join(evalRoot, "results", "v0.1.2", study);
const runsRoot = join(resultsRoot, "runs");
const tracesRoot = join(resultsRoot, "traces");
mkdirSync(runsRoot, { recursive: true });
mkdirSync(tracesRoot, { recursive: true });
const workRoot = mkdtempSync(join(tmpdir(), "sanjaya-pilot-"));
const snapshots = new Map();
const tasksById = new Map(pilot.tasks.map((task) => [task.id, task]));
const repositoriesById = new Map(
  manifest.repositories.map((repository) => [repository.id, repository]),
);
const marketplaceFixture = study === "evidence-first-skill"
  ? createMarketplaceFixture(repositoryRoot)
  : null;
const plan = buildPlan();

try {
  verifyCodexVersion();
  if (verifySetupOnly) {
    if (study !== "evidence-first-skill") {
      throw new Error(
        "--verify-skill-setup-only requires --study evidence-first-skill.",
      );
    }
    const setupRoot = mkdtempSync(join(tmpdir(), "sanjaya-skill-setup-"));
    try {
      seedAuthentication(setupRoot);
      verifyAuthentication(setupRoot);
      installSkillPlugin(setupRoot);
    } finally {
      rmSync(setupRoot, { recursive: true, force: true });
    }
    process.stdout.write(
      "Verified exact plugin installation in a disposable Codex home without "
      + "calling a model.\n",
    );
  } else {
    await prepareSnapshots();
    const indexing = await prepareWarmIndexes();
    writeFileSync(
      join(resultsRoot, "indexing.json"),
      `${JSON.stringify(indexing, null, 2)}\n`,
      "utf8",
    );

    if (dryRun) {
      process.stdout.write(
        `Dry run verified ${plan.length} planned runs and all snapshots/indexes.\n`,
      );
      process.exitCode = 0;
    } else {
      let executed = 0;
      let consumedTokens = existingStudyTokens();
      let consecutiveSessionFailures = 0;
      for (const planned of plan) {
        if (executed >= limit) {
          break;
        }
        if (maxTotalTokens !== null && consumedTokens >= maxTotalTokens) {
          process.stdout.write(
            `stop aggregate token ceiling reached: ${consumedTokens}/`
            + `${maxTotalTokens}\n`,
          );
          break;
        }
        const outputPath = join(runsRoot, `${planned.runId}.json`);
        if (existsSync(outputPath)) {
          process.stdout.write(`skip ${planned.runId} existing\n`);
          continue;
        }
        const task = tasksById.get(planned.taskId);
        const snapshot = snapshots.get(task.repository.id);
        const treatmentRoot = snapshot.treatments.get(task.indexState);
        if (task.indexState === "none") {
          rmSync(join(treatmentRoot, ".sanjaya"), {
            recursive: true,
            force: true,
          });
        }
        process.stdout.write(
          `start ${planned.runId} ${planned.taskId} ${planned.arm} `
          + `rep=${planned.repetition}\n`,
        );
        const run = await executeRun({
          planned,
          task,
          agentRoot: snapshot.agent,
          treatmentRoot,
        });
        writeFileSync(outputPath, `${JSON.stringify(run, null, 2)}\n`, "utf8");
        process.stdout.write(
          `done ${planned.runId} status=${run.status} `
          + `strict=${run.scores?.strictSuccess ?? "n/a"} `
          + `wallMs=${run.metrics.wallTimeMs}\n`,
        );
        executed += 1;
        consumedTokens += totalTokens(run);
        consecutiveSessionFailures = run.status === "completed"
          ? 0
          : consecutiveSessionFailures + 1;
        if (
          protocol.controls.maxConsecutiveSessionFailures
          && consecutiveSessionFailures
          >= protocol.controls.maxConsecutiveSessionFailures
        ) {
          process.stdout.write(
            "stop consecutive session-failure ceiling reached: "
            + `${consecutiveSessionFailures}\n`,
          );
          break;
        }
      }
    }
  }
} finally {
  rmSync(workRoot, { recursive: true, force: true });
  if (marketplaceFixture) {
    removeMarketplaceFixture(repositoryRoot, marketplaceFixture);
  }
}

async function prepareSnapshots() {
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

async function prepareWarmIndexes() {
  const records = [];
  for (const [repositoryId, snapshot] of snapshots) {
    const warmRoot = snapshot.treatments.get("warm");
    if (!warmRoot) {
      continue;
    }
    const started = performance.now();
    const result = await withSanjaya({
      launcherPath,
      repositoryRoot: warmRoot,
    }, (client) => client.call("index_codebase"));
    if (result?.data?.state !== "ready") {
      throw new Error(
        `Warm index failed for ${repositoryId}: `
        + `${result?.error?.code ?? result?.status ?? "unknown"}`,
      );
    }
    records.push({
      repositoryId,
      status: result.status,
      warnings: result.warnings ?? [],
      filesIndexed: result.data.filesIndexed,
      chunksIndexed: result.data.chunksIndexed,
      sourceBytes: result.data.sourceBytes,
      durationMs: Math.round(performance.now() - started),
      indexBytes: statSync(join(warmRoot, ".sanjaya", "index-v1.json")).size,
    });
  }
  return {
    schemaVersion: "1.0",
    package: "sanjaya-mcp@0.1.2",
    generatedAt: new Date().toISOString(),
    records,
  };
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
  const sourceCommit = runGit(["rev-parse", "HEAD"], source).trim();
  if (sourceCommit !== repository.commit) {
    throw new Error(
      `${repository.id} corpus commit ${sourceCommit} `
      + `does not match ${repository.commit}.`,
    );
  }
  runGit(["clone", "--quiet", "--no-hardlinks", source, target]);
  runGit(["checkout", "--quiet", "--detach", repository.commit], target);
  return target;
}

function buildPlan() {
  const unsorted = [];
  for (const taskId of protocol.design.taskIds) {
    for (
      let repetition = 1;
      repetition <= protocol.design.repetitions;
      repetition += 1
    ) {
      for (const arm of protocol.design.arms) {
        const key = [
          protocol.design.orderSeed,
          taskId,
          repetition,
          arm,
        ].join("|");
        unsorted.push({
          taskId,
          repetition,
          arm,
          orderHash: sha256Text(key),
        });
      }
    }
  }
  unsorted.sort((left, right) => left.orderHash.localeCompare(right.orderHash));
  return unsorted
    .map((entry, index) => ({
      ...entry,
      runId:
        `SJ-RUN-${String(
          protocol.design.runNumberOffset + index + 1,
        ).padStart(4, "0")}-`
        + entry.orderHash.slice(0, 8).toUpperCase(),
    }))
    .filter((entry) => entry.repetition <= repetitions);
}

async function executeRun({ planned, task, agentRoot, treatmentRoot }) {
  const prompt = buildPrompt(task);
  const promptSha256 = sha256Text(prompt);
  const rawRoot = mkdtempSync(join(tmpdir(), "sanjaya-pilot-run-"));
  const codexHome = join(rawRoot, "codex-home");
  mkdirSync(codexHome, { recursive: true });
  const finalPath = join(rawRoot, "final.json");
  const rawEventsPath = join(rawRoot, "events.jsonl");
  const traceFileName = `${planned.runId}.jsonl`;
  const traceAbsolutePath = join(tracesRoot, traceFileName);
  let pluginEvidence = null;
  let setupError = null;
  try {
    seedAuthentication(codexHome);
    if (planned.arm === "evidence_first_skill") {
      pluginEvidence = installSkillPlugin(codexHome);
    }
  } catch (error) {
    setupError = error;
  }
  const indexBefore = indexFingerprint(treatmentRoot);
  const started = performance.now();
  let status = "agent_error";
  let answer = null;
  let scores = null;
  let events = [];
  let exitCode = null;
  let timedOut = false;

  try {
    if (setupError) {
      throw setupError;
    }
    const execution = await runCodex({
      prompt,
      agentRoot,
      treatmentRoot,
      arm: planned.arm,
      codexHome,
      finalPath,
      rawEventsPath,
    });
    exitCode = execution.exitCode;
    timedOut = execution.timedOut;
    events = parseJsonLines(rawEventsPath);
    if (timedOut) {
      status = "timeout";
    } else if (exitCode !== 0) {
      status = "agent_error";
    } else if (!existsSync(finalPath)) {
      status = "invalid_output";
    } else {
      try {
        answer = readJson(finalPath);
        if (!validateAnswer(answer)) {
          throw new Error("Answer failed the strict public answer schema.");
        }
        if (answer.taskId !== task.id) {
          throw new Error("Answer task ID did not match.");
        }
        scores = protocol.controls.scorerVersion === "1.1.0"
          ? scoreAnswerV1_1(task, answer, agentRoot)
          : scoreAnswer(task, answer, agentRoot);
        status = "completed";
      } catch {
        answer = null;
        scores = null;
        status = "invalid_output";
      }
    }
  } catch {
    status = "harness_error";
  }

  const wallTimeMs = Math.round(performance.now() - started);
  const trace = sanitizeEvents(events, wallTimeMs, status);
  const traceText = trace.map((event) => JSON.stringify(event)).join("\n")
    + (trace.length > 0 ? "\n" : "");
  writeFileSync(traceAbsolutePath, traceText, "utf8");
  const indexAfter = indexFingerprint(treatmentRoot);
  const indexWrites = indexBefore === indexAfter ? 0 : 1;
  const metrics = deriveMetrics(events, trace, wallTimeMs, indexWrites);
  rmSync(rawRoot, { recursive: true, force: true });

  return {
    schemaVersion: "1.0",
    runId: planned.runId,
    taskId: task.id,
    arm: planned.arm,
    repetition: planned.repetition,
    model: {
      provider: protocol.agent.provider,
      model: protocol.agent.model,
      agent: protocol.agent.name,
      agentVersion: protocol.agent.version,
      effort: protocol.agent.reasoningEffort,
    },
    controls: {
      promptSha256,
      repositoryCommit: task.repository.commit,
      networkAccess: false,
      maxTurns: protocol.controls.maxTurns,
      timeoutSeconds: protocol.controls.timeoutSeconds,
      ...(pluginEvidence ?? {}),
    },
    status,
    answer,
    scores,
    metrics,
    trace: {
      path: `traces/${traceFileName}`,
      sha256: sha256Text(traceText),
      eventCount: trace.length,
    },
  };
}

function runCodex({
  prompt,
  agentRoot,
  treatmentRoot,
  arm,
  codexHome,
  finalPath,
  rawEventsPath,
}) {
  const args = [
    "exec",
    "--strict-config",
    "--ignore-rules",
    "--ephemeral",
    "--sandbox",
    "read-only",
    "--cd",
    agentRoot,
    "--model",
    protocol.agent.model,
    "--output-schema",
    answerSchemaPath,
    "--output-last-message",
    finalPath,
    "--json",
    "-c",
    `model_reasoning_effort=${tomlString(protocol.agent.reasoningEffort)}`,
    "-c",
    "approval_policy=\"never\"",
    "-c",
    "web_search=\"disabled\"",
    "-c",
    "project_doc_max_bytes=0",
  ];
  // CODEX_HOME already points at a disposable per-run home containing only
  // the auth.json symlink, so this flag is isolation hygiene, not the
  // isolation boundary. The skill arm must omit it so Codex reads the
  // installed plugin from that same disposable home.
  if (arm !== "evidence_first_skill") {
    args.splice(2, 0, "--ignore-user-config");
  }
  if (arm !== "native") {
    args.push(
      "-c",
      `mcp_servers.sanjaya.command=${tomlString(process.execPath)}`,
      "-c",
      `mcp_servers.sanjaya.args=${JSON.stringify([
        launcherPath,
        "--root",
        treatmentRoot,
      ])}`,
      "-c",
      "mcp_servers.sanjaya.required=true",
      "-c",
      "mcp_servers.sanjaya.default_tools_approval_mode=\"approve\"",
    );
  }
  args.push(prompt);

  return new Promise((resolvePromise) => {
    const child = spawn("codex", args, {
      cwd: agentRoot,
      env: controlledEnvironment(codexHome),
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
    });
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
    let timedOut = false;
    const timeout = setTimeout(() => {
      timedOut = true;
      child.kill("SIGTERM");
    }, protocol.controls.timeoutSeconds * 1000);
    child.once("exit", (exitCode) => {
      clearTimeout(timeout);
      writeFileSync(rawEventsPath, stdout, "utf8");
      if (stderr) {
        writeFileSync(join(dirname(rawEventsPath), "stderr.txt"), stderr, "utf8");
      }
      resolvePromise({ exitCode, timedOut });
    });
    child.once("error", () => {
      clearTimeout(timeout);
      writeFileSync(rawEventsPath, stdout, "utf8");
      resolvePromise({ exitCode: null, timedOut: false });
    });
  });
}

function buildPrompt(task) {
  const lines = [
    "You are participating in a frozen code-discovery evaluation.",
    "Investigate only the read-only repository snapshot in the current directory.",
    "Do not modify files. Do not use network access.",
    "Use repository-relative paths and exact one-based line ranges for evidence.",
    "Return only the structured JSON required by the supplied output schema.",
    "Use exactly the requested claim keys. Put uncertainty in unknowns rather than inventing evidence.",
    `Task ID: ${task.id}`,
    `Required claim keys: ${task.groundTruth.requiredClaims.map((claim) => claim.key).join(", ")}`,
    `Question: ${task.question}`,
  ];
  if (protocol.design.guidedInstruction) {
    lines.splice(6, 0, protocol.design.guidedInstruction);
  }
  return lines.join("\n");
}

function sanitizeEvents(events, wallTimeMs, status) {
  const trace = [{
    schemaVersion: "1.0",
    sequence: 1,
    elapsedMs: 0,
    eventType: "session_start",
    actor: "harness",
    inputBytes: 0,
    outputBytes: 0,
    repositoryBytesRead: 0,
  }];
  for (const event of events) {
    if (event.type !== "item.completed") {
      continue;
    }
    const item = event.item ?? {};
    if (item.type === "command_execution") {
      trace.push(toolTrace(
        "native",
        "shell",
        byteLength(item.command ?? ""),
        byteLength(item.aggregated_output ?? item.output ?? ""),
      ));
    } else if (item.type === "mcp_tool_call") {
      trace.push(toolTrace(
        "sanjaya",
        sanitizeToolName(item.tool ?? item.name ?? "unknown"),
        byteLength(JSON.stringify(item.arguments ?? {})),
        byteLength(JSON.stringify(item.result ?? {})),
      ));
    } else if (item.type === "agent_message") {
      trace.push({
        schemaVersion: "1.0",
        sequence: 0,
        elapsedMs: 0,
        eventType: "model_turn",
        actor: "model",
        inputBytes: 0,
        outputBytes: byteLength(item.text ?? ""),
        repositoryBytesRead: 0,
      });
    }
  }
  const usage = events.findLast((event) => event.type === "turn.completed")?.usage;
  trace.push({
    schemaVersion: "1.0",
    sequence: 0,
    elapsedMs: wallTimeMs,
    eventType: status === "completed" ? "final_answer" : "error",
    actor: status === "completed" ? "model" : "harness",
    inputBytes: 0,
    outputBytes: 0,
    repositoryBytesRead: 0,
    ...(usage ? {
      uncachedInputTokens:
        Math.max(0, (usage.input_tokens ?? 0) - (usage.cached_input_tokens ?? 0)),
      cachedInputTokens: usage.cached_input_tokens ?? 0,
      outputTokens: usage.output_tokens ?? 0,
    } : {}),
    ...(status === "completed" ? {} : { errorCode: status }),
  });
  return trace.map((event, index) => ({
    ...event,
    sequence: index + 1,
    elapsedMs: event.elapsedMs || Math.min(wallTimeMs, index),
  }));
}

function toolTrace(toolFamily, toolName, inputBytes, outputBytes) {
  return {
    schemaVersion: "1.0",
    sequence: 0,
    elapsedMs: 0,
    eventType: "tool_result",
    actor: toolFamily === "sanjaya" ? "sanjaya" : "native_tool",
    toolFamily,
    toolName,
    inputBytes,
    outputBytes,
    repositoryBytesRead: outputBytes,
  };
}

function deriveMetrics(events, trace, wallTimeMs, indexWrites = 0) {
  const usage = events.findLast((event) => event.type === "turn.completed")?.usage
    ?? {};
  const toolEvents = trace.filter((event) => event.eventType === "tool_result");
  const nativeToolCalls = toolEvents.filter(
    (event) => event.toolFamily === "native",
  ).length;
  const sanjayaToolCalls = toolEvents.filter(
    (event) => event.toolFamily === "sanjaya",
  ).length;
  const toolResponseBytes = toolEvents.reduce(
    (total, event) => total + event.outputBytes,
    0,
  );
  return {
    turns: events.filter((event) => event.type === "turn.completed").length,
    toolCalls: nativeToolCalls + sanjayaToolCalls,
    nativeToolCalls,
    sanjayaToolCalls,
    repositorySourceBytesRead: toolEvents.reduce(
      (total, event) => total + event.repositoryBytesRead,
      0,
    ),
    toolResponseBytes,
    uncachedInputTokens: Math.max(
      0,
      (usage.input_tokens ?? 0) - (usage.cached_input_tokens ?? 0),
    ),
    cachedInputTokens: usage.cached_input_tokens ?? 0,
    outputTokens: usage.output_tokens ?? 0,
    wallTimeMs,
    indexWrites,
  };
}

function parseJsonLines(path) {
  if (!existsSync(path)) {
    return [];
  }
  return readFileSync(path, "utf8")
    .split(/\r?\n/u)
    .filter(Boolean)
    .flatMap((line) => {
      try {
        return [JSON.parse(line)];
      } catch {
        return [];
      }
    });
}

function existingStudyTokens() {
  if (!existsSync(runsRoot)) {
    return 0;
  }
  return readdirSync(runsRoot)
    .filter((file) => file.endsWith(".json"))
    .map((file) => readJson(join(runsRoot, file)))
    .reduce((total, run) => total + totalTokens(run), 0);
}

function totalTokens(run) {
  return (run.metrics?.uncachedInputTokens ?? 0)
    + (run.metrics?.cachedInputTokens ?? 0)
    + (run.metrics?.outputTokens ?? 0);
}

function runGit(args, cwd) {
  const result = spawnSync("git", args, {
    cwd,
    encoding: "utf8",
    env: controlledEnvironment(),
    windowsHide: true,
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`git ${args[0]} failed: ${result.stderr.trim()}`);
  }
  return result.stdout;
}

function verifyCodexVersion() {
  const result = spawnSync("codex", ["--version"], {
    cwd: repositoryRoot,
    encoding: "utf8",
    env: controlledEnvironment(),
    windowsHide: true,
  });
  if (result.error || result.status !== 0) {
    throw new Error("Unable to verify the pinned Codex CLI version.");
  }
  const expected = `${protocol.agent.name} ${protocol.agent.version}`;
  if (result.stdout.trim() !== expected) {
    throw new Error(
      `Expected ${expected}, received ${result.stdout.trim() || "no version"}.`,
    );
  }
}

function seedAuthentication(codexHome) {
  const sourceHome = process.env.CODEX_HOME
    ? resolve(process.env.CODEX_HOME)
    : join(homedir(), ".codex");
  const source = join(sourceHome, "auth.json");
  if (!existsSync(source)) {
    throw new Error("The authenticated Codex home has no auth.json.");
  }
  const target = join(codexHome, "auth.json");
  symlinkSync(realpathSync(source), target, "file");
  if (!lstatSync(target).isSymbolicLink()) {
    throw new Error("The disposable Codex home did not isolate authentication.");
  }
}

function verifyAuthentication(codexHome) {
  const result = spawnSync("codex", ["login", "status"], {
    cwd: repositoryRoot,
    encoding: "utf8",
    env: controlledEnvironment(codexHome),
    windowsHide: true,
  });
  if (
    result.error
    || result.status !== 0
    || !`${result.stdout}\n${result.stderr}`.includes("Logged in using ChatGPT")
  ) {
    throw new Error(
      "The disposable Codex home cannot use the approved login: "
      + JSON.stringify({
        status: result.status,
        stdout: result.stdout.trim(),
        stderr: result.stderr.trim(),
      }),
    );
  }
  if (!lstatSync(join(codexHome, "auth.json")).isSymbolicLink()) {
    throw new Error("Codex replaced the isolated authentication link.");
  }
}

function installSkillPlugin(codexHome) {
  if (!marketplaceFixture) {
    throw new Error("The skill study requires a disposable marketplace.");
  }
  const added = runCodexSetupJson([
    "plugin",
    "marketplace",
    "add",
    marketplaceFixture.marketplaceRoot,
    "--json",
  ], codexHome);
  if (added.marketplaceName !== marketplaceName || added.alreadyAdded !== false) {
    throw new Error("The disposable marketplace was not added exactly once.");
  }

  const pluginId = `sanjaya@${marketplaceName}`;
  const installed = runCodexSetupJson([
    "plugin",
    "add",
    pluginId,
    "--json",
  ], codexHome);
  const expectedRoot = join(
    realpathSync(codexHome),
    "plugins",
    "cache",
    marketplaceName,
    protocol.target.plugin.name,
    protocol.target.plugin.version,
  );
  if (
    installed.pluginId !== pluginId
    || installed.version !== protocol.target.plugin.version
    || resolve(installed.installedPath) !== resolve(expectedRoot)
  ) {
    throw new Error(
      "The installed skill plugin identity is not the frozen target: "
      + JSON.stringify({
        pluginId: installed.pluginId,
        version: installed.version,
        installedPath: installed.installedPath,
        expectedRoot,
      }),
    );
  }
  const listed = runCodexSetupJson([
    "plugin",
    "list",
    "--marketplace",
    marketplaceName,
    "--json",
  ], codexHome);
  const active = listed.installed?.find(
    (plugin) => plugin.pluginId === pluginId,
  );
  if (!active?.installed || !active.enabled || listed.available?.length !== 0) {
    throw new Error("The frozen skill plugin is not installed and enabled.");
  }
  const installedManifest = fileManifest(expectedRoot);
  const canonicalManifest = fileManifest(
    join(repositoryRoot, "plugins", "sanjaya"),
  );
  if (JSON.stringify(installedManifest) !== JSON.stringify(canonicalManifest)) {
    throw new Error("The installed skill plugin differs from the reviewed source.");
  }
  if (
    JSON.stringify(installedManifest.map(({ path }) => path))
    !== JSON.stringify(expectedPluginFiles)
  ) {
    throw new Error("The installed skill plugin contains unexpected files.");
  }

  const manifestSha256 = sha256Text(
    readFileSync(
      join(expectedRoot, ".codex-plugin", "plugin.json"),
      "utf8",
    ),
  );
  const skillSha256 = sha256Text(
    readFileSync(
      join(
        expectedRoot,
        "skills",
        "evidence-first-code-discovery",
        "SKILL.md",
      ),
      "utf8",
    ),
  );
  if (
    manifestSha256 !== protocol.target.plugin.manifestSha256
    || skillSha256 !== protocol.target.plugin.skillSha256
  ) {
    throw new Error("The installed skill plugin hash does not match the protocol.");
  }
  return {
    pluginManifestSha256: manifestSha256,
    skillSha256,
  };
}

function runCodexSetupJson(args, codexHome) {
  const result = spawnSync("codex", args, {
    cwd: repositoryRoot,
    encoding: "utf8",
    env: controlledEnvironment(codexHome),
    windowsHide: true,
  });
  if (result.error || result.status !== 0) {
    throw new Error(
      `codex ${args.slice(0, 2).join(" ")} failed before the measured run.`,
    );
  }
  try {
    return JSON.parse(result.stdout);
  } catch {
    throw new Error("Codex plugin setup returned invalid JSON.");
  }
}

function indexFingerprint(repository) {
  const indexRoot = join(repository, ".sanjaya");
  if (!existsSync(indexRoot)) {
    return "absent";
  }
  return sha256Text(JSON.stringify(fileManifest(indexRoot)));
}

function controlledEnvironment(codexHome = null) {
  const dotnetRoot = process.env.DOTNET_ROOT;
  return {
    ...process.env,
    PATH: dotnetRoot
      ? `${dotnetRoot}${delimiter}${process.env.PATH ?? ""}`
      : process.env.PATH,
    GIT_CONFIG_GLOBAL: process.platform === "win32" ? "NUL" : "/dev/null",
    GIT_CONFIG_NOSYSTEM: "1",
    LC_ALL: "C",
    LANG: "C",
    TZ: "UTC",
    ...(codexHome ? { CODEX_HOME: codexHome } : {}),
  };
}

function requiredArgument(name) {
  const index = process.argv.indexOf(name);
  if (index < 0 || !process.argv[index + 1]) {
    throw new Error(`${name} is required.`);
  }
  return process.argv[index + 1];
}

function optionalArgument(name) {
  const index = process.argv.indexOf(name);
  return index >= 0 ? process.argv[index + 1] : null;
}

function sanitizeToolName(value) {
  return String(value).replace(/[^a-zA-Z0-9_.:-]/gu, "_").slice(0, 120);
}

function tomlString(value) {
  return JSON.stringify(value);
}

function sha256Text(value) {
  return createHash("sha256").update(value, "utf8").digest("hex");
}

function byteLength(value) {
  return Buffer.byteLength(String(value), "utf8");
}

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}
