using System.Diagnostics;
using System.Text.Json;
using Sanjaya.Core.Contracts;
using Xunit;

namespace Sanjaya.Server.Tests;

public sealed class StdioProtocolTests
{
    [Fact(Timeout = 30000)]
    public async Task ServerCompletesProtocolHandshakeAndExposesOnlyImplementedTools()
    {
        string repositoryRoot = FindRepositoryRoot();
        string serverAssembly = Path.Combine(
            repositoryRoot,
            "src",
            "Sanjaya.Server",
            "bin",
            "Release",
            "net8.0",
            "Sanjaya.Server.dll");
        Assert.True(File.Exists(serverAssembly), $"Server assembly was not built: {serverAssembly}");

        using Process process = StartServer(serverAssembly, repositoryRoot);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));

        try
        {
            await SendAsync(
                process,
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"sanjaya-tests","version":"1.0"}}}""");
            JsonElement initialize = await ReadResponseAsync(process, timeout.Token);
            Assert.Equal(1, initialize.GetProperty("id").GetInt32());
            Assert.Equal("sanjaya", initialize.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());

            await SendAsync(process, """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            await SendAsync(process, """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
            JsonElement list = await ReadResponseAsync(process, timeout.Token);
            JsonElement tools = list.GetProperty("result").GetProperty("tools");
            Assert.Equal(PublicToolNames.ProtocolFoundation, tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()));
            Assert.All(tools.EnumerateArray(), tool => Assert.True(tool.TryGetProperty("outputSchema", out _)));

            await SendAsync(process, """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"capabilities","arguments":{}}}""");
            JsonElement capabilities = await ReadResponseAsync(process, timeout.Token);
            AssertStructuredSuccess(capabilities, 3, PublicToolNames.Capabilities);

            await SendAsync(process, """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"health_check","arguments":{}}}""");
            JsonElement health = await ReadResponseAsync(process, timeout.Token);
            AssertStructuredSuccess(health, 4, PublicToolNames.HealthCheck);

            await SendAsync(process, """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"search_code","arguments":{}}}""");
            JsonElement unknown = await ReadResponseAsync(process, timeout.Token);
            Assert.Equal(5, unknown.GetProperty("id").GetInt32());
            Assert.True(
                unknown.TryGetProperty("error", out _)
                || unknown.GetProperty("result").GetProperty("isError").GetBoolean());

            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    private static Process StartServer(string serverAssembly, string repositoryRoot)
    {
        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(serverAssembly);

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the Sanjaya server process.");
    }

    private static async Task SendAsync(Process process, string message)
    {
        await process.StandardInput.WriteLineAsync(message);
        await process.StandardInput.FlushAsync();
    }

    private static async Task<JsonElement> ReadResponseAsync(Process process, CancellationToken cancellationToken)
    {
        string? line = await process.StandardOutput.ReadLineAsync(cancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(line));

        using JsonDocument document = JsonDocument.Parse(line);
        return document.RootElement.Clone();
    }

    private static void AssertStructuredSuccess(JsonElement response, int id, string capability)
    {
        Assert.Equal(id, response.GetProperty("id").GetInt32());
        JsonElement result = response.GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        Assert.Equal("text", result.GetProperty("content")[0].GetProperty("type").GetString());
        JsonElement structured = result.GetProperty("structuredContent");
        Assert.Equal("1", structured.GetProperty("schemaVersion").GetString());
        Assert.Equal("ok", structured.GetProperty("status").GetString());
        Assert.Equal(capability, structured.GetProperty("capability").GetString());
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("error").ValueKind);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sanjaya.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Sanjaya repository root.");
    }
}
