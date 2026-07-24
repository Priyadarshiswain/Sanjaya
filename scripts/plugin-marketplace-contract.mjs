import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import {
  cpSync,
  existsSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join, relative, resolve, sep } from "node:path";

export const marketplaceName = "sanjaya-local-test";
export const expectedPluginFiles = Object.freeze([
  ".codex-plugin/plugin.json",
  "skills/evidence-first-code-discovery/SKILL.md",
  "skills/evidence-first-code-discovery/agents/openai.yaml",
]);
export const expectedMarketplace = Object.freeze({
  name: marketplaceName,
  interface: {
    displayName: "Sanjaya Local Test",
  },
  plugins: [
    {
      name: "sanjaya",
      source: {
        source: "local",
        path: "./plugins/sanjaya",
      },
      policy: {
        installation: "AVAILABLE",
        authentication: "ON_INSTALL",
      },
      category: "Developer Tools",
    },
  ],
});

export function createMarketplaceFixture(repositoryRoot) {
  assertNoRepositoryMarketplace(repositoryRoot);

  const temporaryRoot = mkdtempSync(
    join(canonicalTemporaryRoot(), "sanjaya-plugin-marketplace-"),
  );
  const marketplaceRoot = join(temporaryRoot, "marketplace");
  const pluginRoot = join(repositoryRoot, "plugins", "sanjaya");
  const temporaryPluginRoot = join(marketplaceRoot, "plugins", "sanjaya");
  const marketplacePath = join(
    marketplaceRoot,
    ".agents",
    "plugins",
    "marketplace.json",
  );
  const fixture = {
    temporaryRoot,
    marketplaceRoot,
    temporaryPluginRoot,
    marketplacePath,
  };

  try {
    assert.deepEqual(
      listFiles(pluginRoot),
      expectedPluginFiles,
      "The canonical local plugin drifted from the reviewed three-file layout.",
    );

    mkdirSync(join(marketplaceRoot, ".agents", "plugins"), { recursive: true });
    mkdirSync(join(marketplaceRoot, "plugins"), { recursive: true });
    cpSync(pluginRoot, temporaryPluginRoot, {
      recursive: true,
      errorOnExist: true,
      force: false,
    });
    writeFileSync(
      marketplacePath,
      `${JSON.stringify(expectedMarketplace, null, 2)}\n`,
      { flag: "wx" },
    );

    verifyMarketplaceFixture(repositoryRoot, fixture);
    return fixture;
  } catch (error) {
    rmSync(temporaryRoot, { recursive: true, force: true });
    throw error;
  }
}

export function verifyMarketplaceFixture(repositoryRoot, fixture) {
  const pluginRoot = join(repositoryRoot, "plugins", "sanjaya");
  assert.deepEqual(
    listFiles(fixture.marketplaceRoot),
    [
      ".agents/plugins/marketplace.json",
      ...expectedPluginFiles.map((path) => `plugins/sanjaya/${path}`),
    ].sort(),
    "The disposable marketplace contains an unexpected file.",
  );

  const marketplaceContent = readFileSync(fixture.marketplacePath, "utf8");
  assert.ok(
    Buffer.byteLength(marketplaceContent) < 4096,
    "The disposable marketplace metadata exceeds the review ceiling.",
  );
  assert.deepEqual(
    JSON.parse(marketplaceContent),
    expectedMarketplace,
    "The disposable marketplace metadata drifted from the reviewed identity.",
  );
  assert.equal(
    resolve(
      fixture.marketplaceRoot,
      expectedMarketplace.plugins[0].source.path,
    ),
    fixture.temporaryPluginRoot,
    "The marketplace source does not resolve to the copied plugin.",
  );
  assert.deepEqual(
    fileManifest(fixture.temporaryPluginRoot),
    fileManifest(pluginRoot),
    "The disposable marketplace altered the canonical plugin.",
  );

  const manifest = JSON.parse(
    readFileSync(
      join(fixture.temporaryPluginRoot, ".codex-plugin", "plugin.json"),
      "utf8",
    ),
  );
  assert.equal(manifest.name, expectedMarketplace.plugins[0].name);
  assert.equal(
    manifest.interface.category,
    expectedMarketplace.plugins[0].category,
  );
  for (const forbiddenField of ["apps", "mcpServers", "hooks"]) {
    assert.ok(
      !Object.hasOwn(manifest, forbiddenField),
      `The skills-only test plugin must not declare ${forbiddenField}.`,
    );
  }
}

export function removeMarketplaceFixture(repositoryRoot, fixture) {
  rmSync(fixture.temporaryRoot, { recursive: true, force: true });
  assert.ok(
    !existsSync(fixture.temporaryRoot),
    "The disposable marketplace was not removed after verification.",
  );
  assertNoRepositoryMarketplace(repositoryRoot);
}

export function fileManifest(root) {
  return listFiles(root).map((path) => {
    const content = readFileSync(join(root, ...path.split("/")));
    return {
      path,
      bytes: content.length,
      sha256: createHash("sha256").update(content).digest("hex"),
    };
  });
}

function assertNoRepositoryMarketplace(repositoryRoot) {
  const repositoryMarketplacePath = join(
    repositoryRoot,
    ".agents",
    "plugins",
    "marketplace.json",
  );
  assert.ok(
    !existsSync(repositoryMarketplacePath),
    "The repository must not contain a persistent marketplace entry.",
  );
}

function listFiles(root) {
  const files = [];
  const pending = [root];
  while (pending.length > 0) {
    const current = pending.pop();
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const path = join(current, entry.name);
      const metadata = lstatSync(path);
      if (metadata.isSymbolicLink()) {
        throw new Error(`Marketplace content must not contain symlink ${path}.`);
      }
      if (entry.isDirectory()) {
        pending.push(path);
      } else if (entry.isFile()) {
        files.push(relative(root, path).split(sep).join("/"));
      } else {
        throw new Error(`Marketplace content contains unsupported entry ${path}.`);
      }
    }
  }
  return files.sort();
}

function canonicalTemporaryRoot() {
  const root = resolve(tmpdir());
  return process.platform === "darwin" && root.startsWith("/var/")
    ? `/private${root}`
    : root;
}
