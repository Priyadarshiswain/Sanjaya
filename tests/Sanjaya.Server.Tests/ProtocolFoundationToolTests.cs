using System.Text.Json;
using ModelContextProtocol.Protocol;
using Sanjaya.Core.Contracts;
using Sanjaya.Server.Tools;
using Xunit;

namespace Sanjaya.Server.Tests;

public sealed class ProtocolFoundationToolTests
{
    [Fact]
    public void CapabilitiesReportsEveryApprovedToolExactlyOnce()
    {
        ToolResponse<CapabilityReportData> response = CapabilitiesTool.CreateResponse();

        Assert.Equal(ContractValues.StatusOk, response.Status);
        Assert.Equal(PublicToolNames.All, response.Data!.Tools.Select(tool => tool.Name));
        Assert.Equal(PublicToolNames.All.Count, response.Data.Tools.Select(tool => tool.Name).Distinct().Count());

        ToolAvailability[] supported = response.Data.Tools
            .Where(tool => tool.Status == ContractValues.AvailabilitySupported)
            .ToArray();
        Assert.Equal(PublicToolNames.ProtocolFoundation, supported.Select(tool => tool.Name));

        ToolAvailability[] unavailable = response.Data.Tools
            .Where(tool => tool.Status == ContractValues.AvailabilityUnavailable)
            .ToArray();
        Assert.Equal(PublicToolNames.All.Count - PublicToolNames.ProtocolFoundation.Count, unavailable.Length);
        Assert.All(unavailable, tool => Assert.Equal(ContractValues.ReasonNotImplemented, tool.Reason));
    }

    [Fact]
    public void CapabilitiesReportsDeferredProvidersAsUnavailable()
    {
        CapabilityReportData data = CapabilitiesTool.CreateResponse().Data!;

        Assert.Equal(["csharp", "typescript-javascript", "generic"], data.Providers.Select(provider => provider.Id));
        Assert.All(data.Providers, provider =>
        {
            Assert.Equal(ContractValues.AvailabilityUnavailable, provider.Status);
            Assert.Equal(ContractValues.ReasonNotImplemented, provider.Reason);
        });
        Assert.False(data.DefaultNetworkAccess);
        Assert.Equal("stdio", data.Transport);
    }

    [Fact]
    public void HealthCheckReportsOnlyImplementedRuntimeChecks()
    {
        HealthReportData data = HealthCheckTool.CreateResponse().Data!;

        Assert.Equal(2, data.RegisteredToolCount);
        Assert.Equal(["server", "transport", "stdout", "network"], data.Checks.Select(check => check.Name));
        Assert.All(data.Checks, check => Assert.Equal(ContractValues.StatusOk, check.Status));
    }

    [Fact]
    public void ToolsReturnStructuredContentAndCompactTextFallback()
    {
        CallToolResult result = CapabilitiesTool.GetCapabilities();

        Assert.False(result.IsError);
        TextContentBlock text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        Assert.Contains("2 of 10", text.Text, StringComparison.Ordinal);

        JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal("1", structured.GetProperty("schemaVersion").GetString());
        Assert.Equal("ok", structured.GetProperty("status").GetString());
        Assert.Equal(10, structured.GetProperty("data").GetProperty("tools").GetArrayLength());
    }
}
