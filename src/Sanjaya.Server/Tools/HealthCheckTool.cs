using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;
using Sanjaya.Providers.TypeScript;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

/// <summary>
/// Reports only health facts the current process can verify directly.
/// </summary>
public sealed class HealthCheckTool(
    RepositoryScope repository,
    IEnumerable<ICapabilityProvider> capabilityProviders)
{
    [McpServerTool(
        Name = PublicToolNames.HealthCheck,
        Title = "Sanjaya health check",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<HealthReportData>))]
    [Description("Reports the health of Sanjaya's running stdio protocol foundation.")]
    public CallToolResult CheckHealth()
    {
        ToolResponse<HealthReportData> response = CreateResponse();
        return McpToolResultFactory.Create(
            response,
            $"Sanjaya health is {response.Status} on stdio with {response.Data!.RegisteredToolCount} registered tools.");
    }

    public ToolResponse<HealthReportData> CreateResponse()
    {
        List<ICapabilityProvider> providers = capabilityProviders.ToList();
        bool typeScriptReady = new[]
        {
            TypeScriptSyntaxProvider.TypeScriptProviderId,
            TypeScriptSyntaxProvider.JavaScriptProviderId,
        }.All(id => providers.Any(provider => provider.Id == id
            && provider.GetCapabilities().Any(capability => capability.Status == CapabilityStatus.Supported)));

        List<HealthCheckEntry> checks =
        [
            new("server", ContractValues.StatusOk, "The .NET 8 MCP server is running."),
            new("transport", ContractValues.StatusOk, "JSON-RPC is using stdio."),
            new("stdout", ContractValues.StatusOk, "Stdout is reserved for MCP protocol messages."),
            new("network", ContractValues.StatusOk, "The default implementation contains no network operation."),
            repository.IsReady
                ? new("repository", ContractValues.StatusOk, "The configured repository root is ready.")
                : new(
                    "repository",
                    ContractValues.StatusPartial,
                    repository.ConfigurationError!,
                    repository.ConfigurationReason,
                    repository.ConfigurationRemediation),
            typeScriptReady
                ? new("typescript_worker", ContractValues.StatusOk, "The bundled TypeScript worker is ready.")
                : new(
                    "typescript_worker",
                    ContractValues.StatusPartial,
                    "The bundled TypeScript worker is unavailable.",
                    ContractValues.ReasonRuntimeUnavailable,
                    "Reinstall the reviewed Sanjaya package."),
            repository.IsReady && repository.IsGitWorktreeCandidate
                ? new("git", ContractValues.StatusOk, "Top-level Git worktree metadata is present for optional local change evidence.", Required: false)
                : new(
                    "git",
                    ContractValues.StatusPartial,
                    repository.IsReady
                        ? "The configured root is not a Git worktree root; code discovery remains available."
                        : "Git readiness will be checked after a repository root is configured.",
                    repository.IsReady ? ContractValues.ReasonNotGitRepository : repository.ConfigurationReason,
                    repository.IsReady
                        ? "Use a Git worktree root only if recent_changes is needed."
                        : repository.ConfigurationRemediation,
                    Required: false),
        ];

        bool ready = checks.Where(check => check.Required)
            .All(check => check.Status == ContractValues.StatusOk);
        HealthReportData data = new(SanjayaRuntime.RegisteredToolCount, ready, checks);

        return new ToolResponse<HealthReportData>(
            ready ? ContractValues.StatusOk : ContractValues.StatusPartial,
            PublicToolNames.HealthCheck,
            "sanjaya-runtime",
            data,
            [],
            []);
    }
}
