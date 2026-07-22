using Sanjaya.Core.Contracts;
using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Discovery;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;
using Xunit;

namespace Sanjaya.Core.Tests;

public sealed class FileOutlineServiceTests
{
    [Fact]
    public async Task ReturnsOnlyBoundedGenericMetadataPreviewAndRelativeEvidence()
    {
        using TemporaryDirectory repository = new();
        string content = string.Join('\n', Enumerable.Range(1, 25).Select(line => $"line {line} {new string('x', 300)}"));
        repository.WriteFile("src/sample.cs", content);
        FileOutlineService service = new(RepositoryScope.Create(repository.Path));

        ToolResponse<FileOutlineData> response = await service.OutlineAsync("src/sample.cs", CancellationToken.None);

        Assert.Equal(ContractValues.StatusOk, response.Status);
        Assert.Equal("generic-text", response.Provider);
        Assert.Equal("src/sample.cs", response.Data!.Path);
        Assert.Equal(25, response.Data.LineCount);
        Assert.Equal(DiscoveryLimits.MaximumPreviewLines, response.Data.Preview.Count);
        Assert.True(response.Data.PreviewTruncated);
        Assert.All(response.Data.Preview, line => Assert.True(line.Text.Length <= DiscoveryLimits.MaximumPreviewCharactersPerLine));
        EvidenceLocation evidence = Assert.Single(response.Evidence);
        Assert.Equal("src/sample.cs", evidence.Path);
        Assert.False(System.IO.Path.IsPathRooted(evidence.Path));
    }

    [Fact]
    public async Task RejectsUnsafeAndUnreadableInputsWithStableErrors()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("regular.txt", "text");
        Directory.CreateDirectory(System.IO.Path.Combine(repository.Path, "folder"));
        File.WriteAllBytes(System.IO.Path.Combine(repository.Path, "binary.dat"), [0, 1]);
        File.WriteAllBytes(
            System.IO.Path.Combine(repository.Path, "large.txt"),
            new byte[DiscoveryLimits.MaximumFileBytes + 1]);
        FileOutlineService service = new(RepositoryScope.Create(repository.Path));

        Assert.Equal(ContractValues.ErrorInvalidPath, (await service.OutlineAsync("../outside", CancellationToken.None)).Error!.Code);
        Assert.Equal(ContractValues.ErrorInvalidPath, (await service.OutlineAsync("/outside", CancellationToken.None)).Error!.Code);
        Assert.Equal(ContractValues.ErrorNotAFile, (await service.OutlineAsync("folder", CancellationToken.None)).Error!.Code);
        Assert.Equal(ContractValues.ErrorBinaryFile, (await service.OutlineAsync("binary.dat", CancellationToken.None)).Error!.Code);
        Assert.Equal(ContractValues.ErrorFileTooLarge, (await service.OutlineAsync("large.txt", CancellationToken.None)).Error!.Code);
        Assert.Equal(ContractValues.ErrorFileNotFound, (await service.OutlineAsync("missing.txt", CancellationToken.None)).Error!.Code);
    }

    [Fact]
    public async Task RejectsSymlinksWithoutExposingTheirTargets()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory repository = new();
        using TemporaryDirectory outside = new();
        string secret = outside.WriteFile("secret.txt", "secret");
        File.CreateSymbolicLink(System.IO.Path.Combine(repository.Path, "link.txt"), secret);
        FileOutlineService service = new(RepositoryScope.Create(repository.Path));

        ToolResponse<FileOutlineData> response = await service.OutlineAsync("link.txt", CancellationToken.None);

        Assert.Equal(ContractValues.ErrorPathOutsideRepository, response.Error!.Code);
        Assert.DoesNotContain(outside.Path, response.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingRootAndCancellationRemainStructuredErrors()
    {
        FileOutlineService missing = new(RepositoryScope.Create(null));
        Assert.Equal(
            ContractValues.ErrorRepositoryRootRequired,
            (await missing.OutlineAsync("file.txt", CancellationToken.None)).Error!.Code);

        using TemporaryDirectory repository = new();
        repository.WriteFile("file.txt", "text");
        FileOutlineService service = new(RepositoryScope.Create(repository.Path));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(
            ContractValues.ErrorCancelled,
            (await service.OutlineAsync("file.txt", cancellation.Token)).Error!.Code);
    }

    [Theory]
    [InlineData(StructuralProviderFailure.Unavailable, ContractValues.ErrorStructuralProviderUnavailable)]
    [InlineData(StructuralProviderFailure.TimedOut, ContractValues.ErrorStructuralProviderTimeout)]
    [InlineData(StructuralProviderFailure.OutputLimit, ContractValues.ErrorStructuralProviderOutputLimit)]
    [InlineData(StructuralProviderFailure.InvalidOutput, ContractValues.ErrorStructuralProviderInvalidOutput)]
    public async Task StructuralProviderFailuresRemainSanitizedAndNeverDowngradeToText(
        StructuralProviderFailure failure,
        string expectedCode)
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("source.fake", "SECRET_SOURCE_CONTENT");
        FileOutlineService service = new(
            RepositoryScope.Create(repository.Path),
            [new FailingOutlineProvider(failure)]);

        ToolResponse<FileOutlineData> response = await service.OutlineAsync("source.fake", CancellationToken.None);

        Assert.Equal(ContractValues.StatusError, response.Status);
        Assert.Equal("failing-outline", response.Provider);
        Assert.Equal(expectedCode, response.Error!.Code);
        Assert.Null(response.Data);
        Assert.DoesNotContain("SECRET_SOURCE_CONTENT", response.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(repository.Path, response.Error.Message, StringComparison.Ordinal);
    }

    private sealed class FailingOutlineProvider(StructuralProviderFailure failure) : IFileOutlineProvider
    {
        public string Id => "failing-outline";

        public string ContractVersion => "1";

        public IReadOnlyCollection<string> Languages => ["fake"];

        public bool CanHandle(string relativePath) => relativePath.EndsWith(".fake", StringComparison.Ordinal);

        public IReadOnlyCollection<CapabilityDescriptor> GetCapabilities() =>
            [new(CapabilityKind.FileOutline, Id, "fake", CapabilityStatus.Supported)];

        public FileOutlineAnalysis AnalyzeOutline(
            string relativePath,
            string sourceText,
            CancellationToken cancellationToken) =>
            throw new StructuralProviderException(failure);
    }
}
