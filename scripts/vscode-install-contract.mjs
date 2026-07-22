const installScheme = "vscode:mcp/install?";
const packageName = "sanjaya-mcp";
const serverName = "sanjaya";
const workspaceFolderVariable = "${workspaceFolder}";
const stableVersionPattern = /^(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)$/u;

export function createVsCodeServerConfiguration(version) {
  validateReleasedVersion(version);

  return Object.freeze({
    name: serverName,
    type: "stdio",
    command: "npx",
    args: Object.freeze([
      "-y",
      `${packageName}@${version}`,
      "--root",
      workspaceFolderVariable,
    ]),
  });
}

export function createVsCodeInstallUrl(version) {
  const configuration = createVsCodeServerConfiguration(version);
  return `${installScheme}${encodeURIComponent(JSON.stringify(configuration))}`;
}

export function parseVsCodeInstallUrl(url) {
  if (typeof url !== "string" || !url.startsWith(installScheme)) {
    throw new Error("VS Code MCP install URL has an unsupported scheme.");
  }

  const encoded = url.slice(installScheme.length);
  if (encoded.length === 0) {
    throw new Error("VS Code MCP install URL is missing its configuration.");
  }

  try {
    return JSON.parse(decodeURIComponent(encoded));
  } catch {
    throw new Error("VS Code MCP install URL contains an invalid configuration.");
  }
}

function validateReleasedVersion(version) {
  if (typeof version !== "string"
      || !stableVersionPattern.test(version)
      || version === "0.0.0") {
    throw new Error("VS Code installation requires an exact stable published version.");
  }
}
