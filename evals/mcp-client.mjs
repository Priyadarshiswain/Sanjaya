import { spawn } from "node:child_process";
import { delimiter } from "node:path";
import { createInterface } from "node:readline";

export async function withSanjaya({
  launcherPath,
  repositoryRoot,
  timeoutMs = 300_000,
}, action) {
  const child = spawn(
    process.execPath,
    [launcherPath, "--root", repositoryRoot],
    {
      env: isolatedEnvironment(),
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    },
  );
  const lines = createInterface({ input: child.stdout })[Symbol.asyncIterator]();
  let stderr = "";
  let nextId = 1;
  child.stderr.setEncoding("utf8");
  child.stderr.on("data", (chunk) => {
    stderr += chunk;
  });
  const timeout = setTimeout(() => child.kill("SIGTERM"), timeoutMs);

  async function request(method, params) {
    const id = nextId;
    nextId += 1;
    child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", id, method, params })}\n`);
    const { value, done } = await lines.next();
    if (done || !value) {
      throw new Error(`Sanjaya ended before replying: ${stderr.trim()}`);
    }
    return JSON.parse(value);
  }

  const client = {
    async call(name, args = {}) {
      const response = await request("tools/call", {
        name,
        arguments: args,
      });
      return response?.result?.structuredContent;
    },
    async listTools() {
      const response = await request("tools/list", {});
      return response?.result?.tools ?? [];
    },
  };

  try {
    await request("initialize", {
      protocolVersion: "2025-06-18",
      capabilities: {},
      clientInfo: { name: "sanjaya-eval", version: "1.0" },
    });
    child.stdin.write(`${JSON.stringify({
      jsonrpc: "2.0",
      method: "notifications/initialized",
    })}\n`);
    return await action(client);
  } finally {
    clearTimeout(timeout);
    child.stdin.end();
    if (child.exitCode === null) {
      child.kill("SIGTERM");
    }
  }
}

function isolatedEnvironment() {
  const dotnetRoot = process.env.DOTNET_ROOT;
  return {
    ...process.env,
    PATH: dotnetRoot
      ? `${dotnetRoot}${delimiter}${process.env.PATH ?? ""}`
      : process.env.PATH,
    HTTP_PROXY: "http://127.0.0.1:1",
    HTTPS_PROXY: "http://127.0.0.1:1",
    ALL_PROXY: "http://127.0.0.1:1",
    NO_PROXY: "",
    LC_ALL: "C",
    TZ: "UTC",
  };
}
