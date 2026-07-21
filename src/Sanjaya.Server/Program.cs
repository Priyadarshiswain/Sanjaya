using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Sanjaya.Server;
using Sanjaya.Server.Diagnostics;
using Sanjaya.Server.Serialization;
using Sanjaya.Server.Tools;

try
{
    HostApplicationBuilder builder = new(new HostApplicationBuilderSettings
    {
        // Avoid ambient configuration and default console providers in the MCP process.
        DisableDefaults = true,
    });

    // MCP reserves stdout for JSON-RPC. Host diagnostics must never be written there.
    builder.Logging.ClearProviders();

    builder.Services
        .AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "sanjaya",
                Title = "Sanjaya",
                Version = SanjayaRuntime.BuildVersion,
                Description = "Local-first codebase discovery for AI agents.",
                WebsiteUrl = "https://github.com/Priyadarshiswain/Sanjaya",
            };
        })
        .WithStdioServerTransport()
        .WithTools<CapabilitiesTool>(SanjayaJson.Options)
        .WithTools<HealthCheckTool>(SanjayaJson.Options);

    await builder.Build().RunAsync().ConfigureAwait(false);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(DiagnosticSanitizer.Sanitize(exception.Message));
    return 1;
}
