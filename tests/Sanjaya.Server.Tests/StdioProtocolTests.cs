using System.Diagnostics;
using System.Text.Json;
using Sanjaya.Core.Contracts;
using Xunit;

namespace Sanjaya.Server.Tests;

public sealed class StdioProtocolTests
{
    [Fact(Timeout = 30000)]
    public async Task MissingRootStillInitializesAndReturnsStructuredDiscoveryGuidance()
    {
        using ServerProcess server = StartServer(root: null);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));

        await server.InitializeAsync(timeout.Token);
        JsonElement tools = await server.ListToolsAsync(timeout.Token);
        Assert.Equal(
            PublicToolNames.ProtocolFoundation
                .Concat(PublicToolNames.ImmediateDiscovery)
                .Concat(PublicToolNames.LocalGitEvidence)
                .Concat(PublicToolNames.StructuralIndex)
                .Order(),
            tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).Order());
        Assert.All(tools.EnumerateArray(), tool => Assert.True(tool.TryGetProperty("outputSchema", out _)));
        JsonElement indexTool = tools.EnumerateArray().Single(
            tool => tool.GetProperty("name").GetString() == PublicToolNames.IndexCodebase);
        JsonElement annotations = indexTool.GetProperty("annotations");
        Assert.False(annotations.GetProperty("readOnlyHint").GetBoolean());
        Assert.True(annotations.GetProperty("destructiveHint").GetBoolean());
        Assert.True(annotations.GetProperty("idempotentHint").GetBoolean());
        Assert.False(annotations.GetProperty("openWorldHint").GetBoolean());

        JsonElement capabilities = await server.CallAsync(3, PublicToolNames.Capabilities, "{}", timeout.Token);
        AssertStructured(capabilities, 3, PublicToolNames.Capabilities, ContractValues.StatusOk, isError: false);
        Assert.False(capabilities.GetProperty("result").GetProperty("structuredContent").GetProperty("data").GetProperty("repositoryReady").GetBoolean());

        JsonElement health = await server.CallAsync(4, PublicToolNames.HealthCheck, "{}", timeout.Token);
        AssertStructured(health, 4, PublicToolNames.HealthCheck, ContractValues.StatusOk, isError: false);

        JsonElement search = await server.CallAsync(5, PublicToolNames.SearchText, "{\"query\":\"marker\"}", timeout.Token);
        AssertStructured(search, 5, PublicToolNames.SearchText, ContractValues.StatusError, isError: true);
        Assert.Equal(
            ContractValues.ErrorRepositoryRootRequired,
            search.GetProperty("result").GetProperty("structuredContent").GetProperty("error").GetProperty("code").GetString());

        await server.CloseAsync(timeout.Token);
        Assert.Equal(0, server.ExitCode);
        Assert.True(string.IsNullOrEmpty(await server.StandardError.ReadToEndAsync(timeout.Token)));
    }

    [Fact(Timeout = 30000)]
    public async Task GitRepositoryRootReturnsBoundedRevisionEvidenceWithoutAbsolutePaths()
    {
        string repositoryRoot = FindRepositoryRoot();
        using ServerProcess server = StartServer(repositoryRoot);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        await server.InitializeAsync(timeout.Token);

        JsonElement recent = await server.CallAsync(
            3,
            PublicToolNames.RecentChanges,
            "{\"limit\":1,\"includeWorkingTree\":false}",
            timeout.Token);

        JsonElement result = recent.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        JsonElement structured = result.GetProperty("structuredContent");
        Assert.Equal(PublicToolNames.RecentChanges, structured.GetProperty("capability").GetString());
        Assert.Single(structured.GetProperty("data").GetProperty("commits").EnumerateArray());
        Assert.False(structured.GetProperty("data").GetProperty("workingTree").GetProperty("included").GetBoolean());
        Assert.DoesNotContain(repositoryRoot, recent.GetRawText(), StringComparison.Ordinal);

        await server.CloseAsync(timeout.Token);
    }

    [Fact(Timeout = 30000)]
    public async Task ValidRootSupportsDirectStdioSearchAndOutlineWithoutAbsolutePathLeakage()
    {
        using TemporaryRepository repository = new("own-marker");
        using ServerProcess server = StartServer(repository.Path);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        await server.InitializeAsync(timeout.Token);

        JsonElement search = await server.CallAsync(3, PublicToolNames.SearchText, "{\"query\":\"own-marker\"}", timeout.Token);
        AssertStructured(search, 3, PublicToolNames.SearchText, ContractValues.StatusOk, isError: false);
        JsonElement structuredSearch = search.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal("marker.txt", structuredSearch.GetProperty("data").GetProperty("matches")[0].GetProperty("path").GetString());
        Assert.DoesNotContain(repository.Path, search.GetRawText(), StringComparison.Ordinal);

        JsonElement outline = await server.CallAsync(4, PublicToolNames.FileOutline, "{\"path\":\"marker.txt\"}", timeout.Token);
        AssertStructured(outline, 4, PublicToolNames.FileOutline, ContractValues.StatusOk, isError: false);
        Assert.Equal("marker.txt", outline.GetProperty("result").GetProperty("structuredContent").GetProperty("data").GetProperty("path").GetString());
        Assert.DoesNotContain(repository.Path, outline.GetRawText(), StringComparison.Ordinal);

        JsonElement csharpOutline = await server.CallAsync(6, PublicToolNames.FileOutline, "{\"path\":\"Sample.cs\"}", timeout.Token);
        AssertStructured(csharpOutline, 6, PublicToolNames.FileOutline, ContractValues.StatusOk, isError: false);
        JsonElement csharpStructured = csharpOutline.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal("csharp-roslyn-syntax", csharpStructured.GetProperty("provider").GetString());
        Assert.Equal("class", csharpStructured.GetProperty("data").GetProperty("items")[0].GetProperty("kind").GetString());
        Assert.DoesNotContain(repository.Path, csharpOutline.GetRawText(), StringComparison.Ordinal);

        JsonElement index = await server.CallAsync(7, PublicToolNames.IndexCodebase, "{}", timeout.Token);
        AssertStructured(index, 7, PublicToolNames.IndexCodebase, ContractValues.StatusOk, isError: false);
        JsonElement structuredIndex = index.GetProperty("result").GetProperty("structuredContent");
        Assert.Equal(".sanjaya/index-v1.json", structuredIndex.GetProperty("data").GetProperty("indexPath").GetString());
        Assert.Equal(1, structuredIndex.GetProperty("data").GetProperty("filesIndexed").GetInt32());
        Assert.True(File.Exists(System.IO.Path.Combine(repository.Path, ".sanjaya", "index-v1.json")));
        Assert.DoesNotContain(repository.Path, index.GetRawText(), StringComparison.Ordinal);

        JsonElement multiline = await server.CallAsync(5, PublicToolNames.SearchText, "{\"query\":\"two\\nlines\"}", timeout.Token);
        AssertStructured(multiline, 5, PublicToolNames.SearchText, ContractValues.StatusError, isError: true);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            multiline.GetProperty("result").GetProperty("structuredContent").GetProperty("error").GetProperty("code").GetString());

        await server.CloseAsync(timeout.Token);
    }

    [Fact(Timeout = 30000)]
    public async Task InvalidRootStillCompletesProtocolHandshake()
    {
        string invalid = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"missing-{Guid.NewGuid():N}");
        using ServerProcess server = StartServer(invalid);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));

        await server.InitializeAsync(timeout.Token);
        JsonElement capabilities = await server.CallAsync(3, PublicToolNames.Capabilities, "{}", timeout.Token);
        Assert.False(capabilities.GetProperty("result").GetProperty("structuredContent").GetProperty("data").GetProperty("repositoryReady").GetBoolean());
        Assert.DoesNotContain(invalid, capabilities.GetRawText(), StringComparison.Ordinal);
        await server.CloseAsync(timeout.Token);
    }

    [Fact(Timeout = 30000)]
    public async Task TwoServerProcessesKeepRepositoryScopesIsolated()
    {
        using TemporaryRepository firstRepository = new("FIRST_UNIQUE_MARKER");
        using TemporaryRepository secondRepository = new("SECOND_UNIQUE_MARKER");
        using ServerProcess first = StartServer(firstRepository.Path);
        using ServerProcess second = StartServer(secondRepository.Path);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        await Task.WhenAll(first.InitializeAsync(timeout.Token), second.InitializeAsync(timeout.Token));

        JsonElement firstOwn = await first.CallAsync(3, PublicToolNames.SearchText, "{\"query\":\"FIRST_UNIQUE_MARKER\"}", timeout.Token);
        JsonElement firstOther = await first.CallAsync(4, PublicToolNames.SearchText, "{\"query\":\"SECOND_UNIQUE_MARKER\"}", timeout.Token);
        JsonElement secondOwn = await second.CallAsync(3, PublicToolNames.SearchText, "{\"query\":\"SECOND_UNIQUE_MARKER\"}", timeout.Token);
        JsonElement secondOther = await second.CallAsync(4, PublicToolNames.SearchText, "{\"query\":\"FIRST_UNIQUE_MARKER\"}", timeout.Token);

        Assert.Single(firstOwn.GetProperty("result").GetProperty("structuredContent").GetProperty("data").GetProperty("matches").EnumerateArray());
        Assert.Empty(firstOther.GetProperty("result").GetProperty("structuredContent").GetProperty("data").GetProperty("matches").EnumerateArray());
        Assert.Single(secondOwn.GetProperty("result").GetProperty("structuredContent").GetProperty("data").GetProperty("matches").EnumerateArray());
        Assert.Empty(secondOther.GetProperty("result").GetProperty("structuredContent").GetProperty("data").GetProperty("matches").EnumerateArray());

        await first.CloseAsync(timeout.Token);
        Assert.Equal(0, first.ExitCode);
        JsonElement stillRunning = await second.CallAsync(5, PublicToolNames.HealthCheck, "{}", timeout.Token);
        AssertStructured(stillRunning, 5, PublicToolNames.HealthCheck, ContractValues.StatusOk, isError: false);
        await second.CloseAsync(timeout.Token);

        string allResponses = string.Concat(
            firstOwn.GetRawText(),
            firstOther.GetRawText(),
            secondOwn.GetRawText(),
            secondOther.GetRawText());
        Assert.DoesNotContain(firstRepository.Path, allResponses, StringComparison.Ordinal);
        Assert.DoesNotContain(secondRepository.Path, allResponses, StringComparison.Ordinal);
    }

    private static ServerProcess StartServer(string? root)
    {
        string repositoryRoot = FindRepositoryRoot();
        string serverAssembly = System.IO.Path.Combine(
            repositoryRoot,
            "src",
            "Sanjaya.Server",
            "bin",
            "Release",
            "net8.0",
            "Sanjaya.Server.dll");
        Assert.True(File.Exists(serverAssembly), $"Server assembly was not built: {serverAssembly}");

        ProcessStartInfo startInfo = new(Environment.ProcessPath!)
        {
            WorkingDirectory = System.IO.Path.GetTempPath(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(serverAssembly);
        if (root is not null)
        {
            startInfo.ArgumentList.Add("--root");
            startInfo.ArgumentList.Add(root);
        }

        return new ServerProcess(Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start server."));
    }

    private static void AssertStructured(JsonElement response, int id, string capability, string status, bool isError)
    {
        Assert.Equal(id, response.GetProperty("id").GetInt32());
        JsonElement result = response.GetProperty("result");
        Assert.Equal(isError, result.GetProperty("isError").GetBoolean());
        Assert.Equal("text", result.GetProperty("content")[0].GetProperty("type").GetString());
        JsonElement structured = result.GetProperty("structuredContent");
        Assert.Equal("1", structured.GetProperty("schemaVersion").GetString());
        Assert.Equal(status, structured.GetProperty("status").GetString());
        Assert.Equal(capability, structured.GetProperty("capability").GetString());
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Sanjaya.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private sealed class ServerProcess(Process process) : IDisposable
    {
        public StreamReader StandardError => process.StandardError;

        public int ExitCode => process.ExitCode;

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await SendAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"sanjaya-tests\",\"version\":\"1.0\"}}}");
            JsonElement initialize = await ReadAsync(cancellationToken);
            Assert.Equal("sanjaya", initialize.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
            await SendAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        }

        public async Task<JsonElement> ListToolsAsync(CancellationToken cancellationToken)
        {
            await SendAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");
            return (await ReadAsync(cancellationToken)).GetProperty("result").GetProperty("tools").Clone();
        }

        public async Task<JsonElement> CallAsync(int id, string tool, string arguments, CancellationToken cancellationToken)
        {
            await SendAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"tools/call\",\"params\":{{\"name\":\"{tool}\",\"arguments\":{arguments}}}}}");
            return await ReadAsync(cancellationToken);
        }

        public async Task CloseAsync(CancellationToken cancellationToken)
        {
            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);
        }

        public void Dispose()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.Dispose();
        }

        private async Task SendAsync(string message)
        {
            await process.StandardInput.WriteLineAsync(message);
            await process.StandardInput.FlushAsync();
        }

        private async Task<JsonElement> ReadAsync(CancellationToken cancellationToken)
        {
            string? line = await process.StandardOutput.ReadLineAsync(cancellationToken);
            Assert.False(string.IsNullOrWhiteSpace(line));
            using JsonDocument document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository(string marker)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sanjaya-process-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            File.WriteAllText(System.IO.Path.Combine(Path, "marker.txt"), marker);
            File.WriteAllText(System.IO.Path.Combine(Path, "Sample.cs"), "public class Sample { public void Run() { } }");
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
