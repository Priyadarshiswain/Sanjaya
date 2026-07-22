import { createRequire } from "node:module";
import { isAbsolute } from "node:path";
import { TextDecoder } from "node:util";
import { fileURLToPath } from "node:url";

const protocolVersion = "1";
const maximumRequestBytes = 8 * 1024 * 1024;
const maximumSourceBytes = 1024 * 1024;
const maximumItems = 500;
const maximumDisplayCharacters = 240;
const maximumChunkCharacters = 64 * 1024;
const decoder = new TextDecoder("utf-8", { fatal: true });
const require = createRequire(import.meta.url);
const compilerPath = fileURLToPath(
  new URL("../../third_party/typescript/package/lib/typescript.js", import.meta.url),
);
const ts = require(compilerPath);

if (ts.version !== "6.0.3") {
  failProcess();
}

let pending = Buffer.alloc(0);
process.stdin.on("data", (chunk) => {
  pending = Buffer.concat([pending, chunk]);
  if (pending.length > maximumRequestBytes && !pending.includes(0x0a)) {
    failProcess();
    return;
  }

  drainRequests();
});
process.stdin.on("end", () => {
  if (pending.length !== 0) {
    failProcess();
  }
});
process.stdin.on("error", failProcess);

function drainRequests() {
  while (true) {
    const newline = pending.indexOf(0x0a);
    if (newline < 0) {
      return;
    }

    const line = pending.subarray(0, newline);
    pending = pending.subarray(newline + 1);
    if (line.length === 0 || line.length > maximumRequestBytes) {
      failProcess();
      return;
    }

    let request;
    try {
      request = JSON.parse(decoder.decode(line));
      const response = handleRequest(request);
      process.stdout.write(`${JSON.stringify(response)}\n`);
    } catch {
      const requestId = isBoundedString(request?.requestId, 128)
        ? request.requestId
        : "invalid";
      process.stdout.write(`${JSON.stringify({
        protocolVersion,
        requestId,
        status: "error",
      })}\n`);
    }
  }
}

function handleRequest(request) {
  if (!isPlainObject(request)
      || request.protocolVersion !== protocolVersion
      || !isBoundedString(request.requestId, 128)) {
    throw new Error("invalid request");
  }

  if (request.operation === "handshake") {
    requireExactKeys(request, ["operation", "protocolVersion", "requestId"]);
    return {
      protocolVersion,
      requestId: request.requestId,
      status: "ok",
      nodeVersion: process.versions.node,
      typescriptVersion: ts.version,
    };
  }

  if (request.operation !== "analyze") {
    throw new Error("unknown operation");
  }

  requireExactKeys(request, [
    "language",
    "operation",
    "protocolVersion",
    "relativePath",
    "requestId",
    "sourceText",
  ]);
  if (!isBoundedString(request.relativePath, 4096)
      || isAbsolute(request.relativePath)
      || request.relativePath.includes("\\")
      || request.relativePath.split("/").includes("..")
      || typeof request.sourceText !== "string"
      || Buffer.byteLength(request.sourceText, "utf8") > maximumSourceBytes) {
    throw new Error("invalid source request");
  }

  const scriptKind = selectScriptKind(request.language, request.relativePath);
  const sourceFile = ts.createSourceFile(
    request.relativePath,
    request.sourceText,
    ts.ScriptTarget.Latest,
    true,
    scriptKind,
  );
  const declarations = collectDeclarations(sourceFile, request.sourceText);
  const truncated = declarations.length > maximumItems;
  const selected = declarations.slice(0, maximumItems);

  return {
    protocolVersion,
    requestId: request.requestId,
    status: "ok",
    items: selected.map((item) => ({
      kind: item.kind,
      name: item.name,
      display: item.display,
      container: item.container,
      startLine: item.startLine,
      endLine: item.endLine,
    })),
    itemsTruncated: truncated,
    chunks: selected.map((item) => ({
      kind: item.kind,
      name: item.name,
      container: item.container,
      startLine: item.startLine,
      endLine: item.endLine,
      content: item.content.length <= maximumChunkCharacters
        ? item.content
        : item.content.slice(0, maximumChunkCharacters),
      contentTruncated: item.content.length > maximumChunkCharacters,
    })),
    chunksTruncated: truncated,
    syntaxDiagnosticCount: sourceFile.parseDiagnostics?.length ?? 0,
  };
}

function selectScriptKind(language, relativePath) {
  const lower = relativePath.toLowerCase();
  if (language === "typescript") {
    if (lower.endsWith(".tsx")) {
      return ts.ScriptKind.TSX;
    }
    if ([".ts", ".mts", ".cts"].some((extension) => lower.endsWith(extension))) {
      return ts.ScriptKind.TS;
    }
  }
  if (language === "javascript") {
    if (lower.endsWith(".jsx")) {
      return ts.ScriptKind.JSX;
    }
    if ([".js", ".mjs", ".cjs"].some((extension) => lower.endsWith(extension))) {
      return ts.ScriptKind.JS;
    }
  }

  throw new Error("unsupported language or extension");
}

function collectDeclarations(sourceFile, sourceText) {
  const declarations = [];
  visit(sourceFile);
  return declarations.sort((left, right) => left.startOffset - right.startOffset);

  function visit(node) {
    const description = describe(node, sourceFile);
    if (description) {
      const startOffset = node.getStart(sourceFile, false);
      const endOffset = node.getEnd();
      const start = sourceFile.getLineAndCharacterOfPosition(startOffset);
      const end = sourceFile.getLineAndCharacterOfPosition(Math.max(startOffset, endOffset - 1));
      const content = sourceText.slice(startOffset, endOffset).trim();
      declarations.push({
        kind: description.kind,
        name: bound(description.name),
        display: bound(collapseWhitespace(content)),
        container: getContainer(node, sourceFile),
        startLine: start.line + 1,
        endLine: end.line + 1,
        content,
        startOffset,
      });
    }

    ts.forEachChild(node, visit);
  }
}

function describe(node, sourceFile) {
  if (ts.isModuleDeclaration(node)) {
    const name = stableName(node.name, sourceFile);
    return name ? { kind: ts.isStringLiteral(node.name) ? "module" : "namespace", name } : null;
  }
  if (ts.isClassDeclaration(node)) {
    const name = stableName(node.name, sourceFile) ?? defaultExportName(node);
    return name ? { kind: "class", name } : null;
  }
  if (ts.isInterfaceDeclaration(node)) {
    return { kind: "interface", name: node.name.text };
  }
  if (ts.isTypeAliasDeclaration(node)) {
    return { kind: "type_alias", name: node.name.text };
  }
  if (ts.isEnumDeclaration(node)) {
    return { kind: "enum", name: node.name.text };
  }
  if (ts.isFunctionDeclaration(node)) {
    const name = stableName(node.name, sourceFile) ?? defaultExportName(node);
    return name ? { kind: "function", name } : null;
  }
  if (ts.isMethodDeclaration(node) || ts.isMethodSignature(node)) {
    const name = stableName(node.name, sourceFile);
    return name ? { kind: "method", name } : null;
  }
  if (ts.isConstructorDeclaration(node)) {
    return { kind: "constructor", name: "constructor" };
  }
  if (ts.isGetAccessorDeclaration(node) || ts.isSetAccessorDeclaration(node)) {
    const name = stableName(node.name, sourceFile);
    return name ? { kind: ts.isGetAccessorDeclaration(node) ? "getter" : "setter", name } : null;
  }
  if (ts.isPropertyDeclaration(node) || ts.isPropertySignature(node)) {
    const name = stableName(node.name, sourceFile);
    return name ? { kind: "property", name } : null;
  }
  if (ts.isVariableDeclaration(node) && isSourceOrModuleVariable(node)) {
    const name = stableName(node.name, sourceFile);
    if (!name) {
      return null;
    }
    if (node.initializer && (ts.isArrowFunction(node.initializer) || ts.isFunctionExpression(node.initializer))) {
      return { kind: "function", name };
    }
    if (node.initializer && ts.isClassExpression(node.initializer)) {
      return { kind: "class", name };
    }
    return { kind: "variable", name };
  }
  return null;
}

function stableName(name, sourceFile) {
  if (!name) {
    return null;
  }
  if (ts.isIdentifier(name) || ts.isPrivateIdentifier(name)
      || ts.isStringLiteral(name) || ts.isNumericLiteral(name)) {
    return name.text;
  }
  return null;
}

function defaultExportName(node) {
  return node.modifiers?.some((modifier) => modifier.kind === ts.SyntaxKind.DefaultKeyword)
    ? "default"
    : null;
}

function isSourceOrModuleVariable(node) {
  const statement = node.parent?.parent;
  const owner = statement?.parent;
  return ts.isVariableStatement(statement)
    && (ts.isSourceFile(owner) || ts.isModuleBlock(owner));
}

function getContainer(node, sourceFile) {
  const parts = [];
  for (let ancestor = node.parent; ancestor; ancestor = ancestor.parent) {
    if (ts.isModuleDeclaration(ancestor)
        || ts.isClassDeclaration(ancestor)
        || ts.isClassExpression(ancestor)
        || ts.isInterfaceDeclaration(ancestor)
        || ts.isEnumDeclaration(ancestor)) {
      const name = stableName(ancestor.name, sourceFile)
        ?? variableInitializerName(ancestor, sourceFile)
        ?? defaultExportName(ancestor);
      if (name) {
        parts.push(name);
      }
    }
  }
  return parts.length === 0 ? null : parts.reverse().join(".");
}

function variableInitializerName(node, sourceFile) {
  return (ts.isClassExpression(node) || ts.isFunctionExpression(node))
      && ts.isVariableDeclaration(node.parent)
      && node.parent.initializer === node
    ? stableName(node.parent.name, sourceFile)
    : null;
}

function requireExactKeys(value, expected) {
  const actual = Object.keys(value).sort();
  if (JSON.stringify(actual) !== JSON.stringify([...expected].sort())) {
    throw new Error("unexpected request fields");
  }
}

function isPlainObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function isBoundedString(value, maximum) {
  return typeof value === "string"
    && value.length > 0
    && value.length <= maximum
    && !value.includes("\0")
    && !value.includes("\r")
    && !value.includes("\n");
}

function collapseWhitespace(value) {
  return value.replace(/\s+/gu, " ").trim();
}

function bound(value) {
  return value.length <= maximumDisplayCharacters
    ? value
    : value.slice(0, maximumDisplayCharacters);
}

function failProcess() {
  process.stderr.write("Sanjaya TypeScript worker failed.\n");
  process.exit(1);
}
