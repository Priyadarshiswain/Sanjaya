import { spawn } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createInterface } from "node:readline";

const repositoryRoot = mkdtempSync(join(tmpdir(), "sanjaya-launcher-"));
writeFileSync(join(repositoryRoot, "marker.txt"), "LAUNCHER_UNIQUE_MARKER\n");
writeFileSync(
  join(repositoryRoot, "Sample.cs"),
  "namespace Launcher; public class Sample { public void Run() { } public void Call() { Run(); } }\n",
);
writeFileSync(
  join(repositoryRoot, "Widget.ts"),
  "export interface Widget { id: number }\nexport const createWidget = (): Widget => ({ id: 1 });\n",
);
writeFileSync(
  join(repositoryRoot, "Panel.jsx"),
  "export class Panel { render() { return <section />; } }\n",
);

const child = spawn(process.execPath, ["bin/sanjaya-mcp.js", "--root", repositoryRoot], {
  cwd: process.cwd(),
  env: {
    ...process.env,
    NODE_PATH: join(repositoryRoot, "ambient-node-modules-must-not-load"),
    SANJAYA_NODE_EXECUTABLE: "ambient-runtime-must-not-win",
    HTTPS_PROXY: "http://127.0.0.1:1",
  },
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
    "find_definition",
    "find_references",
    "get_source",
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
    id: 11,
    method: "tools/call",
    params: { name: "file_outline", arguments: { path: "Widget.ts" } },
  });
  const typeScriptOutline = await readMessage();
  const typeScriptContent = typeScriptOutline?.result?.structuredContent;
  if (
    typeScriptContent?.provider !== "typescript-compiler-syntax" ||
    typeScriptContent?.data?.items?.[0]?.name !== "Widget"
  ) {
    throw new Error("Launcher did not expose the TypeScript compiler-backed outline.");
  }

  await send({
    jsonrpc: "2.0",
    id: 12,
    method: "tools/call",
    params: { name: "file_outline", arguments: { path: "Panel.jsx" } },
  });
  const javaScriptOutline = await readMessage();
  if (
    javaScriptOutline?.result?.structuredContent?.provider !== "javascript-typescript-syntax" ||
    javaScriptOutline?.result?.structuredContent?.data?.items?.[0]?.name !== "Panel"
  ) {
    throw new Error("Launcher did not expose the JSX compiler-backed outline.");
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
    indexContent?.data?.filesIndexed !== 3 ||
    indexContent?.data?.chunksIndexed < 1
  ) {
    throw new Error("Launcher did not build the bounded mixed-language structural index.");
  }
  const indexedProviderIds = indexContent?.data?.providers?.map((provider) => provider.id).sort();
  if (JSON.stringify(indexedProviderIds) !== JSON.stringify([
    "csharp-roslyn-syntax",
    "javascript-typescript-syntax",
    "typescript-compiler-syntax",
  ])) {
    throw new Error("Launcher index did not report exact mixed-language providers.");
  }
  const indexPath = join(repositoryRoot, ".sanjaya", "index-v1.json");
  const firstIndexBytes = readFileSync(indexPath);
  const indexDocument = JSON.parse(firstIndexBytes);
  const expectedChunkLabels = [
    ["Sample.cs", "csharp-roslyn-syntax", "csharp"],
    ["Widget.ts", "typescript-compiler-syntax", "typescript"],
    ["Panel.jsx", "javascript-typescript-syntax", "javascript"],
  ];
  for (const [path, provider, language] of expectedChunkLabels) {
    if (!indexDocument.chunks.some((chunk) =>
      chunk.path === path && chunk.provider === provider && chunk.language === language)) {
      throw new Error(`Launcher index omitted the exact ${language} provider label.`);
    }
  }

  await send({
    jsonrpc: "2.0",
    id: 15,
    method: "tools/call",
    params: { name: "index_codebase", arguments: {} },
  });
  const rebuiltIndex = await readMessage();
  if (
    rebuiltIndex?.result?.structuredContent?.data?.repositoryFingerprint !== indexContent.data.repositoryFingerprint ||
    !readFileSync(indexPath).equals(firstIndexBytes)
  ) {
    throw new Error("Mixed-language index rebuild was not byte-for-byte deterministic.");
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
    id: 13,
    method: "tools/call",
    params: { name: "search_code", arguments: { query: "createWidget" } },
  });
  const typeScriptSearch = await readMessage();
  const typeScriptMatch = typeScriptSearch?.result?.structuredContent?.data?.matches?.[0];
  if (typeScriptMatch?.path !== "Widget.ts" || typeScriptMatch?.name !== "createWidget") {
    throw new Error("Launcher did not search the TypeScript structural index.");
  }

  await send({ jsonrpc: "2.0", id: 14, method: "tools/call", params: { name: "capabilities", arguments: {} } });
  const capabilities = await readMessage();
  const providerStatuses = new Map(
    capabilities?.result?.structuredContent?.data?.providers?.map((provider) => [provider.id, provider.status]),
  );
  if (
    providerStatuses.get("typescript-compiler-syntax") !== "supported" ||
    providerStatuses.get("javascript-typescript-syntax") !== "supported"
  ) {
    throw new Error("Launcher did not report active TypeScript and JavaScript providers.");
  }

  await send({
    jsonrpc: "2.0",
    id: 7,
    method: "tools/call",
    params: {
      name: "find_definition",
      arguments: { name: "Run", kind: "method", container: "Launcher.Sample" },
    },
  });
  const definition = await readMessage();
  const definitionContent = definition?.result?.structuredContent;
  const definitionMatch = definitionContent?.data?.matches?.[0];
  if (
    definitionContent?.data?.resolution !== "unique" ||
    definitionMatch?.path !== "Sample.cs" ||
    definitionMatch?.name !== "Run"
  ) {
    throw new Error("Launcher did not resolve the indexed C# syntax definition.");
  }

  if (JSON.stringify(definition).includes(repositoryRoot)) {
    throw new Error("Definition lookup response exposed the absolute repository root.");
  }

  await send({
    jsonrpc: "2.0",
    id: 8,
    method: "tools/call",
    params: { name: "find_references", arguments: { name: "Run" } },
  });
  const references = await readMessage();
  const referenceContent = references?.result?.structuredContent;
  if (
    referenceContent?.data?.classification !== "syntax_candidate" ||
    referenceContent?.data?.totalMatches !== 1 ||
    referenceContent?.data?.matches?.[0]?.path !== "Sample.cs"
  ) {
    throw new Error("Launcher did not return the C# syntax reference candidate.");
  }

  if (JSON.stringify(references).includes(repositoryRoot)) {
    throw new Error("Reference lookup response exposed the absolute repository root.");
  }

  await send({
    jsonrpc: "2.0",
    id: 9,
    method: "tools/call",
    params: { name: "get_source", arguments: { chunkId: definitionMatch.chunkId } },
  });
  const source = await readMessage();
  const sourceContent = source?.result?.structuredContent;
  if (
    sourceContent?.data?.path !== "Sample.cs" ||
    sourceContent?.data?.source !== "public void Run() { }" ||
    sourceContent?.data?.complete !== true
  ) {
    throw new Error("Launcher did not return exact indexed C# declaration source.");
  }

  if (JSON.stringify(source).includes(repositoryRoot)) {
    throw new Error("Source retrieval response exposed the absolute repository root.");
  }

  await send({
    jsonrpc: "2.0",
    id: 10,
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
