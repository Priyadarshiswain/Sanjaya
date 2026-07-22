using System.Diagnostics;
using Sanjaya.Core.Providers;
using Xunit;

namespace Sanjaya.Providers.TypeScript.Tests;

public sealed class TypeScriptWorkerTests
{
    private static readonly string NodeExecutable = FindNodeExecutable();
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMilliseconds(500);

    [Fact]
    public void ReusesAValidatedWorkerForDeterministicAnalysis()
    {
        using WorkerFixture fixture = new("success");
        using TypeScriptWorker worker = fixture.CreateWorker();

        worker.Initialize(CancellationToken.None);
        TypeScriptWorkerAnalysis first = worker.Analyze(
            "src/widget.ts",
            "typescript",
            "class Widget {}",
            CancellationToken.None);
        TypeScriptWorkerAnalysis second = worker.Analyze(
            "src/widget.ts",
            "typescript",
            "class Widget {}",
            CancellationToken.None);

        Assert.Equal(first.Items, second.Items);
        Assert.Equal(first.ItemsTruncated, second.ItemsTruncated);
        Assert.Equal(first.Chunks, second.Chunks);
        Assert.Equal(first.ChunksTruncated, second.ChunksTruncated);
        Assert.Equal(first.SyntaxDiagnosticCount, second.SyntaxDiagnosticCount);
        Assert.Equal("Widget", Assert.Single(first.Items).Name);
        Assert.Equal("Widget", Assert.Single(first.Chunks).Name);
    }

    [Theory]
    [InlineData("old-runtime", true, StructuralProviderFailure.Unavailable)]
    [InlineData("startup-hang", true, StructuralProviderFailure.TimedOut)]
    [InlineData("analysis-hang", false, StructuralProviderFailure.TimedOut)]
    [InlineData("invalid-json", false, StructuralProviderFailure.InvalidOutput)]
    [InlineData("wrong-id", false, StructuralProviderFailure.InvalidOutput)]
    [InlineData("stderr-overflow", false, StructuralProviderFailure.OutputLimit)]
    [InlineData("early-exit", false, StructuralProviderFailure.Unavailable)]
    public void MapsBoundedWorkerFailuresWithoutDiagnostics(
        string mode,
        bool failsDuringStartup,
        StructuralProviderFailure expected)
    {
        using WorkerFixture fixture = new(mode);
        using TypeScriptWorker worker = fixture.CreateWorker();

        StructuralProviderException exception = failsDuringStartup
            ? Assert.Throws<StructuralProviderException>(() => worker.Initialize(CancellationToken.None))
            : AnalyzeAfterInitialize(worker);

        Assert.Equal(expected, exception.Failure);
        Assert.Equal(typeof(StructuralProviderException).FullName, exception.GetType().FullName);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void CancellationTerminatesTheWorkerAndTheNextRequestRestartsCleanly()
    {
        using WorkerFixture fixture = new("analysis-hang");
        using TypeScriptWorker worker = fixture.CreateWorker();
        worker.Initialize(CancellationToken.None);
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(50));

        Assert.ThrowsAny<OperationCanceledException>(() => worker.Analyze(
            "src/widget.ts",
            "typescript",
            "class Widget {}",
            cancellation.Token));

        fixture.SetMode("success");
        TypeScriptWorkerAnalysis restarted = worker.Analyze(
            "src/widget.ts",
            "typescript",
            "class Widget {}",
            CancellationToken.None);
        Assert.Equal("Widget", Assert.Single(restarted.Items).Name);
    }

    [Fact]
    public void EarlyExitDoesNotPoisonTheNextWorkerProcess()
    {
        using WorkerFixture fixture = new("early-exit");
        using TypeScriptWorker worker = fixture.CreateWorker();
        worker.Initialize(CancellationToken.None);

        StructuralProviderException failure = Assert.Throws<StructuralProviderException>(() => worker.Analyze(
            "src/widget.ts",
            "typescript",
            "class Widget {}",
            CancellationToken.None));
        Assert.Equal(StructuralProviderFailure.Unavailable, failure.Failure);

        fixture.SetMode("success");
        Assert.Equal(
            "Widget",
            Assert.Single(worker.Analyze(
                "src/widget.ts",
                "typescript",
                "class Widget {}",
                CancellationToken.None).Items).Name);
    }

    [Fact]
    public void RejectsOversizedRequestsBeforeWritingToTheWorker()
    {
        using WorkerFixture fixture = new("success");
        using TypeScriptWorker worker = fixture.CreateWorker();
        worker.Initialize(CancellationToken.None);

        StructuralProviderException failure = Assert.Throws<StructuralProviderException>(() => worker.Analyze(
            "src/large.ts",
            "typescript",
            new string('x', TypeScriptWorker.MaximumRequestBytes),
            CancellationToken.None));

        Assert.Equal(StructuralProviderFailure.OutputLimit, failure.Failure);
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        using WorkerFixture fixture = new("success");
        TypeScriptWorker worker = fixture.CreateWorker();
        worker.Initialize(CancellationToken.None);

        worker.Dispose();
        worker.Dispose();
    }

    private static StructuralProviderException AnalyzeAfterInitialize(TypeScriptWorker worker)
    {
        worker.Initialize(CancellationToken.None);
        return Assert.Throws<StructuralProviderException>(() => worker.Analyze(
            "src/widget.ts",
            "typescript",
            "class Widget {}",
            CancellationToken.None));
    }

    private static string FindNodeExecutable()
    {
        ProcessStartInfo start = new("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--print");
        start.ArgumentList.Add("process.execPath");
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start Node.js for worker contract tests.");
        string executable = process.StandardOutput.ReadToEnd().Trim();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !Path.IsPathFullyQualified(executable) || !File.Exists(executable))
        {
            throw new InvalidOperationException($"Could not resolve Node.js for worker contract tests: {error}");
        }

        return executable;
    }

    private sealed class WorkerFixture : IDisposable
    {
        private readonly string root = Path.Combine(CanonicalTemporaryRoot(), $"sanjaya-worker-{Guid.NewGuid():N}");

        public WorkerFixture(string mode)
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(CompilerRoot);
            SetMode(mode);
        }

        private string WorkerPath => Path.Combine(root, "fake-worker.mjs");

        private string CompilerRoot => Path.Combine(root, "compiler");

        public TypeScriptWorker CreateWorker() => new(
            NodeExecutable,
            WorkerPath,
            CompilerRoot,
            TestTimeout,
            TestTimeout);

        public void SetMode(string mode) => File.WriteAllText(
            WorkerPath,
            WorkerScript.Replace("__MODE__", mode, StringComparison.Ordinal));

        public void Dispose() => Directory.Delete(root, recursive: true);

        private static string CanonicalTemporaryRoot()
        {
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            return OperatingSystem.IsMacOS() && temporaryRoot.StartsWith("/var/", StringComparison.Ordinal)
                ? $"/private{temporaryRoot}"
                : temporaryRoot;
        }

        private const string WorkerScript = """
            const mode = "__MODE__";
            let pending = "";
            process.stdin.setEncoding("utf8");
            process.stdin.on("data", (chunk) => {
              pending += chunk;
              while (true) {
                const newline = pending.indexOf("\n");
                if (newline < 0) return;
                const request = JSON.parse(pending.slice(0, newline));
                pending = pending.slice(newline + 1);
                handle(request);
              }
            });

            function send(value) {
              process.stdout.write(`${JSON.stringify(value)}\n`);
            }

            function handle(request) {
              if (request.operation === "handshake") {
                if (mode === "startup-hang") return;
                send({
                  protocolVersion: "1",
                  requestId: request.requestId,
                  status: "ok",
                  nodeVersion: mode === "old-runtime" ? "21.0.0" : process.versions.node,
                  typescriptVersion: "6.0.3",
                });
                return;
              }

              if (mode === "analysis-hang") return;
              if (mode === "invalid-json") {
                process.stdout.write("{invalid}\n");
                return;
              }
              if (mode === "early-exit") process.exit(2);
              const response = {
                protocolVersion: "1",
                requestId: mode === "wrong-id" ? "wrong" : request.requestId,
                status: "ok",
                items: [{
                  kind: "class",
                  name: "Widget",
                  display: "class Widget {}",
                  container: null,
                  startLine: 1,
                  endLine: 1,
                }],
                itemsTruncated: false,
                chunks: [{
                  kind: "class",
                  name: "Widget",
                  container: null,
                  startLine: 1,
                  endLine: 1,
                  content: "class Widget {}",
                  contentTruncated: false,
                }],
                chunksTruncated: false,
                syntaxDiagnosticCount: 0,
              };
              if (mode === "stderr-overflow") {
                process.stderr.write("x".repeat(40 * 1024));
                setTimeout(() => send(response), 50);
                return;
              }
              send(response);
            }
            """;
    }
}
