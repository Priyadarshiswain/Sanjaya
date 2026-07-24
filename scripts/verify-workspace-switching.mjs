import { spawn } from "node:child_process";
import {
  mkdirSync,
  mkdtempSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { createInterface } from "node:readline";
import { releaseVersion } from "./release-contract.mjs";
import {
  createBoundVsCodeServerConfiguration,
  createVsCodeInstallUrl,
  parseVsCodeInstallUrl,
} from "./vscode-install-contract.mjs";

const temporaryRoot = mkdtempSync(
  join(canonicalTemporaryRoot(), "sanjaya-workspace-switching-"),
);
const firstRoot = join(temporaryRoot, "first");
const secondRoot = join(temporaryRoot, "second");
const launcherPath = resolve("bin", "sanjaya-mcp.js");
const firstMarker = "FIRST_WORKSPACE_UNIQUE_MARKER";
const secondMarker = "SECOND_WORKSPACE_UNIQUE_MARKER";
const sessions = [];

mkdirSync(firstRoot);
mkdirSync(secondRoot);
writeFileSync(join(firstRoot, "marker.txt"), `${firstMarker}\n`, "utf8");
writeFileSync(join(secondRoot, "marker.txt"), `${secondMarker}\n`, "utf8");

try {
  const portableConfiguration = parseVsCodeInstallUrl(
    createVsCodeInstallUrl(releaseVersion),
  );
  if (portableConfiguration.args[3] !== "${workspaceFolder}") {
    throw new Error("Portable VS Code configuration lost its workspace placeholder.");
  }

  const firstConfiguration = createBoundVsCodeServerConfiguration(
    releaseVersion,
    firstRoot,
  );
  const secondConfiguration = createBoundVsCodeServerConfiguration(
    releaseVersion,
    secondRoot,
  );
  assertIsolatedConfigurations(firstConfiguration, secondConfiguration);

  const first = createSession(firstConfiguration, "first");
  const second = createSession(secondConfiguration, "second");
  sessions.push(first, second);
  if (first.pid === second.pid) {
    throw new Error("Workspace switching reused one server process.");
  }

  await Promise.all([first.initialize(), second.initialize()]);
  const [
    [firstOwn, firstOther],
    [secondOwn, secondOther],
  ] = await Promise.all([
    searchWorkspace(first, firstMarker, secondMarker),
    searchWorkspace(second, secondMarker, firstMarker),
  ]);

  assertSingleMarker(firstOwn, firstMarker, "first");
  assertNoMarker(firstOther, secondMarker, "first");
  assertSingleMarker(secondOwn, secondMarker, "second");
  assertNoMarker(secondOther, firstMarker, "second");

  const serializedResponses = JSON.stringify([
    firstOwn,
    firstOther,
    secondOwn,
    secondOther,
  ]);
  if (
    serializedResponses.includes(firstRoot)
    || serializedResponses.includes(secondRoot)
  ) {
    throw new Error("Workspace discovery exposed an absolute repository root.");
  }

  await Promise.all(sessions.map((session) => session.close()));
  console.log(
    "Verified install-once switching across two isolated workspace roots and "
    + "two independent Sanjaya MCP processes.",
  );
} finally {
  for (const session of sessions) {
    session.terminate();
  }
  rmSync(temporaryRoot, { recursive: true, force: true });
}

function assertIsolatedConfigurations(first, second) {
  if (
    first.name !== "sanjaya"
    || second.name !== "sanjaya"
    || first.command !== "npx"
    || second.command !== "npx"
    || first.args[1] !== `sanjaya-mcp@${releaseVersion}`
    || second.args[1] !== `sanjaya-mcp@${releaseVersion}`
    || first.args[2] !== "--root"
    || second.args[2] !== "--root"
    || first.args[3] !== firstRoot
    || second.args[3] !== secondRoot
  ) {
    throw new Error("Bound VS Code configurations drifted from the reviewed contract.");
  }
  if (
    JSON.stringify(first).includes(secondRoot)
    || JSON.stringify(second).includes(firstRoot)
  ) {
    throw new Error("Bound VS Code configurations mixed workspace roots.");
  }
}

async function searchWorkspace(session, ownMarker, otherMarker) {
  const own = await session.search(ownMarker);
  const other = await session.search(otherMarker);
  return [own, other];
}

function createSession(configuration, label) {
  const child = spawn(
    process.execPath,
    [launcherPath, ...configuration.args.slice(2)],
    {
      cwd: process.cwd(),
      env: {
        ...process.env,
        HTTPS_PROXY: "http://127.0.0.1:1",
      },
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    },
  );
  const output = createInterface({ input: child.stdout });
  const lines = output[Symbol.asyncIterator]();
  let stderr = "";
  let requestId = 0;
  let closed = false;
  const timeout = setTimeout(() => child.kill("SIGTERM"), 15_000);
  const exitPromise = new Promise((resolvePromise) => {
    child.once("exit", (code) => resolvePromise(code));
  });

  child.stderr.setEncoding("utf8");
  child.stderr.on("data", (chunk) => {
    stderr += chunk;
  });

  return {
    pid: child.pid,
    async initialize() {
      const response = await request("initialize", {
        protocolVersion: "2025-06-18",
        capabilities: {},
        clientInfo: {
          name: `sanjaya-workspace-switching-${label}`,
          version: "1.0",
        },
      });
      if (response?.result?.serverInfo?.name !== "sanjaya") {
        throw new Error(`${label} workspace did not initialize Sanjaya.`);
      }
      await send({
        jsonrpc: "2.0",
        method: "notifications/initialized",
      });
    },
    search(query) {
      return request("tools/call", {
        name: "search_text",
        arguments: { query },
      });
    },
    async close() {
      if (closed) {
        return;
      }
      closed = true;
      child.stdin.end();
      const exitCode = await exitPromise;
      clearTimeout(timeout);
      if (exitCode !== 0) {
        throw new Error(
          `${label} workspace server exited with ${exitCode}: ${stderr.trim()}`,
        );
      }
      if (stderr.trim()) {
        throw new Error(
          `${label} workspace server wrote stderr: ${stderr.trim()}`,
        );
      }
    },
    terminate() {
      clearTimeout(timeout);
      if (child.exitCode === null) {
        child.kill("SIGTERM");
      }
    },
  };

  async function request(method, params) {
    requestId += 1;
    const id = requestId;
    await send({ jsonrpc: "2.0", id, method, params });
    const response = await readMessage();
    if (response.id !== id) {
      throw new Error(`${label} workspace returned an unexpected response ID.`);
    }
    return response;
  }

  function send(message) {
    return new Promise((resolvePromise, rejectPromise) => {
      child.stdin.write(`${JSON.stringify(message)}\n`, (error) => {
        if (error) {
          rejectPromise(error);
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
        `${label} workspace closed stdout before replying: ${stderr.trim()}`,
      );
    }
    try {
      return JSON.parse(value);
    } catch (error) {
      throw new Error(
        `${label} workspace wrote non-JSON stdout: ${error.message}`,
      );
    }
  }
}

function assertSingleMarker(response, marker, label) {
  const content = response?.result?.structuredContent;
  const matches = content?.data?.matches;
  if (
    content?.status !== "ok"
    || !Array.isArray(matches)
    || matches.length !== 1
    || matches[0]?.path !== "marker.txt"
    || matches[0]?.snippet !== marker
  ) {
    throw new Error(`${label} workspace did not return its own marker.`);
  }
}

function assertNoMarker(response, marker, label) {
  const content = response?.result?.structuredContent;
  if (
    content?.status !== "ok"
    || !Array.isArray(content?.data?.matches)
    || content.data.matches.length !== 0
  ) {
    throw new Error(`${label} workspace leaked marker ${marker}.`);
  }
}

function canonicalTemporaryRoot() {
  const root = resolve(tmpdir());
  return process.platform === "darwin" && root.startsWith("/var/")
    ? `/private${root}`
    : root;
}
