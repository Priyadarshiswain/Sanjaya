import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import {
  existsSync,
  mkdtempSync,
  readFileSync,
  rmSync,
  symlinkSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import {
  delimiter,
  dirname,
  join,
  resolve,
} from "node:path";
import { createInterface } from "node:readline";
import { fileURLToPath } from "node:url";
import { prepareControlledFixture } from "./prepare-controlled-fixture.mjs";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const fixtureContract = JSON.parse(
  readFileSync(
    join(evalRoot, "fixtures", "controlled-contract.json"),
    "utf8",
  ),
);
const installedPackageRoot = join(
  evalRoot,
  "node_modules",
  "sanjaya-mcp",
);
const installedPackage = JSON.parse(
  readFileSync(join(installedPackageRoot, "package.json"), "utf8"),
);
const launcherPath = join(
  installedPackageRoot,
  "bin",
  "sanjaya-mcp.js",
);

assert.equal(installedPackage.name, "sanjaya-mcp");
assert.equal(installedPackage.version, "0.1.2");
assert.ok(existsSync(launcherPath), "The exact public npm launcher is missing.");

const preparedProfiles = [];
let core;

try {
  for (const profile of ["core", "medium", "large"]) {
    const prepared = prepareControlledFixture({ profile });
    preparedProfiles.push(prepared);
    const expected = fixtureContract.profiles[profile];
    assert.deepEqual(
      {
        commit: prepared.commit,
        sourceFiles: prepared.sourceFiles,
        trackedFiles: prepared.trackedFiles,
      },
      expected,
      `${profile} fixture identity drifted.`,
    );
    assert.deepEqual(
      prepared.workingTree,
      fixtureContract.workingTree,
      `${profile} working-tree evidence drifted.`,
    );
    if (profile === "core") {
      core = prepared;
    }
  }

  assert.ok(core, "The core fixture was not prepared.");
  const runtimeConfig = JSON.parse(
    readFileSync(join(core.repositoryRoot, "config", "runtime.json"), "utf8"),
  );
  assert.equal(
    runtimeConfig.retryBaseSeconds,
    fixtureContract.finalRuntimeRetryBaseSeconds,
    "The deterministic Git transitions did not reach the reviewed runtime state.",
  );

  const version = runLauncher(["--version"]);
  assert.equal(version.status, 0);
  assert.equal(version.stdout.trim(), "sanjaya-mcp 0.1.2");
  assert.equal(version.stderr, "");

  const diagnostics = runLauncher([
    "--diagnose",
    "--root",
    core.repositoryRoot,
  ]);
  assert.equal(diagnostics.status, 0);
  assert.match(diagnostics.stdout, /Result: ready/u);
  assert.equal(diagnostics.stderr, "");
  assert.ok(
    !diagnostics.stdout.includes(core.repositoryRoot),
    "Diagnostics exposed the absolute fixture root.",
  );

  await verifyMcp(core);

  console.log(
    "Verified deterministic core/medium/large SignalDesk fixtures and "
    + "the exact public sanjaya-mcp@0.1.2 artifact through MCP without a model.",
  );
} finally {
  for (const prepared of preparedProfiles) {
    if (prepared.cleanupRoot) {
      rmSync(prepared.cleanupRoot, { recursive: true, force: true });
    }
  }
}

function runLauncher(argumentsToPass) {
  return spawnSync(process.execPath, [launcherPath, ...argumentsToPass], {
    cwd: evalRoot,
    encoding: "utf8",
    env: isolatedEnvironment(),
    windowsHide: true,
  });
}

async function verifyMcp(prepared) {
  const child = spawn(
    process.execPath,
    [launcherPath, "--root", prepared.repositoryRoot],
    {
      cwd: evalRoot,
      env: isolatedEnvironment(),
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    },
  );
  const exitPromise = new Promise((resolve) => child.once("exit", resolve));
  const output = createInterface({ input: child.stdout });
  const lines = output[Symbol.asyncIterator]();
  const responses = [];
  let stderr = "";
  let nextId = 1;
  let externalRoot = null;

  child.stderr.setEncoding("utf8");
  child.stderr.on("data", (chunk) => {
    stderr += chunk;
  });

  const timeout = setTimeout(() => child.kill("SIGTERM"), 30_000);

  try {
    await send({
      jsonrpc: "2.0",
      id: nextId,
      method: "initialize",
      params: {
        protocolVersion: "2025-06-18",
        capabilities: {},
        clientInfo: { name: "sanjaya-eval-fixture", version: "1.0" },
      },
    });
    nextId += 1;
    const initialize = await readMessage();
    assert.equal(initialize?.result?.serverInfo?.name, "sanjaya");

    await send({ jsonrpc: "2.0", method: "notifications/initialized" });
    const list = await request("tools/list", {});
    const toolNames = list?.result?.tools?.map((tool) => tool.name).sort();
    assert.deepEqual(toolNames, [
      "capabilities",
      "file_outline",
      "find_definition",
      "find_references",
      "get_source",
      "health_check",
      "index_codebase",
      "recent_changes",
      "search_code",
      "search_text",
    ]);

    const health = await call("health_check", {});
    assert.equal(content(health)?.status, "ok");
    assert.equal(content(health)?.data?.ready, true);

    const capabilities = await call("capabilities", {});
    assert.equal(content(capabilities)?.data?.repositoryReady, true);
    const providerStatuses = new Map(
      content(capabilities)?.data?.providers?.map(
        (provider) => [provider.id, provider.status],
      ),
    );
    assert.equal(providerStatuses.get("csharp-roslyn-syntax"), "supported");
    assert.equal(
      providerStatuses.get("typescript-compiler-syntax"),
      "supported",
    );
    assert.equal(
      providerStatuses.get("javascript-typescript-syntax"),
      "supported",
    );

    const markerSearch = await call("search_text", {
      query: "SIGNAL_DESK_ESCALATION_MARKER",
    });
    assert.equal(
      content(markerSearch)?.data?.matches?.[0]?.path,
      "config/runtime.json",
    );

    const excludedSearch = await call("search_text", {
      query: "SIGNAL_DESK_EXCLUDED_MARKER",
    });
    assert.equal(content(excludedSearch)?.data?.matches?.length, 0);

    const csharpOutline = await call("file_outline", {
      path: "backend/src/SignalDesk.Application/Relay/RetryPolicy.cs",
    });
    assert.equal(content(csharpOutline)?.provider, "csharp-roslyn-syntax");
    assert.ok(
      content(csharpOutline)?.data?.items?.some(
        (item) => item.name === "RetryPolicy",
      ),
      "C# outline omitted the production RetryPolicy.",
    );

    const typeScriptOutline = await call("file_outline", {
      path: "frontend/src/app/components/incident-board.component.ts",
    });
    assert.equal(
      content(typeScriptOutline)?.provider,
      "typescript-compiler-syntax",
    );
    assert.ok(
      content(typeScriptOutline)?.data?.items?.some(
        (item) => item.name === "IncidentBoardComponent",
      ),
      "TypeScript outline omitted IncidentBoardComponent.",
    );

    const recent = await call("recent_changes", {
      limit: 10,
      includeWorkingTree: true,
    });
    const recentData = content(recent)?.data;
    assert.equal(recentData?.head?.branch, "main");
    assert.equal(recentData?.head?.revision, prepared.commit);
    assert.equal(recentData?.commits?.length, 3);
    assert.ok(
      recentData?.workingTree?.changes?.some(
        (change) =>
          change.path === "docs/operator-runbook.md"
          && change.worktreeStatus === "modified",
      ),
      "Recent changes omitted the reviewed modified file.",
    );
    assert.ok(
      recentData?.workingTree?.changes?.some(
        (change) =>
          change.path === "local-observation.txt"
          && change.indexStatus === "untracked",
      ),
      "Recent changes omitted the reviewed untracked file.",
    );

    const index = await call("index_codebase", {});
    const indexData = content(index)?.data;
    assert.equal(content(index)?.status, "ok");
    assert.equal(indexData?.filesIndexed, prepared.sourceFiles);
    assert.ok(indexData?.chunksIndexed >= prepared.sourceFiles);
    assert.equal(indexData?.indexPath, ".sanjaya/index-v1.json");
    const indexPath = join(
      prepared.repositoryRoot,
      ".sanjaya",
      "index-v1.json",
    );
    const firstIndex = readFileSync(indexPath);

    const rebuilt = await call("index_codebase", {});
    assert.equal(
      content(rebuilt)?.data?.repositoryFingerprint,
      indexData.repositoryFingerprint,
    );
    assert.ok(
      readFileSync(indexPath).equals(firstIndex),
      "The controlled structural index was not byte-for-byte deterministic.",
    );

    const ambiguous = await call("find_definition", {
      name: "CalculateNextDelay",
      kind: "method",
    });
    assert.equal(content(ambiguous)?.data?.resolution, "ambiguous");
    assert.ok(content(ambiguous)?.data?.totalMatches >= 3);

    const definition = await call("find_definition", {
      name: "CalculateNextDelay",
      kind: "method",
      container: "SignalDesk.Relay.RetryPolicy",
    });
    const definitionData = content(definition)?.data;
    assert.equal(definitionData?.resolution, "unique");
    const definitionMatch = definitionData?.matches?.[0];
    assert.equal(
      definitionMatch?.path,
      "backend/src/SignalDesk.Application/Relay/RetryPolicy.cs",
    );

    const references = await call("find_references", {
      name: "CalculateNextDelay",
    });
    assert.equal(
      content(references)?.data?.classification,
      "syntax_candidate",
    );
    assert.ok(content(references)?.data?.totalMatches >= 1);

    const source = await call("get_source", {
      chunkId: definitionMatch.chunkId,
    });
    assert.equal(
      content(source)?.data?.path,
      "backend/src/SignalDesk.Application/Relay/RetryPolicy.cs",
    );
    assert.equal(content(source)?.data?.complete, true);
    assert.match(
      content(source)?.data?.source,
      /Math\.Pow\(2, Math\.Min\(attempt, MaximumAttempts\)\)/u,
    );

    if (process.platform !== "win32") {
      externalRoot = mkdtempSync(join(tmpdir(), "sanjaya-eval-external-"));
      const externalFile = join(externalRoot, "outside.txt");
      writeFileSync(
        externalFile,
        "SIGNAL_DESK_EXTERNAL_SECRET\n",
        "utf8",
      );
      symlinkSync(
        externalFile,
        join(prepared.repositoryRoot, "external-link.txt"),
      );

      const externalSearch = await call("search_text", {
        query: "SIGNAL_DESK_EXTERNAL_SECRET",
      });
      assert.equal(content(externalSearch)?.data?.matches?.length, 0);

      const externalOutline = await call("file_outline", {
        path: "external-link.txt",
      });
      assert.equal(content(externalOutline)?.status, "error");
      assert.ok(
        !JSON.stringify(externalOutline).includes("SIGNAL_DESK_EXTERNAL_SECRET"),
        "External symlink content crossed the fixture boundary.",
      );
    }

    for (const response of responses) {
      const serialized = JSON.stringify(response);
      assert.ok(
        !serialized.includes(prepared.repositoryRoot),
        "An MCP response exposed the absolute fixture root.",
      );
      assert.ok(
        !serialized.includes("eval-fixture@example.invalid"),
        "An MCP response exposed fixture identity metadata.",
      );
    }

    child.stdin.end();
    const exitCode = await exitPromise;
    assert.equal(exitCode, 0, `Public launcher failed: ${stderr.trim()}`);
    assert.equal(stderr, "");
  } finally {
    clearTimeout(timeout);
    if (child.exitCode === null) {
      child.kill("SIGTERM");
    }
    if (externalRoot) {
      rmSync(externalRoot, { recursive: true, force: true });
    }
  }

  async function call(name, argumentsToPass) {
    return request("tools/call", {
      name,
      arguments: argumentsToPass,
    });
  }

  async function request(method, params) {
    const id = nextId;
    nextId += 1;
    await send({ jsonrpc: "2.0", id, method, params });
    return readMessage();
  }

  function send(message) {
    return new Promise((resolvePromise, reject) => {
      child.stdin.write(`${JSON.stringify(message)}\n`, (error) => {
        if (error) {
          reject(error);
        } else {
          resolvePromise();
        }
      });
    });
  }

  async function readMessage() {
    const { value, done } = await lines.next();
    if (done || !value) {
      throw new Error(
        `Public launcher closed before replying. ${stderr.trim()}`,
      );
    }
    const message = JSON.parse(value);
    responses.push(message);
    return message;
  }
}

function content(response) {
  return response?.result?.structuredContent;
}

function isolatedEnvironment() {
  const dotnetRoot = process.env.DOTNET_ROOT;
  return {
    ...process.env,
    PATH: dotnetRoot
      ? `${dotnetRoot}${delimiter}${process.env.PATH ?? ""}`
      : process.env.PATH,
    HTTPS_PROXY: "http://127.0.0.1:1",
    HTTP_PROXY: "http://127.0.0.1:1",
    NO_PROXY: "",
    SANJAYA_NODE_EXECUTABLE: "ambient-runtime-must-not-win",
  };
}
