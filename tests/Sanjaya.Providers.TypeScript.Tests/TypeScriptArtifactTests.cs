using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Sanjaya.Providers.TypeScript.Tests;

public sealed class TypeScriptArtifactTests
{
    private static readonly string[] ApprovedFiles =
    [
        "package/LICENSE.txt",
        "package/ThirdPartyNoticeText.txt",
        "package/lib/typescript.js",
        "package/package.json",
    ];

    [Fact]
    public void ProvenancePinsExactAllowlistedArtifactFiles()
    {
        string componentRoot = Path.Combine(FindRepositoryRoot(), "third_party", "typescript");
        using JsonDocument provenance = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(componentRoot, "PROVENANCE.json")));
        JsonElement root = provenance.RootElement;

        Assert.Equal("typescript", root.GetProperty("package").GetString());
        Assert.Equal("6.0.3", root.GetProperty("version").GetString());
        Assert.Equal("Apache-2.0", root.GetProperty("license").GetString());
        Assert.Matches("^[a-f0-9]{64}$", root.GetProperty("source").GetProperty("tarballSha256").GetString());

        JsonElement[] files = root.GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(
            ApprovedFiles,
            files.Select(file => file.GetProperty("path").GetString()).Order(StringComparer.Ordinal));
        Assert.Equal(
            ApprovedFiles,
            Directory.EnumerateFiles(Path.Combine(componentRoot, "package"), "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(componentRoot, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal));

        foreach (JsonElement file in files)
        {
            string relativePath = file.GetProperty("path").GetString()!;
            string path = Path.Combine(componentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            byte[] content = File.ReadAllBytes(path);
            Assert.Equal(file.GetProperty("bytes").GetInt64(), content.LongLength);
            Assert.Equal(
                file.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
        }
    }

    [Fact]
    public void RootNoticesIdentifyBundledTypeScriptAndCompleteNotices()
    {
        string repositoryRoot = FindRepositoryRoot();
        string notice = File.ReadAllText(Path.Combine(repositoryRoot, "NOTICE"));
        string thirdParty = File.ReadAllText(Path.Combine(repositoryRoot, "THIRD-PARTY-NOTICES.txt"));

        Assert.Contains("Microsoft TypeScript 6.0.3", notice, StringComparison.Ordinal);
        Assert.Contains("Apache-2.0", thirdParty, StringComparison.Ordinal);
        Assert.Contains("ThirdPartyNoticeText.txt", thirdParty, StringComparison.Ordinal);
        Assert.Contains("LICENSE.txt", thirdParty, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sanjaya.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
