using Sanjaya.Core.Contracts;
using Sanjaya.Core.Discovery;
using Sanjaya.Core.Repositories;
using Xunit;

namespace Sanjaya.Core.Tests;

public sealed class SearchTextServiceTests
{
    [Fact]
    public async Task ExactSearchIsCaseSensitiveAndDeterministicByDefault()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("b.txt", "MARKER on b\nmarker lower");
        repository.WriteFile("a.txt", "first MARKER\nsecond MARKER");
        SearchTextService service = new(RepositoryScope.Create(repository.Path));

        ToolResponse<SearchTextData> first = await service.SearchAsync("MARKER", true, null, CancellationToken.None);
        ToolResponse<SearchTextData> second = await service.SearchAsync("MARKER", true, null, CancellationToken.None);

        Assert.Equal(ContractValues.StatusOk, first.Status);
        Assert.Equal(["a.txt", "a.txt", "b.txt"], first.Data!.Matches.Select(match => match.Path));
        Assert.Equal([1, 2, 1], first.Data.Matches.Select(match => match.Line));
        Assert.Equal([7, 8, 1], first.Data.Matches.Select(match => match.Column));
        Assert.Equal(first.Data.Matches, second.Data!.Matches);
        Assert.All(first.Evidence, item => Assert.False(System.IO.Path.IsPathRooted(item.Path)));
    }

    [Fact]
    public async Task CaseInsensitiveSearchAndResultLimitAreExplicit()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("text.txt", "Needle needle NEEDLE");
        SearchTextService service = new(RepositoryScope.Create(repository.Path));

        ToolResponse<SearchTextData> response = await service.SearchAsync("needle", false, 2, CancellationToken.None);

        Assert.Equal(ContractValues.StatusPartial, response.Status);
        Assert.Equal(2, response.Data!.Matches.Count);
        Assert.True(response.Data.Truncated);
        Assert.Contains("search_limit_reached", response.Warnings);
    }

    [Fact]
    public async Task SearchesSourceInsidePackagesDirectories()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("packages/core/source.ts", "PACKAGES_MARKER");
        SearchTextService service = new(RepositoryScope.Create(repository.Path));

        ToolResponse<SearchTextData> response = await service.SearchAsync(
            "PACKAGES_MARKER",
            true,
            null,
            CancellationToken.None);

        Assert.Equal(ContractValues.StatusOk, response.Status);
        Assert.Equal("packages/core/source.ts", Assert.Single(response.Data!.Matches).Path);
    }

    [Fact]
    public async Task SearchSkipsBinaryOversizedGeneratedExcludedAndSymlinkTargets()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("visible.txt", "VISIBLE_MARKER");
        repository.WriteFile("node_modules/hidden.txt", "HIDDEN_MARKER");
        repository.WriteFile("generated.min.js", "HIDDEN_MARKER");
        File.WriteAllBytes(System.IO.Path.Combine(repository.Path, "binary.dat"), [0, 1, 2, 3]);
        File.WriteAllBytes(
            System.IO.Path.Combine(repository.Path, "oversized.txt"),
            new byte[DiscoveryLimits.MaximumFileBytes + 1]);
        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(
                System.IO.Path.Combine(repository.Path, "linked.txt"),
                System.IO.Path.Combine(repository.Path, "visible.txt"));
            Directory.CreateSymbolicLink(
                System.IO.Path.Combine(repository.Path, "linked-directory"),
                System.IO.Path.Combine(repository.Path, "node_modules"));
        }

        SearchTextService service = new(RepositoryScope.Create(repository.Path));
        ToolResponse<SearchTextData> response = await service.SearchAsync("MARKER", true, null, CancellationToken.None);

        TextMatch match = Assert.Single(response.Data!.Matches);
        Assert.Equal("visible.txt", match.Path);
        Assert.Equal(ContractValues.StatusPartial, response.Status);
        Assert.Contains(response.Warnings, warning => warning.StartsWith("binary_files_skipped:", StringComparison.Ordinal));
        Assert.Contains(response.Warnings, warning => warning.StartsWith("oversized_files_skipped:", StringComparison.Ordinal));
        Assert.Contains(response.Warnings, warning => warning.StartsWith("generated_files_skipped:", StringComparison.Ordinal));
        Assert.Contains(response.Warnings, warning => warning.StartsWith("excluded_directories_skipped:", StringComparison.Ordinal));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Contains(response.Warnings, warning => warning.StartsWith("symlink_files_skipped:", StringComparison.Ordinal));
            Assert.Contains(response.Warnings, warning => warning.StartsWith("symlink_directories_skipped:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task DeliberateExclusionsAndBinaryFilesDoNotMakeCompleteSearchPartial()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("visible.txt", "VISIBLE_MARKER");
        repository.WriteFile("node_modules/hidden.txt", "HIDDEN_MARKER");
        repository.WriteFile("generated.min.js", "HIDDEN_MARKER");
        File.WriteAllBytes(System.IO.Path.Combine(repository.Path, "binary.dat"), [0, 1, 2]);
        SearchTextService service = new(RepositoryScope.Create(repository.Path));

        ToolResponse<SearchTextData> response = await service.SearchAsync("VISIBLE_MARKER", true, null, CancellationToken.None);

        Assert.Equal(ContractValues.StatusOk, response.Status);
        Assert.Single(response.Data!.Matches);
        Assert.NotEmpty(response.Warnings);
    }

    [Fact]
    public async Task MissingRootInvalidArgumentsAndCancellationUseStableResults()
    {
        SearchTextService missing = new(RepositoryScope.Create(null));
        ToolResponse<SearchTextData> rootError = await missing.SearchAsync("text", true, null, CancellationToken.None);
        Assert.Equal(ContractValues.ErrorRepositoryRootRequired, rootError.Error!.Code);

        using TemporaryDirectory repository = new();
        repository.WriteFile("file.txt", "text");
        SearchTextService service = new(RepositoryScope.Create(repository.Path));
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await service.SearchAsync(string.Empty, true, null, CancellationToken.None)).Error!.Code);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        ToolResponse<SearchTextData> cancelled = await service.SearchAsync("text", true, null, cancellation.Token);
        Assert.Equal(ContractValues.StatusPartial, cancelled.Status);
        Assert.Equal(ContractValues.ErrorCancelled, cancelled.Error!.Code);
        Assert.Contains("search_cancelled", cancelled.Warnings);
    }

    [Theory]
    [InlineData("two\rlines")]
    [InlineData("two\nlines")]
    [InlineData("nul\0query")]
    public async Task RejectsQueriesThatCannotBeRepresentedAsLineEvidence(string query)
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("file.txt", "text");
        SearchTextService service = new(RepositoryScope.Create(repository.Path));

        ToolResponse<SearchTextData> response = await service.SearchAsync(query, true, null, CancellationToken.None);

        Assert.Equal(ContractValues.StatusError, response.Status);
        Assert.Equal(ContractValues.ErrorInvalidArgument, response.Error!.Code);
        Assert.Null(response.Data);
    }

    [Fact]
    public async Task SnippetsAndLongLinesRemainBounded()
    {
        using TemporaryDirectory repository = new();
        string line = string.Concat(new string('x', DiscoveryLimits.MaximumLineCharacters - 10), "NEEDLE", new string('y', 100));
        repository.WriteFile("long.txt", line);
        SearchTextService service = new(RepositoryScope.Create(repository.Path));

        ToolResponse<SearchTextData> response = await service.SearchAsync("NEEDLE", true, null, CancellationToken.None);

        TextMatch match = Assert.Single(response.Data!.Matches);
        Assert.True(match.Snippet.Length <= DiscoveryLimits.MaximumSnippetCharacters);
        Assert.Contains(response.Warnings, warning => warning.StartsWith("long_lines_truncated:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MaximumQueryRemainsWholeInSnippetNearLongLineEdge()
    {
        using TemporaryDirectory repository = new();
        string query = new('q', DiscoveryLimits.MaximumQueryCharacters);
        string line = string.Concat(new string('x', 500), query, new string('y', 500));
        repository.WriteFile("edge.txt", line);
        SearchTextService service = new(RepositoryScope.Create(repository.Path));

        ToolResponse<SearchTextData> response = await service.SearchAsync(query, true, null, CancellationToken.None);

        TextMatch match = Assert.Single(response.Data!.Matches);
        Assert.Contains(query, match.Snippet, StringComparison.Ordinal);
        Assert.True(match.Snippet.Length <= DiscoveryLimits.MaximumSnippetCharacters);
    }
}
