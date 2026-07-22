import { spawn } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { createInterface } from "node:readline";
import { resolve } from "node:path";

const workerPath = resolve("dist/dotnet/runtime/typescript/typescript-worker.mjs");
const compilerRoot = resolve("dist/dotnet/third_party/typescript/package");
if (!existsSync(workerPath) || !existsSync(resolve(compilerRoot, "lib/typescript.js"))) {
  throw new Error("Published TypeScript worker or compiler is missing; run npm run build first.");
}

const workerSource = readFileSync(workerPath, "utf8");
const imports = [...workerSource.matchAll(/from\s+["']([^"']+)["']/gu)].map((match) => match[1]);
assertEqual(
  imports,
  ["node:module", "node:path", "node:util", "node:url"],
  "Worker built-in imports changed without a boundary review.",
);
for (const forbidden of ["node:net", "node:http", "node:https", "node:dns", "node:tls", "node:dgram", "fetch(", "eval("]) {
  assert(!workerSource.includes(forbidden), `Worker contains an unreviewed capability: ${forbidden}`);
}

const child = spawn(process.execPath, [
  "--permission",
  `--allow-fs-read=${resolve("dist/dotnet/runtime/typescript")}`,
  `--allow-fs-read=${compilerRoot}`,
  "--no-addons",
  "--no-global-search-paths",
  "--disable-proto=throw",
  "--no-warnings",
  "--max-old-space-size=256",
  workerPath,
], {
  cwd: resolve("dist/dotnet/runtime/typescript"),
  env: { TZ: "UTC" },
  stdio: ["pipe", "pipe", "pipe"],
  windowsHide: true,
});
const lines = createInterface({ input: child.stdout })[Symbol.asyncIterator]();
let stderr = "";
child.stderr.setEncoding("utf8");
child.stderr.on("data", (chunk) => { stderr += chunk; });
const exitPromise = new Promise((resolveExit) => child.once("exit", resolveExit));
const timeout = setTimeout(() => child.kill("SIGTERM"), 10_000);

try {
  const handshake = await exchange({ protocolVersion: "1", requestId: "1", operation: "handshake" });
  assert(handshake.status === "ok", "Worker handshake failed.");
  assert(handshake.protocolVersion === "1", "Worker protocol version drifted.");
  assert(handshake.typescriptVersion === "6.0.3", "Worker loaded an unexpected TypeScript version.");

  const fixtures = [
    ["typescript", "types/sample.d.ts", "export interface Item { id: number }", "Item", "interface"],
    ["typescript", "src/view.tsx", "export default function View() { return <main />; }", "View", "function"],
    ["typescript", "src/module.mts", "export const make = () => 1;", "make", "function"],
    ["typescript", "src/common.cts", "class Common {}", "Common", "class"],
    ["javascript", "src/item.js", "export class Item { run() {} }", "Item", "class"],
    ["javascript", "src/view.jsx", "export default function View() { return <main />; }", "View", "function"],
    ["javascript", "src/module.mjs", "export const make = () => 1;", "make", "function"],
    ["javascript", "src/common.cjs", "class Common { run() {} }", "Common", "class"],
  ];

  let requestId = 2;
  for (const [language, relativePath, sourceText, name, kind] of fixtures) {
    const response = await exchange({
      protocolVersion: "1",
      requestId: String(requestId++),
      operation: "analyze",
      language,
      relativePath,
      sourceText,
    });
    assert(response.status === "ok", `${relativePath} analysis failed.`);
    const item = response.items.find((candidate) => candidate.name === name && candidate.kind === kind);
    assert(item, `${relativePath} did not return the expected declaration.`);
    assert(response.items.length === response.chunks.length, `${relativePath} item/chunk counts differ.`);
    assert(!JSON.stringify(response).includes(resolve("dist")), `${relativePath} leaked an absolute build path.`);
  }

  const inertSource = `
    import { writeFileSync } from "node:fs";
    export const tryNetwork = () => fetch("https://example.invalid");
    export const tryWrite = () => writeFileSync("should-not-exist", "x");
    export const tryProcess = () => process.exit(7);
  `;
  const inert = await exchange({
    protocolVersion: "1",
    requestId: String(requestId++),
    operation: "analyze",
    language: "typescript",
    relativePath: "src/inert.ts",
    sourceText: inertSource,
  });
  assert(inert.status === "ok", "Source containing side-effect APIs was not safely parsed as inert text.");
  assertEqual(inert.items.map((item) => item.name), ["tryNetwork", "tryWrite", "tryProcess"], "Inert declarations changed.");
  assert(!existsSync(resolve("dist/dotnet/runtime/typescript/should-not-exist")), "Project source was executed.");

  const nestedRequest = {
    protocolVersion: "1",
    requestId: String(requestId++),
    operation: "analyze",
    language: "typescript",
    relativePath: "src/nested.ts",
    sourceText: `
      namespace Outer {
        export class Inner { run() {} }
        export const make = () => new Inner();
      }
      export default class { render() {} }
    `,
  };
  const nested = await exchange(nestedRequest);
  assert(nested.status === "ok", "Nested TypeScript fixture failed.");
  assert(
    nested.items.some((item) => item.name === "run" && item.container === "Outer.Inner"),
    "Nested class method container was not preserved.",
  );
  assert(
    nested.items.some((item) => item.name === "make" && item.kind === "function" && item.container === "Outer"),
    "Arrow-function variable was not classified structurally.",
  );
  assert(
    nested.items.some((item) => item.name === "default" && item.kind === "class"),
    "Anonymous default export was not named deterministically.",
  );
  assert(
    nested.items.some((item) => item.name === "render" && item.container === "default"),
    "Anonymous default-export container was not preserved.",
  );

  const repeat = await exchange({ ...nestedRequest, requestId: String(requestId++) });
  assertEqual(
    { ...nested, requestId: "stable" },
    { ...repeat, requestId: "stable" },
    "Repeated worker analysis was not deterministic.",
  );

  const malformed = await exchange({
    protocolVersion: "1",
    requestId: String(requestId++),
    operation: "analyze",
    language: "javascript",
    relativePath: "src/broken.js",
    sourceText: "export class Broken { run( { }",
  });
  assert(malformed.status === "ok", "Malformed source did not return bounded recovered structure.");
  assert(malformed.syntaxDiagnosticCount > 0, "Malformed source did not report syntax recovery.");

  const manyDeclarations = Array.from({ length: 501 }, (_, index) => `export class C${index} {}`).join("\n");
  const truncated = await exchange({
    protocolVersion: "1",
    requestId: String(requestId++),
    operation: "analyze",
    language: "typescript",
    relativePath: "src/many.ts",
    sourceText: manyDeclarations,
  });
  assert(truncated.items.length === 500, "Worker did not enforce the declaration bound.");
  assert(truncated.itemsTruncated === true && truncated.chunksTruncated === true, "Worker did not report truncation.");

  const invalid = await exchange({
    protocolVersion: "1",
    requestId: String(requestId++),
    operation: "handshake",
    unexpected: true,
  });
  assert(invalid.status === "error", "Worker accepted an unexpected protocol field.");

  child.stdin.end();
  const exitCode = await exitPromise;
  assert(exitCode === 0, `Worker exited with ${exitCode}: ${stderr.trim()}`);
  assert(stderr.length === 0, `Worker wrote an unexpected diagnostic: ${stderr.trim()}`);
  console.log("TypeScript/JavaScript worker protocol, syntax fixtures, and fixed execution boundary verified.");
} finally {
  clearTimeout(timeout);
  if (child.exitCode === null) {
    child.kill("SIGTERM");
  }
}

function exchange(message) {
  return new Promise((resolveExchange, reject) => {
    child.stdin.write(`${JSON.stringify(message)}\n`, async (error) => {
      if (error) {
        reject(error);
        return;
      }
      try {
        const next = await lines.next();
        if (next.done || !next.value) {
          reject(new Error(`Worker closed stdout before replying: ${stderr.trim()}`));
          return;
        }
        resolveExchange(JSON.parse(next.value));
      } catch (readError) {
        reject(readError);
      }
    });
  });
}

function assertEqual(actual, expected, message) {
  assert(JSON.stringify(actual) === JSON.stringify(expected), message);
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
