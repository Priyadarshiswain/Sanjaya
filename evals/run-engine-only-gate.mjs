import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import {
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import {
  dirname,
  join,
  resolve,
} from "node:path";
import { fileURLToPath } from "node:url";
import { withSanjaya } from "./mcp-client.mjs";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const protocolPath = join(evalRoot, "protocol", "engine-only-gate.json");
const protocolText = readFileSync(protocolPath, "utf8");
const protocol = JSON.parse(protocolText);
const corpusIndex = process.argv.indexOf("--corpus-root");
if (corpusIndex < 0 || !process.argv[corpusIndex + 1]) {
  throw new Error(
    "Usage: node run-engine-only-gate.mjs --corpus-root <acquired-public-repositories>",
  );
}
const corpusRoot = resolve(process.argv[corpusIndex + 1]);
const outputRoot = join(
  evalRoot,
  "results",
  "v0.1.2",
  "engine-only-gate",
);
const outputPath = join(outputRoot, "records.json");
if (existsSync(outputPath) && !process.argv.includes("--overwrite")) {
  throw new Error(
    `${outputPath} already exists; pass --overwrite only for an explicitly reviewed rerun.`,
  );
}

const packageRoot = join(evalRoot, "node_modules", "sanjaya-mcp");
const packageDocument = readJson(join(packageRoot, "package.json"));
if (packageDocument.version !== protocol.package.version) {
  throw new Error(
    `Installed sanjaya-mcp ${packageDocument.version} does not match `
    + `protocol ${protocol.package.version}.`,
  );
}
const launcherPath = join(packageRoot, "bin", "sanjaya-mcp.js");
const workRoot = mkdtempSync(join(tmpdir(), "sanjaya-engine-only-"));
const repositoryRecords = [];

try {
  for (const repository of protocol.repositories) {
    const source = join(corpusRoot, repository.id);
    const nativeRoot = join(workRoot, `${repository.id}-native`);
    const sanjayaRoot = join(workRoot, `${repository.id}-sanjaya`);
    cloneSnapshot(source, nativeRoot, repository.commit);
    cloneSnapshot(source, sanjayaRoot, repository.commit);

    const repositoryRecord = await withSanjaya({
      launcherPath,
      repositoryRoot: sanjayaRoot,
    }, async (client) => {
      const indexStarted = performance.now();
      const indexResult = await client.callResult("index_codebase");
      const indexDurationMs = elapsed(indexStarted);
      const indexResponse = indexResult?.structuredContent;
      const tasks = [];

      for (
        let repetition = 1;
        repetition <= protocol.controls.queryRepetitions;
        repetition += 1
      ) {
        for (const task of repository.tasks) {
          const taskRecord = tasks.find((candidate) => candidate.id === task.id)
            ?? createTaskRecord(tasks, task);
          if (repetition % 2 === 1) {
            taskRecord.native.push(runNative(task, nativeRoot, repetition));
            taskRecord.sanjaya.push(
              await runSanjaya(task, client, repetition),
            );
          } else {
            taskRecord.sanjaya.push(
              await runSanjaya(task, client, repetition),
            );
            taskRecord.native.push(runNative(task, nativeRoot, repetition));
          }
        }
      }

      return {
        index: {
          status: indexResponse?.status ?? "missing",
          state: indexResponse?.data?.state ?? null,
          durationMs: indexDurationMs,
          responseBytes: utf8Bytes(JSON.stringify(indexResult ?? null)),
          indexBytes: fileBytes(
            join(sanjayaRoot, ".sanjaya", "index-v1.json"),
          ),
          filesIndexed: indexResponse?.data?.filesIndexed ?? null,
          chunksIndexed: indexResponse?.data?.chunksIndexed ?? null,
          sourceBytes: indexResponse?.data?.sourceBytes ?? null,
          warnings: indexResponse?.warnings ?? [],
          errorCode: indexResponse?.error?.code ?? null,
        },
        tasks,
      };
    });
    repositoryRecords.push({
      id: repository.id,
      commit: repository.commit,
      ...repositoryRecord,
    });
  }

  const document = {
    schemaVersion: "1.0",
    status: "complete",
    generatedAt: new Date().toISOString(),
    protocolSha256: sha256(protocolText),
    package: `${protocol.package.name}@${protocol.package.version}`,
    environment: {
      platform: process.platform,
      architecture: process.arch,
      node: process.version,
      ripgrep: firstLine(runExecutable("rg", ["--version"]).stdout),
    },
    controls: {
      modelCalls: 0,
      queryRepetitions: protocol.controls.queryRepetitions,
      rawSourceStored: false,
      rawToolResponsesStored: false,
    },
    repositories: repositoryRecords,
  };
  mkdirSync(outputRoot, { recursive: true });
  writeFileSync(outputPath, `${JSON.stringify(document, null, 2)}\n`, "utf8");
  process.stdout.write(
    `Recorded engine-only observations at ${outputPath}.\n`,
  );
} finally {
  rmSync(workRoot, { recursive: true, force: true });
}

function createTaskRecord(tasks, task) {
  const record = {
    id: task.id,
    native: [],
    sanjaya: [],
  };
  tasks.push(record);
  return record;
}

function runNative(task, repositoryRoot, repetition) {
  const args = [
    "--json",
    "--color=never",
    "--no-messages",
  ];
  for (const glob of task.native.globs) {
    args.push("--glob", glob);
  }
  args.push("--regexp", task.native.pattern, ...task.native.scopes);
  const started = performance.now();
  const result = runExecutable("rg", args, repositoryRoot, [0, 1]);
  const durationMs = elapsed(started);
  const matches = [];
  const rendered = [];
  for (const line of result.stdout.split(/\r?\n/u)) {
    if (!line) {
      continue;
    }
    const event = JSON.parse(line);
    if (event.type !== "match") {
      continue;
    }
    const path = normalizePath(event.data.path.text);
    const lineNumber = event.data.line_number;
    const text = event.data.lines.text.replace(/\r?\n$/u, "");
    matches.push({ path, startLine: lineNumber, endLine: lineNumber });
    rendered.push(`${path}:${lineNumber}:${text}`);
  }
  const compactOutput = rendered.length === 0
    ? ""
    : `${rendered.join("\n")}\n`;
  return {
    repetition,
    status: result.status === 0 ? "ok" : "no_matches",
    durationMs,
    responseBytes: utf8Bytes(compactOutput),
    candidateCount: matches.length,
    outputFingerprint: sha256(compactOutput),
    candidates: matches,
  };
}

async function runSanjaya(task, client, repetition) {
  const started = performance.now();
  const result = await client.callResult(
    task.sanjaya.tool,
    task.sanjaya.arguments,
  );
  const durationMs = elapsed(started);
  const response = result?.structuredContent;
  const serialized = JSON.stringify(result ?? null);
  const matches = response?.data?.matches ?? [];
  const candidates = matches.map((match) => ({
    path: normalizePath(match.path),
    startLine: match.startLine ?? match.line,
    endLine: match.endLine ?? match.line,
  }));
  return {
    repetition,
    status: response?.status ?? "missing",
    durationMs,
    responseBytes: utf8Bytes(serialized),
    candidateCount: candidates.length,
    totalMatches: response?.data?.totalMatches ?? candidates.length,
    truncated: response?.data?.truncated ?? false,
    errorCode: response?.error?.code ?? null,
    outputFingerprint: sha256(serialized),
    candidates,
  };
}

function cloneSnapshot(source, target, expectedCommit) {
  runGit(["clone", "--quiet", "--no-hardlinks", source, target]);
  runGit(["checkout", "--quiet", "--detach", expectedCommit], target);
  const actual = runGit(["rev-parse", "HEAD"], target).trim();
  if (actual !== expectedCommit) {
    throw new Error(`${target} is at ${actual}; expected ${expectedCommit}.`);
  }
}

function runGit(args, cwd) {
  return runExecutable("git", args, cwd).stdout;
}

function runExecutable(command, args, cwd, acceptedStatuses = [0]) {
  const result = spawnSync(command, args, {
    cwd,
    encoding: "utf8",
    env: {
      ...process.env,
      GIT_CONFIG_GLOBAL: process.platform === "win32" ? "NUL" : "/dev/null",
      GIT_CONFIG_NOSYSTEM: "1",
      HTTP_PROXY: "http://127.0.0.1:1",
      HTTPS_PROXY: "http://127.0.0.1:1",
      ALL_PROXY: "http://127.0.0.1:1",
      NO_PROXY: "",
      LC_ALL: "C",
      TZ: "UTC",
    },
    maxBuffer: 64 * 1024 * 1024,
    timeout: 300_000,
    windowsHide: true,
  });
  if (result.error) {
    throw result.error;
  }
  if (!acceptedStatuses.includes(result.status)) {
    throw new Error(
      `${command} ${args[0] ?? ""} failed: ${result.stderr.trim()}`,
    );
  }
  return result;
}

function fileBytes(path) {
  return existsSync(path) ? statSync(path).size : 0;
}

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

function normalizePath(path) {
  return path.replaceAll("\\", "/").replace(/^\.\//u, "");
}

function elapsed(started) {
  return Math.round((performance.now() - started) * 1000) / 1000;
}

function utf8Bytes(value) {
  return Buffer.byteLength(value, "utf8");
}

function sha256(value) {
  return createHash("sha256").update(value, "utf8").digest("hex");
}

function firstLine(value) {
  return value.split(/\r?\n/u)[0];
}
