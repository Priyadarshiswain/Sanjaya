using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;

namespace Sanjaya.Providers.TypeScript;

public sealed class TypeScriptWorker : ITypeScriptWorker
{
    public const string NodeExecutableEnvironmentVariable = "SANJAYA_NODE_EXECUTABLE";
    public const string ProtocolVersion = "1";
    public const int MaximumRequestBytes = 8 * 1024 * 1024;
    public const int MaximumResponseBytes = 16 * 1024 * 1024;
    public const int MaximumErrorBytes = 32 * 1024;
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan AnalysisTimeout = TimeSpan.FromSeconds(5);

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly HashSet<string> ApprovedKinds = new(StringComparer.Ordinal)
    {
        "namespace",
        "module",
        "class",
        "interface",
        "type_alias",
        "enum",
        "function",
        "method",
        "constructor",
        "getter",
        "setter",
        "property",
        "variable",
    };

    private readonly string nodeExecutable;
    private readonly string workerPath;
    private readonly string compilerPackageRoot;
    private readonly TimeSpan startupTimeout;
    private readonly TimeSpan analysisTimeout;
    private readonly SemaphoreSlim gate = new(1, 1);
    private Process? process;
    private BoundedLineReader? output;
    private TaskCompletionSource<bool>? stderrLimit;
    private bool ready;
    private int nextRequestId;
    private bool disposed;

    internal TypeScriptWorker(
        string nodeExecutable,
        string workerPath,
        string compilerPackageRoot,
        TimeSpan? startupTimeout = null,
        TimeSpan? analysisTimeout = null)
    {
        this.nodeExecutable = nodeExecutable;
        this.workerPath = workerPath;
        this.compilerPackageRoot = compilerPackageRoot;
        this.startupTimeout = ValidateTimeout(startupTimeout ?? StartupTimeout, nameof(startupTimeout));
        this.analysisTimeout = ValidateTimeout(analysisTimeout ?? AnalysisTimeout, nameof(analysisTimeout));
    }

    public static TypeScriptWorker? TryCreate(string applicationBaseDirectory)
    {
        string? nodeExecutable = Environment.GetEnvironmentVariable(NodeExecutableEnvironmentVariable);
        string workerPath = Path.Combine(
            applicationBaseDirectory,
            "runtime",
            "typescript",
            "typescript-worker.mjs");
        string compilerPackageRoot = Path.Combine(
            applicationBaseDirectory,
            "third_party",
            "typescript",
            "package");
        string compilerPath = Path.Combine(compilerPackageRoot, "lib", "typescript.js");
        if (string.IsNullOrWhiteSpace(nodeExecutable)
            || !Path.IsPathFullyQualified(nodeExecutable)
            || !File.Exists(nodeExecutable)
            || !File.Exists(workerPath)
            || !File.Exists(compilerPath))
        {
            return null;
        }

        TypeScriptWorker worker = new(
            Path.GetFullPath(nodeExecutable),
            Path.GetFullPath(workerPath),
            Path.GetFullPath(compilerPackageRoot));
        try
        {
            worker.Initialize(CancellationToken.None);
            return worker;
        }
        catch (Exception exception) when (exception is StructuralProviderException
            or OperationCanceledException
            or IOException
            or JsonException
            or DecoderFallbackException
            or InvalidOperationException)
        {
            worker.Dispose();
            return null;
        }
    }

    public TypeScriptWorkerAnalysis Analyze(
        string relativePath,
        string language,
        string sourceText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(sourceText);
        gate.Wait(cancellationToken);
        try
        {
            ThrowIfDisposed();
            EnsureReady(cancellationToken);
            string requestId = NextRequestId();
            WorkerResponse response = Exchange(
                new WorkerRequest(
                    ProtocolVersion,
                    requestId,
                    "analyze",
                    language,
                    relativePath,
                    sourceText),
                analysisTimeout,
                cancellationToken);
            ValidateCommon(response, requestId);
            ValidateAnalysis(response, CountLines(sourceText));
            return new TypeScriptWorkerAnalysis(
                response.Items!,
                response.ItemsTruncated!.Value,
                response.Chunks!,
                response.ChunksTruncated!.Value,
                response.SyntaxDiagnosticCount!.Value);
        }
        catch (OperationCanceledException)
        {
            ResetProcess();
            throw;
        }
        catch (StructuralProviderException)
        {
            ResetProcess();
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or JsonException
            or DecoderFallbackException
            or InvalidOperationException)
        {
            ResetProcess();
            throw new StructuralProviderException(StructuralProviderFailure.InvalidOutput);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        gate.Wait();
        try
        {
            if (!disposed)
            {
                disposed = true;
                ResetProcess();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    internal void Initialize(CancellationToken cancellationToken)
    {
        gate.Wait(cancellationToken);
        try
        {
            ThrowIfDisposed();
            EnsureReady(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private void EnsureReady(CancellationToken cancellationToken)
    {
        if (ready && process is not null && !process.HasExited)
        {
            return;
        }

        ResetProcess();
        StartProcess();
        string requestId = NextRequestId();
        WorkerResponse response;
        try
        {
            response = Exchange(
                new WorkerRequest(ProtocolVersion, requestId, "handshake", null, null, null),
                startupTimeout,
                cancellationToken);
            ValidateCommon(response, requestId);
            ValidateHandshake(response);
            if (!Version.TryParse(response.NodeVersion, out Version? nodeVersion)
                || nodeVersion.Major < 22
                || (nodeVersion.Major == 22 && nodeVersion.Minor < 13)
                || !string.Equals(response.TypeScriptVersion, "6.0.3", StringComparison.Ordinal))
            {
                throw new StructuralProviderException(StructuralProviderFailure.Unavailable);
            }

            ready = true;
        }
        catch
        {
            ResetProcess();
            throw;
        }
    }

    private void StartProcess()
    {
        ProcessStartInfo start = new(nodeExecutable)
        {
            WorkingDirectory = Path.GetDirectoryName(workerPath)!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (string argument in new[]
                 {
                     "--permission",
                     $"--allow-fs-read={Path.GetDirectoryName(workerPath)!}",
                     $"--allow-fs-read={compilerPackageRoot}",
                     "--no-addons",
                     "--no-global-search-paths",
                     "--disable-proto=throw",
                     "--no-warnings",
                     "--max-old-space-size=256",
                     workerPath,
                 })
        {
            start.ArgumentList.Add(argument);
        }

        Dictionary<string, string?> ambient = start.Environment.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.OrdinalIgnoreCase);
        start.Environment.Clear();
        CopyEnvironment(ambient, start, "SystemRoot");
        CopyEnvironment(ambient, start, "WINDIR");
        start.Environment["TZ"] = "UTC";

        Process candidate = new() { StartInfo = start };
        try
        {
            if (!candidate.Start())
            {
                candidate.Dispose();
                throw new StructuralProviderException(StructuralProviderFailure.Unavailable);
            }
        }
        catch (Exception exception) when (exception is Win32Exception
            or InvalidOperationException
            or UnauthorizedAccessException)
        {
            candidate.Dispose();
            throw new StructuralProviderException(StructuralProviderFailure.Unavailable);
        }

        process = candidate;
        output = new BoundedLineReader(candidate.StandardOutput.BaseStream);
        stderrLimit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = MonitorStandardErrorAsync(candidate, stderrLimit);
    }

    private WorkerResponse Exchange(
        WorkerRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (process is null || output is null || stderrLimit is null || process.HasExited)
        {
            throw new StructuralProviderException(StructuralProviderFailure.Unavailable);
        }
        TaskCompletionSource<bool> currentStderrLimit = stderrLimit;

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        if (payload.Length + 1 > MaximumRequestBytes)
        {
            throw new StructuralProviderException(StructuralProviderFailure.OutputLimit);
        }

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            process.StandardInput.BaseStream.WriteAsync(payload, deadline.Token).AsTask().GetAwaiter().GetResult();
            process.StandardInput.BaseStream.WriteAsync("\n"u8.ToArray(), deadline.Token).AsTask().GetAwaiter().GetResult();
            process.StandardInput.BaseStream.FlushAsync(deadline.Token).GetAwaiter().GetResult();
            Task<string> responseTask = output.ReadAsync(MaximumResponseBytes, deadline.Token);
            Task completed = Task.WhenAny(responseTask, currentStderrLimit.Task)
                .WaitAsync(deadline.Token)
                .GetAwaiter()
                .GetResult();
            if (completed == currentStderrLimit.Task)
            {
                throw new StructuralProviderException(StructuralProviderFailure.OutputLimit);
            }

            string responseText = responseTask.GetAwaiter().GetResult();
            if (currentStderrLimit.Task.IsCompleted)
            {
                throw new StructuralProviderException(StructuralProviderFailure.OutputLimit);
            }

            return JsonSerializer.Deserialize<WorkerResponse>(responseText, JsonOptions)
                ?? throw new JsonException("Worker response was null.");
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new StructuralProviderException(StructuralProviderFailure.TimedOut);
        }
        catch (BoundedLineException)
        {
            throw new StructuralProviderException(StructuralProviderFailure.OutputLimit);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            throw new StructuralProviderException(
                currentStderrLimit.Task.IsCompleted
                    ? StructuralProviderFailure.OutputLimit
                    : StructuralProviderFailure.Unavailable);
        }
    }

    private static async Task MonitorStandardErrorAsync(
        Process monitoredProcess,
        TaskCompletionSource<bool> limitSignal)
    {
        byte[] buffer = new byte[4096];
        int total = 0;
        try
        {
            int read;
            while ((read = await monitoredProcess.StandardError.BaseStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaximumErrorBytes)
                {
                    limitSignal.TrySetResult(true);
                    TryKill(monitoredProcess);
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Process teardown closes the redirected stream.
        }
    }

    private static void ValidateCommon(WorkerResponse response, string requestId)
    {
        if (!string.Equals(response.ProtocolVersion, ProtocolVersion, StringComparison.Ordinal)
            || !string.Equals(response.RequestId, requestId, StringComparison.Ordinal)
            || !string.Equals(response.Status, "ok", StringComparison.Ordinal))
        {
            throw new StructuralProviderException(StructuralProviderFailure.InvalidOutput);
        }
    }

    private static void ValidateHandshake(WorkerResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.NodeVersion)
            || string.IsNullOrWhiteSpace(response.TypeScriptVersion)
            || response.Items is not null
            || response.ItemsTruncated is not null
            || response.Chunks is not null
            || response.ChunksTruncated is not null
            || response.SyntaxDiagnosticCount is not null)
        {
            throw new StructuralProviderException(StructuralProviderFailure.InvalidOutput);
        }
    }

    private static void ValidateAnalysis(WorkerResponse response, int maximumLine)
    {
        if (response.NodeVersion is not null
            || response.TypeScriptVersion is not null
            || response.Items is null
            || response.ItemsTruncated is null
            || response.Chunks is null
            || response.ChunksTruncated is null
            || response.SyntaxDiagnosticCount is null
            || response.SyntaxDiagnosticCount < 0
            || response.Items.Count > 500
            || response.Chunks.Count > 500
            || response.Items.Count != response.Chunks.Count
            || response.ItemsTruncated != response.ChunksTruncated)
        {
            throw new StructuralProviderException(StructuralProviderFailure.InvalidOutput);
        }

        for (int index = 0; index < response.Items.Count; index++)
        {
            OutlineItem item = response.Items[index];
            StructuralChunk chunk = response.Chunks[index];
            if (!IsValidMetadata(item.Kind, item.Name, item.Container, item.StartLine, item.EndLine)
                || item.Display is null
                || item.Display.Length is < 1 or > 240
                || ContainsControl(item.Display)
                || !IsValidMetadata(chunk.Kind, chunk.Name, chunk.Container, chunk.StartLine, chunk.EndLine)
                || chunk.Content is null
                || chunk.Content.Length > 64 * 1024
                || !string.Equals(item.Kind, chunk.Kind, StringComparison.Ordinal)
                || !string.Equals(item.Name, chunk.Name, StringComparison.Ordinal)
                || !string.Equals(item.Container, chunk.Container, StringComparison.Ordinal)
                || item.StartLine != chunk.StartLine
                || item.EndLine != chunk.EndLine
                || item.EndLine > maximumLine)
            {
                throw new StructuralProviderException(StructuralProviderFailure.InvalidOutput);
            }
        }
    }

    private static bool IsValidMetadata(
        string? kind,
        string? name,
        string? container,
        int startLine,
        int endLine) =>
        kind is not null
        && ApprovedKinds.Contains(kind)
        && name is not null
        && name.Length is > 0 and <= 240
        && !ContainsControl(name)
        && (container is null || (container.Length <= 240 && !ContainsControl(container)))
        && startLine >= 1
        && endLine >= startLine;

    private static bool ContainsControl(string value) =>
        value.Any(character => character is '\0' or '\r' or '\n');

    private static int CountLines(string sourceText) =>
        sourceText.Length == 0
            ? 1
            : sourceText.Count(character => character == '\n') + (sourceText.EndsWith('\n') ? 0 : 1);

    private static TimeSpan ValidateTimeout(TimeSpan value, string parameterName) =>
        value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(parameterName);

    private string NextRequestId() => Interlocked.Increment(ref nextRequestId).ToString(CultureInfo.InvariantCulture);

    private static void CopyEnvironment(
        Dictionary<string, string?> source,
        ProcessStartInfo target,
        string name)
    {
        if (source.TryGetValue(name, out string? value) && !string.IsNullOrEmpty(value))
        {
            target.Environment[name] = value;
        }
    }

    private void ResetProcess()
    {
        ready = false;
        Process? previous = process;
        process = null;
        output = null;
        stderrLimit = null;
        if (previous is null)
        {
            return;
        }

        TryKill(previous);
        previous.Dispose();
    }

    private static void TryKill(Process candidate)
    {
        try
        {
            if (!candidate.HasExited)
            {
                candidate.Kill(entireProcessTree: true);
                candidate.WaitForExit(1000);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // The process exited concurrently or could not be terminated further.
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private sealed record WorkerRequest(
        string ProtocolVersion,
        string RequestId,
        string Operation,
        string? Language,
        string? RelativePath,
        string? SourceText);

    private sealed record WorkerResponse(
        string? ProtocolVersion,
        string? RequestId,
        string? Status,
        string? NodeVersion,
        string? TypeScriptVersion,
        IReadOnlyList<OutlineItem>? Items,
        bool? ItemsTruncated,
        IReadOnlyList<StructuralChunk>? Chunks,
        bool? ChunksTruncated,
        int? SyntaxDiagnosticCount);

    private sealed class BoundedLineReader(Stream stream)
    {
        private readonly byte[] buffer = new byte[16 * 1024];
        private int offset;
        private int count;

        public async Task<string> ReadAsync(int maximumBytes, CancellationToken cancellationToken)
        {
            using MemoryStream collected = new(Math.Min(maximumBytes, 64 * 1024));
            while (true)
            {
                if (offset == count)
                {
                    count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    offset = 0;
                    if (count == 0)
                    {
                        throw new EndOfStreamException("Worker stdout ended before a protocol response.");
                    }
                }

                int newline = Array.IndexOf(buffer, (byte)'\n', offset, count - offset);
                int end = newline >= 0 ? newline : count;
                int length = end - offset;
                if (collected.Length + length > maximumBytes)
                {
                    throw new BoundedLineException();
                }

                collected.Write(buffer, offset, length);
                offset = newline >= 0 ? newline + 1 : count;
                if (newline < 0)
                {
                    continue;
                }

                byte[] bytes = collected.ToArray();
                int decodedLength = bytes.Length > 0 && bytes[^1] == (byte)'\r'
                    ? bytes.Length - 1
                    : bytes.Length;
                if (decodedLength == 0)
                {
                    throw new BoundedLineException();
                }

                return StrictUtf8.GetString(bytes, 0, decodedLength);
            }
        }
    }

    private sealed class BoundedLineException : Exception
    {
    }
}
