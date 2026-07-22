using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Sanjaya.Core.Discovery;
using Sanjaya.Core.Git;
using Sanjaya.Core.Indexing;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;
using Sanjaya.Providers.CSharp;
using Sanjaya.Providers.TypeScript;
using Sanjaya.Server;
using Sanjaya.Server.Configuration;
using Sanjaya.Server.Diagnostics;
using Sanjaya.Server.Serialization;
using Sanjaya.Server.Tools;

try
{
    RepositoryScope repository = RepositoryScope.Create(RootConfiguration.Parse(args));
    using TypeScriptWorker? typeScriptWorker = TypeScriptWorker.TryCreate(AppContext.BaseDirectory);
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
        .AddSingleton<IReferenceProvider>(services => services.GetRequiredService<CSharpSyntaxProvider>())
        .AddSingleton<ISourceRetrievalProvider>(services => services.GetRequiredService<CSharpSyntaxProvider>())
        .AddSingleton<ICapabilityProvider>(services => services.GetRequiredService<CSharpSyntaxProvider>());

    if (typeScriptWorker is not null)
    {
        TypeScriptSyntaxProvider typeScript = new("typescript", typeScriptWorker);
        TypeScriptSyntaxProvider javaScript = new("javascript", typeScriptWorker);
        builder.Services
            .AddSingleton<IFileOutlineProvider>(typeScript)
            .AddSingleton<IStructuralChunkProvider>(typeScript)
            .AddSingleton<ICapabilityProvider>(typeScript)
            .AddSingleton<IFileOutlineProvider>(javaScript)
            .AddSingleton<IStructuralChunkProvider>(javaScript)
            .AddSingleton<ICapabilityProvider>(javaScript);
    }
    else
    {
        UnavailableTypeScriptProvider typeScript = new("typescript");
        UnavailableTypeScriptProvider javaScript = new("javascript");
        builder.Services
            .AddSingleton<IFileOutlineProvider>(typeScript)
            .AddSingleton<ICapabilityProvider>(typeScript)
            .AddSingleton<IFileOutlineProvider>(javaScript)
            .AddSingleton<ICapabilityProvider>(javaScript);
    }

    builder.Services
        .AddSingleton<SearchTextService>()
        .AddSingleton<FileOutlineService>()
        .AddSingleton(services => new IndexCodebaseService(
            repository,
            services.GetServices<IStructuralChunkProvider>(),
            SanjayaRuntime.BuildVersion))
        .AddSingleton(services => new SearchCodeService(
            repository,
            services.GetServices<IStructuralChunkProvider>()))
        .AddSingleton(services => new FindDefinitionService(
            repository,
            services.GetServices<IStructuralChunkProvider>()))
        .AddSingleton(services => new FindReferencesService(
            repository,
            services.GetServices<IStructuralChunkProvider>(),
            services.GetServices<IReferenceProvider>()))
        .AddSingleton(services => new GetSourceService(
            repository,
            services.GetServices<IStructuralChunkProvider>(),
            services.GetServices<ISourceRetrievalProvider>()))
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
        .WithTools<RecentChangesTool>(SanjayaJson.Options)
        .WithTools<IndexCodebaseTool>(SanjayaJson.Options)
        .WithTools<SearchCodeTool>(SanjayaJson.Options)
        .WithTools<FindDefinitionTool>(SanjayaJson.Options)
        .WithTools<FindReferencesTool>(SanjayaJson.Options)
        .WithTools<GetSourceTool>(SanjayaJson.Options);

    await builder.Build().RunAsync().ConfigureAwait(false);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(DiagnosticSanitizer.Sanitize(exception.Message));
    return 1;
}
