import { spawnSync } from "node:child_process";
import {
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { withSanjaya } from "./mcp-client.mjs";
import { prepareControlledFixture } from "./prepare-controlled-fixture.mjs";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const manifest = readJson(join(evalRoot, "repositories", "manifest.json"));
const corpusIndex = process.argv.indexOf("--corpus-root");
if (corpusIndex < 0 || !process.argv[corpusIndex + 1]) {
  throw new Error(
    "Usage: node run-layer0.mjs --corpus-root <acquired-public-repositories>",
  );
}
const corpusRoot = resolve(process.argv[corpusIndex + 1]);
const launcherPath = join(
  evalRoot,
  "node_modules",
  "sanjaya-mcp",
  "bin",
  "sanjaya-mcp.js",
);
const workRoot = mkdtempSync(join(tmpdir(), "sanjaya-layer0-"));
const results = [];
const queries = new Map([
  ["signaldesk-core", "CalculateNextDelay"],
  ["fastendpoints", "EndpointInvoker"],
  ["vitest", "CodeCache"],
  ["kiota", "CodeMethodWriter"],
  ["aspire", "LaunchAndWaitForBackchannelAsync"],
]);

try {
  for (const repository of manifest.repositories) {
    const snapshot = repository.originKind === "controlled_fixture"
      ? prepareControlledFixture({
          profile: "core",
          output: join(workRoot, repository.id),
        })
      : cloneSnapshot(
          join(corpusRoot, repository.id),
          join(workRoot, repository.id),
          repository.commit,
        );
    const started = performance.now();
    const record = await withSanjaya({
      launcherPath,
      repositoryRoot: snapshot.repositoryRoot,
    }, async (client) => {
      const tools = await client.listTools();
      const health = await client.call("health_check");
      const capabilities = await client.call("capabilities");
      const text = await client.call("search_text", {
        query: queries.get(repository.id),
        maxResults: 5,
      });
      const indexStarted = performance.now();
      const index = await client.call("index_codebase");
      const indexDurationMs = Math.round(performance.now() - indexStarted);
      const code = index?.data?.state === "ready"
        ? await client.call("search_code", {
            query: queries.get(repository.id),
            maxResults: 5,
          })
        : null;
      return {
        toolCount: tools.length,
        healthStatus: health?.status,
        repositoryReady: capabilities?.data?.repositoryReady,
        providerStatuses: Object.fromEntries(
          (capabilities?.data?.providers ?? []).map(
            (provider) => [provider.id, provider.status],
          ),
        ),
        textSearchStatus: text?.status,
        textSearchMatches: text?.data?.matches?.length ?? 0,
        textSearchWarnings: text?.warnings ?? [],
        indexStatus: index?.status,
        indexErrorCode: index?.error?.code ?? null,
        indexWarnings: index?.warnings ?? [],
        indexedFiles: index?.data?.filesIndexed ?? null,
        indexedChunks: index?.data?.chunksIndexed ?? null,
        indexDurationMs,
        indexBytes: directoryBytes(join(snapshot.repositoryRoot, ".sanjaya")),
        codeSearchStatus: code?.status ?? null,
        codeSearchMatches: code?.data?.matches?.length ?? 0,
      };
    });
    results.push({
      repositoryId: repository.id,
      commit: repository.commit,
      sizeBand: repository.sizeBand,
      elapsedMs: Math.round(performance.now() - started),
      ...record,
    });
  }

  const document = {
    schemaVersion: "1.0",
    package: "sanjaya-mcp@0.1.2",
    generatedAt: new Date().toISOString(),
    corpus: results,
  };
  const outputRoot = join(evalRoot, "results", "v0.1.2");
  mkdirSync(outputRoot, { recursive: true });
  writeFileSync(
    join(outputRoot, "layer0.json"),
    `${JSON.stringify(document, null, 2)}\n`,
    "utf8",
  );
  process.stdout.write(`${JSON.stringify(document, null, 2)}\n`);
} finally {
  rmSync(workRoot, { recursive: true, force: true });
}

function cloneSnapshot(source, target, expectedCommit) {
  runGit(["clone", "--quiet", "--no-hardlinks", source, target]);
  runGit(["checkout", "--quiet", "--detach", expectedCommit], target);
  const commit = runGit(["rev-parse", "HEAD"], target).trim();
  if (commit !== expectedCommit) {
    throw new Error(`Snapshot commit ${commit} did not match ${expectedCommit}.`);
  }
  return { repositoryRoot: target };
}

function runGit(args, cwd) {
  const result = spawnSync("git", args, {
    cwd,
    encoding: "utf8",
    env: {
      ...process.env,
      GIT_CONFIG_GLOBAL: process.platform === "win32" ? "NUL" : "/dev/null",
      GIT_CONFIG_NOSYSTEM: "1",
      LC_ALL: "C",
      TZ: "UTC",
    },
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

function directoryBytes(root) {
  try {
    let total = 0;
    for (const entry of readdirSync(root, { withFileTypes: true })) {
      const path = join(root, entry.name);
      total += entry.isDirectory() ? directoryBytes(path) : statSync(path).size;
    }
    return total;
  } catch {
    return 0;
  }
}

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}
