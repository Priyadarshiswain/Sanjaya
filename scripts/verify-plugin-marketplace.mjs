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
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const pluginRoot = join(repositoryRoot, "plugins", "sanjaya");
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

const temporaryRoot = mkdtempSync(
  join(canonicalTemporaryRoot(), "sanjaya-plugin-marketplace-"),
);
const marketplaceRoot = join(temporaryRoot, "marketplace");
const temporaryPluginRoot = join(marketplaceRoot, "plugins", "sanjaya");
const marketplacePath = join(
  marketplaceRoot,
  ".agents",
  "plugins",
  "marketplace.json",
);
const expectedPluginFiles = [
  ".codex-plugin/plugin.json",
  "skills/evidence-first-code-discovery/SKILL.md",
  "skills/evidence-first-code-discovery/agents/openai.yaml",
];
const expectedMarketplace = {
  name: "sanjaya-local-test",
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
};

try {
  assert.deepEqual(
    listFiles(pluginRoot),
    expectedPluginFiles,
    "The canonical local plugin drifted from the reviewed three-file layout.",
  );

  mkdirSync(dirname(marketplacePath), { recursive: true });
  mkdirSync(dirname(temporaryPluginRoot), { recursive: true });
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

  assert.deepEqual(
    listFiles(marketplaceRoot),
    [
      ".agents/plugins/marketplace.json",
      ...expectedPluginFiles.map((path) => `plugins/sanjaya/${path}`),
    ].sort(),
    "The disposable marketplace contains an unexpected file.",
  );
  const marketplaceContent = readFileSync(marketplacePath, "utf8");
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
    resolve(marketplaceRoot, expectedMarketplace.plugins[0].source.path),
    temporaryPluginRoot,
    "The marketplace source does not resolve to the copied plugin.",
  );
  assert.deepEqual(
    fileManifest(temporaryPluginRoot),
    fileManifest(pluginRoot),
    "The disposable marketplace altered the canonical plugin.",
  );

  const manifest = JSON.parse(
    readFileSync(
      join(temporaryPluginRoot, ".codex-plugin", "plugin.json"),
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
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}

assert.ok(
  !existsSync(temporaryRoot),
  "The disposable marketplace was not removed after verification.",
);
assert.ok(
  !existsSync(repositoryMarketplacePath),
  "Marketplace verification modified the repository installation surface.",
);

console.log(
  "Generated, verified, and removed an exact local-only Sanjaya marketplace "
  + "without installing or publishing it.",
);

function fileManifest(root) {
  return listFiles(root).map((path) => {
    const content = readFileSync(join(root, ...path.split("/")));
    return {
      path,
      bytes: content.length,
      sha256: createHash("sha256").update(content).digest("hex"),
    };
  });
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
