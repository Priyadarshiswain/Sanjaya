import { spawn } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createInterface } from "node:readline";

const repositoryRoot = mkdtempSync(join(tmpdir(), "sanjaya-launcher-"));
writeFileSync(join(repositoryRoot, "marker.txt"), "LAUNCHER_UNIQUE_MARKER\n");
writeFileSync(
  join(repositoryRoot, "Sample.cs"),
  "namespace Launcher; public class Sample { public void Run() { } }\n",
);

const child = spawn(process.execPath, ["bin/sanjaya-mcp.js", "--root", repositoryRoot], {
  cwd: process.cwd(),
  stdio: ["pipe", "pipe", "pipe"],
  windowsHide: true,
});
const exitPromise = new Promise((resolve) => child.once("exit", resolve));
const output = createInterface({ input: child.stdout });
const lines = output[Symbol.asyncIterator]();
let stderr = "";

child.stderr.setEncoding("utf8");
child.stderr.on("data", (chunk) => {
  stderr += chunk;
});

const timeout = setTimeout(() => child.kill("SIGTERM"), 10_000);

try {
  await send({
    jsonrpc: "2.0",
    id: 1,
    method: "initialize",
    params: {
      protocolVersion: "2025-06-18",
      capabilities: {},
      clientInfo: { name: "sanjaya-launcher-check", version: "1.0" },
    },
  });

  const initialize = await readMessage();
  if (initialize?.result?.serverInfo?.name !== "sanjaya") {
    throw new Error("Launcher did not complete the Sanjaya initialize handshake.");
  }

  await send({ jsonrpc: "2.0", method: "notifications/initialized" });
  await send({ jsonrpc: "2.0", id: 2, method: "tools/list", params: {} });

  const list = await readMessage();
  const toolNames = list?.result?.tools?.map((tool) => tool.name);
  const expectedTools = [
    "capabilities",
    "file_outline",
    "health_check",
    "index_codebase",
    "recent_changes",
    "search_code",
    "search_text",
  ];
  if (JSON.stringify(toolNames?.sort()) !== JSON.stringify(expectedTools)) {
    throw new Error("Launcher did not expose exactly the implemented tools.");
  }

  await send({
    jsonrpc: "2.0",
    id: 3,
    method: "tools/call",
    params: { name: "search_text", arguments: { query: "LAUNCHER_UNIQUE_MARKER" } },
  });
  const search = await readMessage();
  const match = search?.result?.structuredContent?.data?.matches?.[0];
  if (match?.path !== "marker.txt") {
    throw new Error("Launcher did not forward the explicit repository root.");
  }

  if (JSON.stringify(search).includes(repositoryRoot)) {
    throw new Error("Launcher response exposed the absolute repository root.");
  }

  await send({
    jsonrpc: "2.0",
    id: 4,
    method: "tools/call",
    params: { name: "file_outline", arguments: { path: "Sample.cs" } },
  });
  const outline = await readMessage();
  const outlineContent = outline?.result?.structuredContent;
  if (
    outlineContent?.provider !== "csharp-roslyn-syntax" ||
    outlineContent?.data?.items?.[0]?.kind !== "namespace"
  ) {
    throw new Error("Launcher did not expose the Roslyn-backed C# outline.");
  }

  if (JSON.stringify(outline).includes(repositoryRoot)) {
    throw new Error("C# outline exposed the absolute repository root.");
  }

  await send({
    jsonrpc: "2.0",
    id: 5,
    method: "tools/call",
    params: { name: "index_codebase", arguments: {} },
  });
  const index = await readMessage();
  const indexContent = index?.result?.structuredContent;
  if (
    indexContent?.data?.indexPath !== ".sanjaya/index-v1.json" ||
    indexContent?.data?.filesIndexed !== 1 ||
    indexContent?.data?.chunksIndexed < 1
  ) {
    throw new Error("Launcher did not build the bounded C# structural index.");
  }

  if (JSON.stringify(index).includes(repositoryRoot)) {
    throw new Error("Index response exposed the absolute repository root.");
  }

  await send({
    jsonrpc: "2.0",
    id: 6,
    method: "tools/call",
    params: { name: "search_code", arguments: { query: "Run" } },
  });
  const codeSearch = await readMessage();
  const codeMatch = codeSearch?.result?.structuredContent?.data?.matches?.[0];
  if (codeMatch?.path !== "Sample.cs" || codeMatch?.name !== "Run") {
    throw new Error("Launcher did not search the current structural index.");
  }

  if (JSON.stringify(codeSearch).includes(repositoryRoot)) {
    throw new Error("Indexed search response exposed the absolute repository root.");
  }

  await send({
    jsonrpc: "2.0",
    id: 7,
    method: "tools/call",
    params: { name: "recent_changes", arguments: {} },
  });
  const recent = await readMessage();
  if (recent?.result?.structuredContent?.error?.code !== "not_git_repository") {
    throw new Error("Launcher did not expose stable local Git readiness guidance.");
  }

  child.stdin.end();
  const exitCode = await exitPromise;
  if (exitCode !== 0) {
    throw new Error(`Launcher exited with code ${exitCode}. ${stderr.trim()}`);
  }

  if (stderr.trim().length > 0) {
    throw new Error(`Launcher wrote an unexpected diagnostic: ${stderr.trim()}`);
  }

  console.log("npm launcher completed the Sanjaya MCP handshake.");
} finally {
  clearTimeout(timeout);
  if (child.exitCode === null) {
    child.kill("SIGTERM");
  }

  rmSync(repositoryRoot, { recursive: true, force: true });
}

function send(message) {
  return new Promise((resolve, reject) => {
    child.stdin.write(`${JSON.stringify(message)}\n`, (error) => {
      if (error) {
        reject(error);
      } else {
        resolve();
      }
    });
  });
}

async function readMessage() {
  const { value, done } = await lines.next();
  if (done || !value) {
    throw new Error(`Launcher closed stdout before replying. ${stderr.trim()}`);
  }

  try {
    return JSON.parse(value);
  } catch (error) {
    throw new Error(`Launcher wrote non-JSON stdout: ${error.message}`);
  }
}
