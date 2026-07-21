using System.Text.Json;
using Sanjaya.Core.Contracts;
using Xunit;

namespace Sanjaya.ContractTests;

public sealed class SerializationContractTests
{
    [Fact]
    public void ResponseEnvelopeUsesExactCamelCasePropertyNamesAndStringStatuses()
    {
        ToolResponse<HealthReportData> response = new(
            ContractValues.StatusOk,
            PublicToolNames.HealthCheck,
            "sanjaya-runtime",
            new HealthReportData(2, [new HealthCheckEntry("server", ContractValues.StatusOk, "Running.")]),
            [],
            []);

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(response));
        JsonElement root = document.RootElement;

        Assert.Equal("1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("health_check", root.GetProperty("capability").GetString());
        Assert.Equal("sanjaya-runtime", root.GetProperty("provider").GetString());
        Assert.Equal(2, root.GetProperty("data").GetProperty("registeredToolCount").GetInt32());
        Assert.Equal("ok", root.GetProperty("data").GetProperty("checks")[0].GetProperty("status").GetString());

        string[] expectedNames =
        [
            "status",
            "capability",
            "provider",
            "data",
            "evidence",
            "warnings",
            "error",
            "schemaVersion",
        ];

        Assert.Equal(expectedNames.Order(), root.EnumerateObject().Select(property => property.Name).Order());
    }

    [Fact]
    public void StableContractValuesDoNotDrift()
    {
        Assert.Equal("ok", ContractValues.StatusOk);
        Assert.Equal("partial", ContractValues.StatusPartial);
        Assert.Equal("error", ContractValues.StatusError);
        Assert.Equal("supported", ContractValues.AvailabilitySupported);
        Assert.Equal("unavailable", ContractValues.AvailabilityUnavailable);
        Assert.Equal("not_implemented", ContractValues.ReasonNotImplemented);
        Assert.Equal("repository_root_required", ContractValues.ReasonRepositoryRootRequired);
        Assert.Equal("not_git_repository", ContractValues.ReasonNotGitRepository);
        Assert.Equal("structural_provider_unavailable", ContractValues.ReasonStructuralProviderUnavailable);
    }

    [Fact]
    public void DiscoveryContractsUseBoundedEvidenceShapes()
    {
        ToolResponse<SearchTextData> response = new(
            ContractValues.StatusOk,
            PublicToolNames.SearchText,
            "generic-text",
            new SearchTextData(
                "needle",
                true,
                [new TextMatch("src/file.txt", 2, 4, "a needle")],
                1,
                8,
                false),
            [new EvidenceLocation("src/file.txt", 2, 2)],
            []);

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(response));
        JsonElement root = document.RootElement;
        Assert.Equal("1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("src/file.txt", root.GetProperty("data").GetProperty("matches")[0].GetProperty("path").GetString());
        Assert.Equal(2, root.GetProperty("evidence")[0].GetProperty("startLine").GetInt32());
    }

    [Fact]
    public void IndexedSearchContractUsesStableRankedEvidenceFields()
    {
        SearchCodeData data = new(
            "Run",
            false,
            "sha256:" + new string('a', 64),
            [new CodeSearchMatch(
                "sha256:" + new string('b', 64),
                "src/Sample.cs",
                "method",
                "Run",
                "Sample",
                4,
                7,
                1000,
                ["name"],
                "void Run()")],
            1,
            false);

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(data));
        JsonElement match = document.RootElement.GetProperty("matches")[0];

        Assert.Equal("src/Sample.cs", match.GetProperty("path").GetString());
        Assert.Equal(1000, match.GetProperty("score").GetInt32());
        Assert.Equal("name", match.GetProperty("matchedFields")[0].GetString());
        Assert.Equal("sha256:" + new string('a', 64), document.RootElement.GetProperty("indexFingerprint").GetString());
    }

    [Fact]
    public void RecentChangesContractOmitsAuthorEmailDiffAndAbsoluteRootFields()
    {
        RecentChangesData data = new(
            new GitHeadData(new string('a', 40), "main", false),
            new GitWorkingTreeData(true, false, [new GitWorkingTreeChange("src/file.cs", null, "unchanged", "modified")], false),
            [new GitCommitData(
                new string('b', 40),
                "2026-07-21T12:00:00+05:30",
                "bounded subject",
                false,
                [new GitPathChange("src/file.cs", null, "modified")],
                false)],
            false);

        string json = JsonSerializer.Serialize(data);

        Assert.Contains("\"revision\"", json, StringComparison.Ordinal);
        Assert.Contains("\"path\":\"src/file.cs\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("author", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diff", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("repositoryRoot", json, StringComparison.Ordinal);
    }

    [Fact]
    public void FileOutlineContractUsesLanguageNeutralBoundedItems()
    {
        FileOutlineData data = new(
            "src/Sample.cs",
            120,
            8,
            [],
            false,
            [new OutlineItem("class", "Sample", "public class Sample", "Demo", 2, 8)],
            false,
            0);

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(data));
        JsonElement root = document.RootElement;

        Assert.Equal(JsonValueKind.Array, root.GetProperty("items").ValueKind);
        Assert.Equal("class", root.GetProperty("items")[0].GetProperty("kind").GetString());
        Assert.Equal("Demo", root.GetProperty("items")[0].GetProperty("container").GetString());
        Assert.Equal(2, root.GetProperty("items")[0].GetProperty("startLine").GetInt32());
        Assert.Equal(0, root.GetProperty("syntaxDiagnosticCount").GetInt32());
    }

    [Fact]
    public void IndexResponseContractContainsOnlyCompactLifecycleMetadata()
    {
        IndexCodebaseData data = new(
            "1",
            "ready",
            ".sanjaya/index-v1.json",
            "sha256:abc",
            "missing",
            [new IndexedProviderSummary("csharp-roslyn-syntax", "1", ["csharp"], 2, 5)],
            2,
            3,
            5,
            100,
            0,
            0);

        string json = JsonSerializer.Serialize(data);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal(".sanjaya/index-v1.json", root.GetProperty("indexPath").GetString());
        Assert.Equal(5, root.GetProperty("chunksIndexed").GetInt32());
        Assert.Equal("1", root.GetProperty("providers")[0].GetProperty("contractVersion").GetString());
        Assert.DoesNotContain("content", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("absolute", json, StringComparison.OrdinalIgnoreCase);
    }
}
