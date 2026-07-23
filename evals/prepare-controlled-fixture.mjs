import { spawnSync } from "node:child_process";
import {
  appendFileSync,
  cpSync,
  existsSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import {
  basename,
  dirname,
  extname,
  join,
  resolve,
} from "node:path";
import { fileURLToPath } from "node:url";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const fixtureSource = join(evalRoot, "fixtures", "controlled");
const sourceExtensions = new Set([".cs", ".js", ".jsx", ".ts", ".tsx"]);
const generatedDirectoryNames = new Set([
  ".sanjaya",
  "bin",
  "node_modules",
  "obj",
]);
const profileTargets = new Map([
  ["core", null],
  ["medium", 1000],
  ["large", 5000],
]);

export function prepareControlledFixture({ profile = "core", output } = {}) {
  if (!profileTargets.has(profile)) {
    throw new Error("Fixture profile must be core, medium, or large.");
  }
  if (!existsSync(fixtureSource)) {
    throw new Error("The tracked controlled fixture is missing.");
  }

  let cleanupRoot = null;
  let repositoryRoot;
  if (output) {
    repositoryRoot = resolve(output);
    if (existsSync(repositoryRoot)) {
      throw new Error("The explicit fixture output path must not already exist.");
    }
  } else {
    cleanupRoot = mkdtempSync(join(tmpdir(), "sanjaya-eval-fixture-"));
    repositoryRoot = join(cleanupRoot, "signaldesk");
  }

  mkdirSync(dirname(repositoryRoot), { recursive: true });
  cpSync(fixtureSource, repositoryRoot, {
    recursive: true,
    errorOnExist: true,
    filter: (source) => !generatedDirectoryNames.has(basename(source)),
    force: false,
  });

  const target = profileTargets.get(profile);
  if (target !== null) {
    addScaleSources(repositoryRoot, target);
  }

  initializeRepository(repositoryRoot, profile);
  createWorkingTreeEvidence(repositoryRoot);

  const sourceFiles = countSourceFiles(repositoryRoot);
  if (target !== null && sourceFiles !== target) {
    throw new Error(
      `The ${profile} profile produced ${sourceFiles} source files instead of ${target}.`,
    );
  }

  return {
    cleanupRoot,
    repositoryRoot,
    profile,
    commit: git(repositoryRoot, ["rev-parse", "HEAD"]).trim(),
    sourceFiles,
    trackedFiles: git(repositoryRoot, ["ls-files"])
      .split(/\r?\n/u)
      .filter(Boolean)
      .length,
    workingTree: git(repositoryRoot, ["status", "--porcelain=v1"])
      .split(/\r?\n/u)
      .filter(Boolean),
  };
}

function addScaleSources(repositoryRoot, target) {
  const existing = countSourceFiles(repositoryRoot);
  if (existing > target) {
    throw new Error(
      `The controlled core already exceeds the ${target}-source-file profile.`,
    );
  }

  const extensions = [".cs", ".ts", ".js"];
  for (let index = existing; index < target; index += 1) {
    const extension = extensions[index % extensions.length];
    const shard = String(Math.floor(index / 100)).padStart(3, "0");
    const serial = String(index).padStart(5, "0");
    const directory = join(repositoryRoot, "scale", `shard-${shard}`);
    mkdirSync(directory, { recursive: true });
    writeFileSync(
      join(directory, `Generated${serial}${extension}`),
      generatedSource(extension, serial),
      "utf8",
    );
  }
}

function generatedSource(extension, serial) {
  if (extension === ".cs") {
    return [
      "namespace SignalDesk.Scale;",
      "",
      `public static class Generated${serial}`,
      "{",
      `    public const string Key = "scale-${serial}";`,
      "}",
      "",
    ].join("\n");
  }
  if (extension === ".ts") {
    return [
      `export interface Generated${serial} {`,
      `  key: "scale-${serial}";`,
      "}",
      "",
    ].join("\n");
  }
  return [
    `export const generated${serial} = Object.freeze({`,
    `  key: "scale-${serial}",`,
    "});",
    "",
  ].join("\n");
}

function initializeRepository(repositoryRoot, profile) {
  git(repositoryRoot, ["init", "--initial-branch=main"]);
  git(repositoryRoot, ["config", "core.autocrlf", "false"]);
  git(repositoryRoot, ["config", "core.filemode", "false"]);
  git(repositoryRoot, ["add", "--all"]);
  commit(repositoryRoot, `Create SignalDesk ${profile} fixture`, 0);

  replaceExact(
    join(repositoryRoot, "config", "runtime.json"),
    '"retryBaseSeconds": 5',
    '"retryBaseSeconds": 7',
  );
  git(repositoryRoot, ["add", "config/runtime.json"]);
  commit(repositoryRoot, "Tune the runtime retry base delay", 1);

  replaceExact(
    join(
      repositoryRoot,
      "frontend",
      "src",
      "app",
      "core",
      "runtime-config.ts",
    ),
    "retryBaseSeconds: 5",
    "retryBaseSeconds: 7",
  );
  git(repositoryRoot, ["add", "frontend/src/app/core/runtime-config.ts"]);
  commit(repositoryRoot, "Align the retry preview default", 2);
}

function createWorkingTreeEvidence(repositoryRoot) {
  appendFileSync(
    join(repositoryRoot, "docs", "operator-runbook.md"),
    "\nLocal observation: verify the pager route before the next drill.\n",
    "utf8",
  );
  writeFileSync(
    join(repositoryRoot, "local-observation.txt"),
    "The pager drill remains pending review.\n",
    "utf8",
  );

  const excludedDirectory = join(repositoryRoot, "node_modules", "signaldesk-decoy");
  mkdirSync(excludedDirectory, { recursive: true });
  writeFileSync(
    join(excludedDirectory, "excluded-marker.ts"),
    'export const excluded = "SIGNAL_DESK_EXCLUDED_MARKER";\n',
    "utf8",
  );
}

function commit(repositoryRoot, message, dayOffset) {
  const day = String(15 + dayOffset).padStart(2, "0");
  const date = `2026-01-${day}T12:00:00Z`;
  git(repositoryRoot, ["commit", "--quiet", "-m", message], {
    GIT_AUTHOR_NAME: "Sanjaya Eval Fixture",
    GIT_AUTHOR_EMAIL: "eval-fixture@example.invalid",
    GIT_AUTHOR_DATE: date,
    GIT_COMMITTER_NAME: "Sanjaya Eval Fixture",
    GIT_COMMITTER_EMAIL: "eval-fixture@example.invalid",
    GIT_COMMITTER_DATE: date,
  });
}

function replaceExact(path, before, after) {
  const source = readFileSync(path, "utf8");
  if (!source.includes(before) || source.includes(after)) {
    throw new Error(`Fixture transition precondition failed for ${path}.`);
  }
  writeFileSync(path, source.replace(before, after), "utf8");
}

function countSourceFiles(root) {
  let count = 0;
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    if (entry.name === ".git" || generatedDirectoryNames.has(entry.name)) {
      continue;
    }
    const path = join(root, entry.name);
    if (entry.isDirectory()) {
      count += countSourceFiles(path);
    } else if (entry.isFile() && sourceExtensions.has(extname(entry.name))) {
      count += 1;
    }
  }
  return count;
}

function git(repositoryRoot, argumentsToPass, extraEnvironment = {}) {
  const result = spawnSync("git", argumentsToPass, {
    cwd: repositoryRoot,
    encoding: "utf8",
    env: {
      ...process.env,
      GIT_CONFIG_GLOBAL: process.platform === "win32" ? "NUL" : "/dev/null",
      GIT_CONFIG_NOSYSTEM: "1",
      LC_ALL: "C",
      TZ: "UTC",
      ...extraEnvironment,
    },
    windowsHide: true,
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(
      `git ${argumentsToPass[0]} failed: ${result.stderr.trim()}`,
    );
  }
  return result.stdout;
}

const invokedPath = process.argv[1] ? resolve(process.argv[1]) : null;
if (invokedPath === fileURLToPath(import.meta.url)) {
  const { profile, output } = parseArguments(process.argv.slice(2));
  const prepared = prepareControlledFixture({ profile, output });
  process.stdout.write(`${JSON.stringify(prepared, null, 2)}\n`);
}

function parseArguments(argumentsToParse) {
  let profile = "core";
  let output;
  for (let index = 0; index < argumentsToParse.length; index += 1) {
    const argument = argumentsToParse[index];
    if (argument === "--profile") {
      profile = argumentsToParse[index + 1];
      index += 1;
    } else if (argument === "--output") {
      output = argumentsToParse[index + 1];
      index += 1;
    } else {
      throw new Error(`Unknown fixture argument: ${argument}`);
    }
  }
  if (!profile || (argumentsToParse.includes("--output") && !output)) {
    throw new Error("Fixture arguments require non-empty values.");
  }
  return { profile, output };
}
