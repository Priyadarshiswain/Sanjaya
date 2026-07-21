using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Sanjaya.Core.Discovery;
using Sanjaya.Core.Git;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;
using Sanjaya.Providers.CSharp;
using Sanjaya.Server;
using Sanjaya.Server.Configuration;
using Sanjaya.Server.Diagnostics;
using Sanjaya.Server.Serialization;
using Sanjaya.Server.Tools;

try
{
    RepositoryScope repository = RepositoryScope.Create(RootConfiguration.Parse(args));
    HostApplicationBuilder builder = new(new HostApplicationBuilderSettings
    {
        // Avoid ambient configuration and default console providers in the MCP process.
        DisableDefaults = true,
    });

    // MCP reserves stdout for JSON-RPC. Host diagnostics must never be written there.
    builder.Logging.ClearProviders();

    builder.Services
        .AddSingleton(repository)
        .AddSingleton<CSharpSyntaxProvider>()
        .AddSingleton<IFileOutlineProvider>(services => services.GetRequiredService<CSharpSyntaxProvider>())
        .AddSingleton<IStructuralChunkProvider>(services => services.GetRequiredService<CSharpSyntaxProvider>())
        .AddSingleton<ICapabilityProvider>(services => services.GetRequiredService<CSharpSyntaxProvider>())
        .AddSingleton<SearchTextService>()
        .AddSingleton<FileOutlineService>()
        .AddSingleton<IGitCommandRunner, GitCommandRunner>()
        .AddSingleton<RecentChangesService>()
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
        .WithTools<HealthCheckTool>(SanjayaJson.Options)
        .WithTools<FileOutlineTool>(SanjayaJson.Options)
        .WithTools<SearchTextTool>(SanjayaJson.Options)
        .WithTools<RecentChangesTool>(SanjayaJson.Options);

    await builder.Build().RunAsync().ConfigureAwait(false);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(DiagnosticSanitizer.Sanitize(exception.Message));
    return 1;
}
