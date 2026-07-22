import { createHash } from "node:crypto";
import { lstatSync, readFileSync } from "node:fs";
import { join } from "node:path";

export const maximumPackageEntries = 64;
export const maximumCompressedBytes = 9 * 1024 * 1024;
export const maximumUnpackedBytes = 25 * 1024 * 1024;

export const approvedPackageFiles = Object.freeze([
  "LICENSE",
  "NOTICE",
  "README.md",
  "THIRD-PARTY-NOTICES.txt",
  "bin/sanjaya-mcp.js",
  "dist/dotnet/Microsoft.CodeAnalysis.CSharp.dll",
  "dist/dotnet/Microsoft.CodeAnalysis.dll",
  "dist/dotnet/Microsoft.Extensions.AI.Abstractions.dll",
  "dist/dotnet/Microsoft.Extensions.Caching.Abstractions.dll",
  "dist/dotnet/Microsoft.Extensions.Configuration.Abstractions.dll",
  "dist/dotnet/Microsoft.Extensions.Configuration.Binder.dll",
  "dist/dotnet/Microsoft.Extensions.Configuration.CommandLine.dll",
  "dist/dotnet/Microsoft.Extensions.Configuration.EnvironmentVariables.dll",
  "dist/dotnet/Microsoft.Extensions.Configuration.FileExtensions.dll",
  "dist/dotnet/Microsoft.Extensions.Configuration.Json.dll",
  "dist/dotnet/Microsoft.Extensions.Configuration.UserSecrets.dll",
  "dist/dotnet/Microsoft.Extensions.Configuration.dll",
  "dist/dotnet/Microsoft.Extensions.DependencyInjection.Abstractions.dll",
  "dist/dotnet/Microsoft.Extensions.DependencyInjection.dll",
  "dist/dotnet/Microsoft.Extensions.Diagnostics.Abstractions.dll",
  "dist/dotnet/Microsoft.Extensions.Diagnostics.dll",
  "dist/dotnet/Microsoft.Extensions.FileProviders.Abstractions.dll",
  "dist/dotnet/Microsoft.Extensions.FileProviders.Physical.dll",
  "dist/dotnet/Microsoft.Extensions.FileSystemGlobbing.dll",
  "dist/dotnet/Microsoft.Extensions.Hosting.Abstractions.dll",
  "dist/dotnet/Microsoft.Extensions.Hosting.dll",
  "dist/dotnet/Microsoft.Extensions.Logging.Abstractions.dll",
  "dist/dotnet/Microsoft.Extensions.Logging.Configuration.dll",
  "dist/dotnet/Microsoft.Extensions.Logging.Console.dll",
  "dist/dotnet/Microsoft.Extensions.Logging.Debug.dll",
  "dist/dotnet/Microsoft.Extensions.Logging.EventLog.dll",
  "dist/dotnet/Microsoft.Extensions.Logging.EventSource.dll",
  "dist/dotnet/Microsoft.Extensions.Logging.dll",
  "dist/dotnet/Microsoft.Extensions.Options.ConfigurationExtensions.dll",
  "dist/dotnet/Microsoft.Extensions.Options.dll",
  "dist/dotnet/Microsoft.Extensions.Primitives.dll",
  "dist/dotnet/ModelContextProtocol.Core.dll",
  "dist/dotnet/ModelContextProtocol.dll",
  "dist/dotnet/Sanjaya.Core.dll",
  "dist/dotnet/Sanjaya.Providers.CSharp.dll",
  "dist/dotnet/Sanjaya.Providers.TypeScript.dll",
  "dist/dotnet/Sanjaya.Server.deps.json",
  "dist/dotnet/Sanjaya.Server.dll",
  "dist/dotnet/Sanjaya.Server.runtimeconfig.json",
  "dist/dotnet/System.Diagnostics.DiagnosticSource.dll",
  "dist/dotnet/System.Diagnostics.EventLog.dll",
  "dist/dotnet/System.IO.Pipelines.dll",
  "dist/dotnet/System.Net.ServerSentEvents.dll",
  "dist/dotnet/System.Text.Encodings.Web.dll",
  "dist/dotnet/System.Text.Json.dll",
  "dist/dotnet/runtime/typescript/typescript-worker.mjs",
  "dist/dotnet/runtimes/browser/lib/net8.0/System.Text.Encodings.Web.dll",
  "dist/dotnet/runtimes/win/lib/net8.0/System.Diagnostics.EventLog.Messages.dll",
  "dist/dotnet/runtimes/win/lib/net8.0/System.Diagnostics.EventLog.dll",
  "dist/dotnet/third_party/typescript/PROVENANCE.json",
  "dist/dotnet/third_party/typescript/package/LICENSE.txt",
  "dist/dotnet/third_party/typescript/package/ThirdPartyNoticeText.txt",
  "dist/dotnet/third_party/typescript/package/lib/typescript.js",
  "dist/dotnet/third_party/typescript/package/package.json",
  "package.json",
].sort());

export const forbiddenLifecycleScripts = Object.freeze([
  "prepublish",
  "prepublishOnly",
  "prepack",
  "postpack",
  "publish",
  "postpublish",
  "preinstall",
  "install",
  "postinstall",
  "prepare",
]);

// One-way fingerprints reject reviewed private identifiers without publishing them.
const forbiddenIdentifierDigests = new Set([
  "6f0d702cc0fbd42c0de0cb0e43663ef441c1740c86a857706c842793d8d59c61",
]);

const forbiddenTextPatterns = Object.freeze([
  ["Unix user-home path", /\/(?:Users|home)\/[^/\0\r\n]{1,128}\//iu],
  ["Windows user-home path", /[A-Za-z]:\\Users\\[^\\\0\r\n]{1,128}\\/iu],
  ["source checkout path", /(?:\/|\\)(?:Projects|checkouts)(?:\/|\\)Sanjaya(?:\/|\\)/iu],
  ["local planning path", /\b(?:docs?|documentation)[/\\](?:local|private|planning)(?:[/\\]|$)/iu],
  ["local status filename", /\b[A-Za-z0-9_-]*status[A-Za-z0-9_.-]*\.md\b/iu],
  ["delegation metadata", /\b[A-Za-z_]*(?:thread|task)_id\b|<[A-Za-z_:.-]*delegation\b/iu],
  ["private key marker", /-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----/u],
  ["credential assignment", /\b[A-Z][A-Z0-9_]{2,}(?:TOKEN|SECRET|PASSWORD|API_KEY)\s*[:=]/u],
]);

export function verifyPackedFiles(repositoryRoot, paths) {
  const manifest = [];
  for (const path of paths) {
    const fullPath = join(repositoryRoot, ...path.split("/"));
    const metadata = lstatSync(fullPath);
    if (!metadata.isFile() || metadata.isSymbolicLink()) {
      throw new Error(`Package entry is not a regular file: ${path}`);
    }

    const content = readFileSync(fullPath);
    const searchableContent = content.toString("latin1");
    for (const [label, pattern] of forbiddenTextPatterns) {
      if (pattern.test(searchableContent)) {
        throw new Error(`Package entry contains ${label}: ${path}`);
      }
    }
    for (const match of searchableContent.matchAll(/[A-Za-z][A-Za-z0-9_-]{5,63}/gu)) {
      const normalized = match[0].replaceAll("_", "").replaceAll("-", "").toLowerCase();
      const digest = createHash("sha256").update(normalized).digest("hex");
      if (forbiddenIdentifierDigests.has(digest)) {
        throw new Error(`Package entry contains a private predecessor identifier: ${path}`);
      }
    }

    manifest.push({
      path,
      bytes: content.length,
      sha256: createHash("sha256").update(content).digest("hex"),
    });
  }

  return manifest;
}

export function assertEqual(actual, expected, message) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(message);
  }
}
