using System.Text.Json;
using ModelContextProtocol.Protocol;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Repositories;
using Sanjaya.Providers.CSharp;
using Sanjaya.Providers.TypeScript;
using Sanjaya.Server.Tools;
using Xunit;

namespace Sanjaya.Server.Tests;

public sealed class ProtocolFoundationToolTests
{
    [Fact]
    public void CapabilitiesReportsImplementedToolsAsReadyForValidRoot()
    {
        CapabilitiesTool tool = new(RepositoryScope.Create(FindRepositoryRoot()), [new CSharpSyntaxProvider()]);
        ToolResponse<CapabilityReportData> response = tool.CreateResponse();

        Assert.Equal(ContractValues.StatusOk, response.Status);
        Assert.True(response.Data!.RepositoryReady);
        Assert.Equal(PublicToolNames.All, response.Data.Tools.Select(item => item.Name));
        Assert.Equal(PublicToolNames.All.Count, response.Data.Tools.Select(item => item.Name).Distinct().Count());
        Assert.Equal(
            PublicToolNames.ProtocolFoundation
                .Concat(PublicToolNames.ImmediateDiscovery)
                .Concat(PublicToolNames.LocalGitEvidence)
                .Concat(PublicToolNames.StructuralIndex),
            response.Data.Tools.Where(item => item.Status == ContractValues.AvailabilitySupported).Select(item => item.Name));
        Assert.Equal(
            ContractValues.ReasonIndexMissing,
            response.Data.Tools.Single(item => item.Name == PublicToolNames.SearchCode).Reason);
        Assert.Equal(
            ContractValues.ReasonIndexMissing,
            response.Data.Tools.Single(item => item.Name == PublicToolNames.FindDefinition).Reason);
        Assert.Equal(
            ContractValues.ReasonIndexMissing,
            response.Data.Tools.Single(item => item.Name == PublicToolNames.FindReferences).Reason);
        Assert.Equal(
            ContractValues.ReasonIndexMissing,
            response.Data.Tools.Single(item => item.Name == PublicToolNames.GetSource).Reason);
        Assert.All(
            response.Data.Tools.Where(item => item.Status == ContractValues.AvailabilityUnavailable
                && item.Name != PublicToolNames.SearchCode
                && item.Name != PublicToolNames.FindDefinition
                && item.Name != PublicToolNames.FindReferences
                && item.Name != PublicToolNames.GetSource),
            item => Assert.Equal(ContractValues.ReasonNotImplemented, item.Reason));
        ProviderAvailability csharp = response.Data.Providers.Single(
            item => item.Id == CSharpSyntaxProvider.ProviderId);
        Assert.Equal(ContractValues.AvailabilitySupported, csharp.Status);
        Assert.Equal(
            ["file_outline", "structural_chunking", "definitions", "references", "source_retrieval"],
            csharp.Capabilities
                .Where(item => item.Status == ContractValues.AvailabilitySupported)
                .Select(item => item.Name));
    }

    [Fact]
    public void CapabilitiesDistinguishesImplementationFromMissingRootReadiness()
    {
        CapabilitiesTool tool = new(RepositoryScope.Create(null), [new CSharpSyntaxProvider()]);
        CapabilityReportData data = tool.CreateResponse().Data!;

        Assert.False(data.RepositoryReady);
        Assert.Equal(
            ContractValues.ReasonRepositoryRootRequired,
            data.Tools.Single(item => item.Name == PublicToolNames.SearchText).Reason);
        Assert.Equal(
            ContractValues.ReasonRepositoryRootRequired,
            data.Tools.Single(item => item.Name == PublicToolNames.FileOutline).Reason);
        Assert.Equal(
            ContractValues.ReasonRepositoryRootRequired,
            data.Tools.Single(item => item.Name == PublicToolNames.RecentChanges).Reason);
        Assert.Equal(
            ContractValues.ReasonRepositoryRootRequired,
            data.Tools.Single(item => item.Name == PublicToolNames.IndexCodebase).Reason);
        Assert.Equal(
            ContractValues.ReasonRepositoryRootRequired,
            data.Tools.Single(item => item.Name == PublicToolNames.SearchCode).Reason);
        Assert.Equal(
            ContractValues.ReasonRepositoryRootRequired,
            data.Tools.Single(item => item.Name == PublicToolNames.FindDefinition).Reason);
        Assert.Equal(
            ContractValues.ReasonRepositoryRootRequired,
            data.Tools.Single(item => item.Name == PublicToolNames.FindReferences).Reason);
        Assert.Equal(
            ContractValues.ReasonRepositoryRootRequired,
            data.Tools.Single(item => item.Name == PublicToolNames.GetSource).Reason);
        Assert.Equal(
            ContractValues.ReasonRepositoryRootRequired,
            data.Providers.Single(item => item.Id == "generic").Reason);
        Assert.Equal(
            ContractValues.ReasonRepositoryRootRequired,
            data.Providers.Single(item => item.Id == CSharpSyntaxProvider.ProviderId).Reason);
        Assert.Equal(
            ContractValues.ReasonNotImplemented,
            data.Providers.Single(item => item.Id == TypeScriptSyntaxProvider.TypeScriptProviderId).Reason);
        Assert.Equal(
            ContractValues.ReasonNotImplemented,
            data.Providers.Single(item => item.Id == TypeScriptSyntaxProvider.JavaScriptProviderId).Reason);
    }

    [Fact]
    public void CapabilitiesReportsMissingTypeScriptRuntimeWithoutClaimingGenericFallback()
    {
        CapabilitiesTool tool = new(
            RepositoryScope.Create(FindRepositoryRoot()),
            [new CSharpSyntaxProvider(), new UnavailableTypeScriptProvider("typescript"), new UnavailableTypeScriptProvider("javascript")]);

        CapabilityReportData data = tool.CreateResponse().Data!;
        foreach (string providerId in new[]
                 {
                     TypeScriptSyntaxProvider.TypeScriptProviderId,
                     TypeScriptSyntaxProvider.JavaScriptProviderId,
                 })
        {
            ProviderAvailability provider = data.Providers.Single(item => item.Id == providerId);
            Assert.Equal(ContractValues.AvailabilityUnavailable, provider.Status);
            Assert.Equal(ContractValues.ReasonRuntimeUnavailable, provider.Reason);
            Assert.All(
                provider.Capabilities.Where(item => item.Name is "file_outline" or "structural_chunking"),
                item => Assert.Equal(ContractValues.ReasonRuntimeUnavailable, item.Reason));
        }
    }

    [Fact]
    public void IndexCapabilityRequiresAnActiveStructuralProvider()
    {
        CapabilitiesTool tool = new(RepositoryScope.Create(FindRepositoryRoot()), []);

        ToolAvailability index = tool.CreateResponse().Data!.Tools.Single(
            item => item.Name == PublicToolNames.IndexCodebase);
        ToolAvailability definition = tool.CreateResponse().Data!.Tools.Single(
            item => item.Name == PublicToolNames.FindDefinition);
        ToolAvailability source = tool.CreateResponse().Data!.Tools.Single(
            item => item.Name == PublicToolNames.GetSource);

        Assert.Equal(ContractValues.AvailabilityUnavailable, index.Status);
        Assert.Equal(ContractValues.ReasonStructuralProviderUnavailable, index.Reason);
        Assert.Equal(ContractValues.AvailabilityUnavailable, definition.Status);
        Assert.Equal(ContractValues.ReasonDefinitionProviderUnavailable, definition.Reason);
        Assert.Equal(ContractValues.AvailabilityUnavailable, source.Status);
        Assert.Equal(ContractValues.ReasonSourceProviderUnavailable, source.Reason);
    }

    [Fact]
    public void HealthCheckReportsOnlyImplementedRuntimeChecks()
    {
        HealthReportData data = HealthCheckTool.CreateResponse().Data!;

        Assert.Equal(10, data.RegisteredToolCount);
        Assert.Equal(["server", "transport", "stdout", "network"], data.Checks.Select(check => check.Name));
        Assert.All(data.Checks, check => Assert.Equal(ContractValues.StatusOk, check.Status));
    }

    [Fact]
    public void ToolsReturnStructuredContentAndCompactTextFallback()
    {
        CapabilitiesTool tool = new(RepositoryScope.Create(FindRepositoryRoot()), [new CSharpSyntaxProvider()]);
        CallToolResult result = tool.GetCapabilities();

        Assert.False(result.IsError);
        TextContentBlock text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Contains("6 of 10", text.Text, StringComparison.Ordinal);

        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("1", structured.GetProperty("schemaVersion").GetString());
        Assert.Equal("ok", structured.GetProperty("status").GetString());
        Assert.Equal(10, structured.GetProperty("data").GetProperty("tools").GetArrayLength());
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
