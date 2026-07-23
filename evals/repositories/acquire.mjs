import { spawnSync } from "node:child_process";
import {
  existsSync,
  mkdirSync,
  readFileSync,
  readdirSync,
} from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const manifest = JSON.parse(
  readFileSync(join(evalRoot, "repositories", "manifest.json"), "utf8"),
);
const outputIndex = process.argv.indexOf("--output");
if (outputIndex < 0 || !process.argv[outputIndex + 1]) {
  throw new Error("Usage: node repositories/acquire.mjs --output <directory>");
}
const outputRoot = resolve(process.argv[outputIndex + 1]);
mkdirSync(outputRoot, { recursive: true });

for (const repository of manifest.repositories) {
  if (repository.originKind !== "public_git") {
    continue;
  }
  const target = join(outputRoot, repository.id);
  if (existsSync(target)) {
    const entries = readdirSync(target);
    if (entries.length === 0) {
      run(["init"], target);
    } else {
      verifyExisting(target, repository);
      continue;
    }
  } else {
    mkdirSync(target, { recursive: true });
    run(["init"], target);
  }
  run(["remote", "add", "origin", repository.url], target);
  run(["fetch", "--depth=1", "origin", repository.commit], target);
  run(["checkout", "--detach", repository.commit], target);
  verifyExisting(target, repository);
}

console.log(`Acquired and verified pilot repositories under ${outputRoot}.`);

function verifyExisting(root, repository) {
  const commit = run(["rev-parse", "HEAD"], root).trim();
  if (commit !== repository.commit) {
    throw new Error(
      `${repository.id} is at ${commit}; expected ${repository.commit}.`,
    );
  }
  const origin = run(["remote", "get-url", "origin"], root).trim();
  if (origin.replace(/\.git$/u, "") !== repository.url.replace(/\.git$/u, "")) {
    throw new Error(`${repository.id} has unexpected origin ${origin}.`);
  }
}

function run(args, cwd) {
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
