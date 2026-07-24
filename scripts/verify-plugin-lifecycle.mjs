import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { randomUUID } from "node:crypto";
import { existsSync, readFileSync } from "node:fs";
import { basename, dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  createMarketplaceFixture,
  fileManifest,
  marketplaceName,
  removeMarketplaceFixture,
} from "./plugin-marketplace-contract.mjs";

const dockerImage = "node@sha256:"
  + "6c74791e557ce11fc957704f6d4fe134a7bc8d6f5ca4403205b2966bd488f6b3";
const codexPackage = "@openai/codex@0.144.5";
const codexVersion = "codex-cli 0.144.5";
const pluginName = "sanjaya";
const pluginId = `${pluginName}@${marketplaceName}`;
const initialVersion = "0.1.0";
const updatedVersion = "0.1.0+codex.lifecycle-test";
const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const cachebusterHelper = parseCachebusterHelper(process.argv.slice(2));
const fixture = createMarketplaceFixture(repositoryRoot);
const containerName = `sanjaya-plugin-lifecycle-${randomUUID().slice(0, 12)}`;
const canonicalPluginRoot = join(repositoryRoot, "plugins", "sanjaya");
const initialPluginManifest = fileManifest(canonicalPluginRoot);
const initialSkillManifest = initialPluginManifest.filter(
  ({ path }) => path !== ".codex-plugin/plugin.json",
);
let containerStarted = false;

try {
  assert.equal(
    readPluginVersion(fixture.temporaryPluginRoot),
    initialVersion,
    "The lifecycle test must begin from the reviewed plugin version.",
  );
  requireCommand("docker", ["version", "--format", "{{.Server.Version}}"]);

  const containerId = runDocker([
    "run",
    "--detach",
    "--name",
    containerName,
    "--mount",
    `type=bind,source=${fixture.marketplaceRoot},target=/marketplace,readonly`,
    dockerImage,
    "sleep",
    "infinity",
  ]).stdout.trim();
  containerStarted = true;
  assert.match(containerId, /^[a-f0-9]{64}$/u);

  verifyContainerMount();
  runContainer([
    "npm",
    "install",
    "--global",
    "--no-audit",
    "--no-fund",
    codexPackage,
  ]);
  assert.equal(runContainer(["codex", "--version"]).stdout.trim(), codexVersion);

  runDocker(["network", "disconnect", "bridge", containerName]);
  verifyNetworkUnavailable();

  const addedMarketplace = runCodexJson([
    "plugin",
    "marketplace",
    "add",
    "/marketplace",
    "--json",
  ]);
  assert.deepEqual(addedMarketplace, {
    marketplaceName,
    installedRoot: "/marketplace",
    alreadyAdded: false,
  });
  assert.deepEqual(
    runCodexJson(["plugin", "marketplace", "list", "--json"]),
    {
      marketplaces: [
        {
          name: marketplaceName,
          root: "/marketplace",
          marketplaceSource: {
            sourceType: "local",
            source: "/marketplace",
          },
        },
      ],
    },
  );

  assert.deepEqual(
    runCodexJson([
      "plugin",
      "list",
      "--marketplace",
      marketplaceName,
      "--available",
      "--json",
    ]),
    pluginList({
      version: initialVersion,
      installed: false,
      enabled: false,
    }),
  );

  const initialInstall = runCodexJson([
    "plugin",
    "add",
    pluginId,
    "--json",
  ]);
  const initialInstalledPath = installedPath(initialVersion);
  assert.deepEqual(initialInstall, {
    pluginId,
    name: pluginName,
    marketplaceName,
    version: initialVersion,
    installedPath: initialInstalledPath,
    authPolicy: "ON_INSTALL",
  });
  assert.deepEqual(
    runCodexJson(["plugin", "list", "--marketplace", marketplaceName, "--json"]),
    pluginList({
      version: initialVersion,
      installed: true,
      enabled: true,
    }),
  );
  assert.deepEqual(
    readContainerManifest(initialInstalledPath),
    initialPluginManifest,
    "The installed plugin differs from the reviewed marketplace source.",
  );

  runHost("python3", [
    cachebusterHelper,
    fixture.temporaryPluginRoot,
    "--cachebuster",
    "lifecycle-test",
  ]);
  assert.equal(readPluginVersion(fixture.temporaryPluginRoot), updatedVersion);
  assert.deepEqual(
    fileManifest(fixture.temporaryPluginRoot).filter(
      ({ path }) => path !== ".codex-plugin/plugin.json",
    ),
    initialSkillManifest,
    "The update helper altered skill content.",
  );

  const updatedInstall = runCodexJson([
    "plugin",
    "add",
    pluginId,
    "--json",
  ]);
  const updatedInstalledPath = installedPath(updatedVersion);
  assert.deepEqual(updatedInstall, {
    pluginId,
    name: pluginName,
    marketplaceName,
    version: updatedVersion,
    installedPath: updatedInstalledPath,
    authPolicy: "ON_INSTALL",
  });
  assert.deepEqual(
    runCodexJson(["plugin", "list", "--marketplace", marketplaceName, "--json"]),
    pluginList({
      version: updatedVersion,
      installed: true,
      enabled: true,
    }),
  );
  assert.deepEqual(
    readContainerManifest(updatedInstalledPath),
    fileManifest(fixture.temporaryPluginRoot),
    "The reinstalled plugin differs from the cachebuster-updated source.",
  );
  assert.equal(
    containerPathExists(initialInstalledPath),
    false,
    "Reinstall retained the previous plugin cache.",
  );

  const removedPlugin = runCodexJson([
    "plugin",
    "remove",
    pluginId,
    "--json",
  ]);
  assert.deepEqual(removedPlugin, {
    pluginId,
    name: pluginName,
    marketplaceName,
  });
  assert.deepEqual(
    runCodexJson([
      "plugin",
      "list",
      "--marketplace",
      marketplaceName,
      "--available",
      "--json",
    ]),
    pluginList({
      version: updatedVersion,
      installed: false,
      enabled: false,
    }),
  );
  assert.deepEqual(
    readContainerManifest(
      `/root/.codex/plugins/cache/${marketplaceName}/${pluginName}`,
    ),
    [],
    "Plugin removal retained cached plugin files.",
  );

  const removedMarketplace = runCodexJson([
    "plugin",
    "marketplace",
    "remove",
    marketplaceName,
    "--json",
  ]);
  assert.deepEqual(removedMarketplace, {
    marketplaceName,
    installedRoot: null,
  });
  assert.deepEqual(
    runCodexJson(["plugin", "marketplace", "list", "--json"]),
    { marketplaces: [] },
  );
  assert.deepEqual(
    runCodexJson(["plugin", "list", "--available", "--json"]),
    { installed: [], available: [] },
  );
  assert.equal(
    readContainerFile("/root/.codex/config.toml").trim(),
    "",
    "Plugin removal left active Codex configuration.",
  );

  const pluginHelp = runContainer(["codex", "plugin", "--help"]).stdout;
  assert.ok(
    !/^\s+disable\s+/mu.test(pluginHelp),
    "The lifecycle verifier must be updated when a noninteractive disable command becomes available.",
  );

  console.log(
    `Verified ${pluginId} discovery, install, fresh-process enablement, `
    + "cachebuster update, reinstall, removal, and cleanup in a "
    + `network-isolated ${codexVersion} container. Interactive disable remains `
    + "a documented manual check because the CLI exposes no disable command.",
  );
} finally {
  try {
    if (containerStarted) {
      runDocker(["rm", "--force", containerName]);
      const inspection = runHost(
        "docker",
        ["container", "inspect", containerName],
        { acceptedStatuses: [1] },
      );
      assert.equal(
        inspection.status,
        1,
        "The disposable container still exists.",
      );
    }
  } finally {
    removeMarketplaceFixture(repositoryRoot, fixture);
  }
}

function parseCachebusterHelper(argumentsToParse) {
  if (
    argumentsToParse.length !== 2
    || argumentsToParse[0] !== "--cachebuster-helper"
  ) {
    throw new Error(
      "Usage: node scripts/verify-plugin-lifecycle.mjs "
      + "--cachebuster-helper /absolute/path/to/update_plugin_cachebuster.py",
    );
  }
  const helper = resolve(argumentsToParse[1]);
  if (
    basename(helper) !== "update_plugin_cachebuster.py"
    || !existsSync(helper)
  ) {
    throw new Error("The cachebuster helper path is missing or unexpected.");
  }
  return helper;
}

function verifyContainerMount() {
  const inspection = JSON.parse(
    runDocker(["inspect", containerName, "--format", "{{json .Mounts}}"]).stdout,
  );
  assert.deepEqual(inspection, [
    {
      Type: "bind",
      Source: fixture.marketplaceRoot,
      Destination: "/marketplace",
      Mode: "",
      RW: false,
      Propagation: "rprivate",
    },
  ]);
}

function verifyNetworkUnavailable() {
  const program = [
    "fetch('https://developers.openai.com', {",
    "  signal: AbortSignal.timeout(2000),",
    "})",
    "  .then(() => process.exit(1))",
    "  .catch(() => process.exit(0));",
  ].join("\n");
  runContainer(["node", "-e", program]);
}

function pluginList({ version, installed, enabled }) {
  const plugin = {
    pluginId,
    name: pluginName,
    marketplaceName,
    version,
    installed,
    enabled,
    source: {
      source: "local",
      path: "/marketplace/plugins/sanjaya",
    },
    marketplaceSource: {
      sourceType: "local",
      source: "/marketplace",
    },
    installPolicy: "AVAILABLE",
    authPolicy: "ON_INSTALL",
  };
  return installed
    ? { installed: [plugin], available: [] }
    : { installed: [], available: [plugin] };
}

function installedPath(version) {
  return `/root/.codex/plugins/cache/${marketplaceName}/${pluginName}/${version}`;
}

function readPluginVersion(pluginRoot) {
  return JSON.parse(
    readFileSync(join(pluginRoot, ".codex-plugin", "plugin.json"), "utf8"),
  ).version;
}

function readContainerManifest(root) {
  const program = [
    "const crypto = require('node:crypto');",
    "const fs = require('node:fs');",
    "const path = require('node:path');",
    "const root = process.argv[1];",
    "const files = [];",
    "function visit(directory) {",
    "  if (!fs.existsSync(directory)) return;",
    "  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {",
    "    const fullPath = path.join(directory, entry.name);",
    "    if (entry.isDirectory()) visit(fullPath);",
    "    else if (entry.isFile()) {",
    "      const content = fs.readFileSync(fullPath);",
    "      files.push({",
    "        path: path.relative(root, fullPath).split(path.sep).join('/'),",
    "        bytes: content.length,",
    "        sha256: crypto.createHash('sha256').update(content).digest('hex'),",
    "      });",
    "    } else throw new Error(`unsupported entry ${fullPath}`);",
    "  }",
    "}",
    "visit(root);",
    "files.sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0));",
    "console.log(JSON.stringify(files));",
  ].join("\n");
  return JSON.parse(runContainer(["node", "-e", program, root]).stdout);
}

function containerPathExists(path) {
  const program = [
    "const fs = require('node:fs');",
    "console.log(JSON.stringify(fs.existsSync(process.argv[1])));",
  ].join("\n");
  return JSON.parse(runContainer(["node", "-e", program, path]).stdout);
}

function readContainerFile(path) {
  const program = [
    "const fs = require('node:fs');",
    "process.stdout.write(fs.readFileSync(process.argv[1], 'utf8'));",
  ].join("\n");
  return runContainer(["node", "-e", program, path]).stdout;
}

function runCodexJson(argumentsToPass) {
  const result = runContainer(["codex", ...argumentsToPass]);
  try {
    return JSON.parse(result.stdout);
  } catch (error) {
    throw new Error(
      `Codex returned invalid JSON for ${argumentsToPass.join(" ")}: `
      + result.stdout.trim(),
      { cause: error },
    );
  }
}

function runContainer(argumentsToPass) {
  return runDocker(["exec", containerName, ...argumentsToPass]);
}

function runDocker(argumentsToPass) {
  return runHost("docker", argumentsToPass);
}

function requireCommand(command, argumentsToPass) {
  const result = runHost(command, argumentsToPass);
  assert.ok(result.stdout.trim(), `${command} returned no version information.`);
}

function runHost(command, argumentsToPass, options = {}) {
  const acceptedStatuses = options.acceptedStatuses ?? [0];
  const result = spawnSync(command, argumentsToPass, {
    cwd: repositoryRoot,
    encoding: "utf8",
    maxBuffer: 10 * 1024 * 1024,
    windowsHide: true,
  });
  if (result.error) {
    throw result.error;
  }
  if (!acceptedStatuses.includes(result.status)) {
    throw new Error(
      `${command} ${argumentsToPass.join(" ")} failed with status `
      + `${result.status}: ${result.stderr.trim()}`,
    );
  }
  return result;
}
