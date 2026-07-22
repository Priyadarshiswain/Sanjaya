using System.Text.Json;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Discovery;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Indexing;

public sealed class IndexCodebaseService(
    RepositoryScope repository,
    IEnumerable<IStructuralChunkProvider> structuralProviders,
    string producerVersion,
    IndexBuildLimits? configuredLimits = null)
{
    public const string FormatVersion = "1";
    public const string RelativeIndexPath = ".sanjaya/index-v1.json";

    private readonly IReadOnlyList<IStructuralChunkProvider> providers = structuralProviders
        .GroupBy(provider => provider.Id, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(provider => provider.Id, StringComparer.Ordinal)
        .ToArray();
    private readonly IndexBuildLimits limits = configuredLimits ?? IndexBuildLimits.Default;

    public async Task<ToolResponse<IndexCodebaseData>> RebuildAsync(CancellationToken cancellationToken)
    {
        if (!repository.IsReady)
        {
            return Error(
                repository.ConfigurationReason!,
                repository.ConfigurationError!,
                repository.ConfigurationRemediation);
        }

        if (providers.Count == 0)
        {
            return Error(
                ContractValues.ErrorStructuralProviderUnavailable,
                "No structural provider is available for indexing.");
        }

        string indexDirectory = Path.Combine(repository.CanonicalRoot!, ".sanjaya");
        string targetPath = Path.Combine(indexDirectory, "index-v1.json");
        string lockPath = Path.Combine(indexDirectory, "index.lock");
        string? temporaryPath = null;

        try
        {
            PrepareIndexDirectory(indexDirectory);
            await using FileStream indexLock = AcquireLock(lockPath);
            ExistingIndexInfo? existing = ReadExistingTarget(targetPath);
            BuildResult build = await BuildAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(build.Document, IndexSerialization.Options);
            if (payload.Length + 1 > limits.MaximumOutputBytes)
            {
                throw Failure(
                    ContractValues.ErrorIndexOutputLimit,
                    $"Serialized index exceeds the {limits.MaximumOutputBytes}-byte output limit.");
            }

            temporaryPath = CreateTemporaryPath(indexDirectory);
            await WriteTemporaryAsync(temporaryPath, payload, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ReadExistingTarget(targetPath);
            File.Move(temporaryPath, targetPath, overwrite: true);
            temporaryPath = null;

            List<string> warnings = build.Counters.CreateWarnings(repository.CanonicalRoot!);
            Add(warnings, "syntax_diagnostics_recovered", build.SyntaxDiagnosticCount);
            Add(warnings, "chunk_content_truncated", build.TruncatedChunkCount);
            bool partial = build.SyntaxDiagnosticCount > 0 || build.TruncatedChunkCount > 0;
            IndexCodebaseData data = new(
                FormatVersion,
                "ready",
                RelativeIndexPath,
                build.Document.RepositoryFingerprint,
                ClassifyPreviousIndex(existing, build.Document),
                build.ProviderSummaries,
                build.Document.Files.Count,
                build.Counters.UnsupportedFiles,
                build.Document.Chunks.Count,
                build.SourceBytes,
                build.SyntaxDiagnosticCount,
                build.TruncatedChunkCount);

            return new ToolResponse<IndexCodebaseData>(
                partial ? ContractValues.StatusPartial : ContractValues.StatusOk,
                PublicToolNames.IndexCodebase,
                "sanjaya-index",
                data,
                [],
                warnings);
        }
        catch (OperationCanceledException)
        {
            return Error(
                ContractValues.ErrorCancelled,
                "Index rebuild was cancelled; any previous index was preserved.");
        }
        catch (IndexBuildFailure failure)
        {
            return Error(failure.Code, failure.Message);
        }
        catch (IndexSourceScanFailure failure)
        {
            return Error(failure.Code, failure.Message);
        }
        catch (StructuralProviderException exception)
        {
            return ProviderError(exception.Failure);
        }
        catch (UnauthorizedAccessException)
        {
            return Error(
                ContractValues.ErrorIndexWriteFailed,
                "Index storage is inaccessible; any previous index was preserved.");
        }
        catch (IOException)
        {
            return Error(
                ContractValues.ErrorIndexWriteFailed,
                "Index storage failed; any previous index was preserved.");
        }
        finally
        {
            DeleteOwnedTemporary(temporaryPath);
        }
    }

    private async Task<BuildResult> BuildAsync(CancellationToken cancellationToken)
    {
        IndexSourceSnapshot snapshot = await new IndexSourceScanner(repository, providers, limits)
            .CaptureAsync(includeText: true, cancellationToken)
            .ConfigureAwait(false);
        List<IndexFile> files = [];
        List<IndexChunk> chunks = [];
        int syntaxDiagnosticCount = 0;
        int truncatedChunkCount = 0;

        foreach (IndexSourceFile source in snapshot.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StructuralChunkAnalysis analysis = source.Provider.AnalyzeChunks(
                source.RelativePath,
                source.Text!,
                cancellationToken);
            if (analysis.ChunksTruncated)
            {
                throw Failure(
                    ContractValues.ErrorIndexChunkLimit,
                    "A source file exceeded its structural chunk limit.");
            }

            if (chunks.Count + analysis.Chunks.Count > limits.MaximumChunks)
            {
                throw Failure(
                    ContractValues.ErrorIndexChunkLimit,
                    $"Structural content exceeds the {limits.MaximumChunks}-chunk index limit.");
            }

            syntaxDiagnosticCount += analysis.SyntaxDiagnosticCount;
            truncatedChunkCount += analysis.Chunks.Count(chunk => chunk.ContentTruncated);
            files.Add(new IndexFile(
                source.RelativePath,
                source.Provider.Id,
                source.ContentHash,
                source.ByteCount,
                analysis.SyntaxDiagnosticCount));
            string language = source.Provider.Languages.Order(StringComparer.Ordinal).First();
            chunks.AddRange(analysis.Chunks.Select(chunk => new IndexChunk(
                IndexFingerprint.CreateChunkId(source.Provider, source.RelativePath, chunk),
                source.Provider.Id,
                language,
                source.RelativePath,
                chunk.Kind,
                chunk.Name,
                chunk.Container,
                chunk.StartLine,
                chunk.EndLine,
                chunk.Content,
                chunk.ContentTruncated)));
        }

        IndexFile[] orderedFiles = files
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();
        IndexChunk[] orderedChunks = chunks
            .OrderBy(chunk => chunk.Path, StringComparer.Ordinal)
            .ThenBy(chunk => chunk.StartLine)
            .ThenBy(chunk => chunk.EndLine)
            .ThenBy(chunk => chunk.Kind, StringComparer.Ordinal)
            .ThenBy(chunk => chunk.Name, StringComparer.Ordinal)
            .ThenBy(chunk => chunk.Id, StringComparer.Ordinal)
            .ToArray();
        IndexProvider[] indexProviders = providers.Select(provider => new IndexProvider(
            provider.Id,
            provider.ContractVersion,
            provider.Languages.Order(StringComparer.Ordinal).ToArray())).ToArray();
        string fingerprint = IndexFingerprint.CreateRepository(indexProviders, orderedFiles);
        IndexDocument document = new(
            "sanjaya",
            FormatVersion,
            new IndexProducer("sanjaya", producerVersion),
            fingerprint,
            indexProviders,
            orderedFiles,
            orderedChunks);
        IndexedProviderSummary[] summaries = indexProviders.Select(provider => new IndexedProviderSummary(
            provider.Id,
            provider.ContractVersion,
            provider.Languages,
            orderedFiles.Count(file => file.Provider == provider.Id),
            orderedChunks.Count(chunk => chunk.Provider == provider.Id))).ToArray();

        return new BuildResult(
            document,
            summaries,
            snapshot.SourceBytes,
            syntaxDiagnosticCount,
            truncatedChunkCount,
            snapshot.Counters);
    }

    private static void PrepareIndexDirectory(string indexDirectory)
    {
        DirectoryInfo directory = new(indexDirectory);
        if (directory.LinkTarget is not null)
        {
            throw Failure(
                ContractValues.ErrorIndexPathConflict,
                "The .sanjaya directory must not be a symbolic link or reparse point.");
        }

        if (File.Exists(indexDirectory))
        {
            throw Failure(
                ContractValues.ErrorIndexPathConflict,
                "The .sanjaya path exists but is not a directory.");
        }

        if (Directory.Exists(indexDirectory))
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw Failure(
                    ContractValues.ErrorIndexPathConflict,
                    "The .sanjaya directory must not be a symbolic link or reparse point.");
            }

            return;
        }

        Directory.CreateDirectory(indexDirectory);
    }

    private static FileStream AcquireLock(string lockPath)
    {
        FileInfo lockFile = new(lockPath);
        if (lockFile.LinkTarget is not null)
        {
            throw Failure(
                ContractValues.ErrorIndexPathConflict,
                "The index lock path must not be a symbolic link or reparse point.");
        }

        bool lockAlreadyExists = lockFile.Exists;
        if (Directory.Exists(lockPath))
        {
            throw Failure(
                ContractValues.ErrorIndexPathConflict,
                "The index lock path exists but is not a regular file.");
        }

        if (lockAlreadyExists)
        {
            if ((lockFile.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw Failure(
                    ContractValues.ErrorIndexPathConflict,
                    "The index lock path must not be a symbolic link or reparse point.");
            }
        }

        try
        {
            return new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
        }
        catch (IOException) when (File.Exists(lockPath))
        {
            throw Failure(
                ContractValues.ErrorIndexBusy,
                "Another Sanjaya process is rebuilding this repository index.");
        }
        catch (IOException)
        {
            throw Failure(
                ContractValues.ErrorIndexWriteFailed,
                "Index lock storage could not be created.");
        }
    }

    private static ExistingIndexInfo? ReadExistingTarget(string targetPath)
    {
        FileInfo target = new(targetPath);
        if (target.LinkTarget is not null)
        {
            throw Failure(
                ContractValues.ErrorIndexPathConflict,
                "The index target must not be a symbolic link or reparse point.");
        }

        if (Directory.Exists(targetPath))
        {
            throw Failure(
                ContractValues.ErrorIndexPathConflict,
                "The index target exists but is not a regular file.");
        }

        if (!target.Exists)
        {
            return null;
        }

        if ((target.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Failure(
                ContractValues.ErrorIndexPathConflict,
                "The index target must not be a symbolic link or reparse point.");
        }

        if (target.Length > IndexBuildLimits.Default.MaximumOutputBytes)
        {
            throw Failure(
                ContractValues.ErrorIndexPathConflict,
                "The existing index target exceeds the recognized index size limit.");
        }

        try
        {
            using FileStream stream = new(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            bool recognized = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("owner", out JsonElement owner)
                && owner.GetString() == "sanjaya"
                && root.TryGetProperty("formatVersion", out JsonElement version)
                && !string.IsNullOrWhiteSpace(version.GetString())
                && root.TryGetProperty("producer", out JsonElement producer)
                && producer.ValueKind == JsonValueKind.Object
                && producer.TryGetProperty("name", out JsonElement producerName)
                && producerName.GetString() == "sanjaya";
            if (!recognized)
            {
                throw Failure(
                    ContractValues.ErrorIndexPathConflict,
                    "The existing index target is not recognized as Sanjaya-owned data.");
            }

            string fingerprint = root.TryGetProperty("repositoryFingerprint", out JsonElement fingerprintElement)
                ? fingerprintElement.GetString() ?? string.Empty
                : string.Empty;
            List<string> providerContracts = [];
            if (root.TryGetProperty("providers", out JsonElement providersElement)
                && providersElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement provider in providersElement.EnumerateArray())
                {
                    string id = provider.TryGetProperty("id", out JsonElement idElement)
                        ? idElement.GetString() ?? string.Empty
                        : string.Empty;
                    string contractVersion = provider.TryGetProperty("contractVersion", out JsonElement contractElement)
                        ? contractElement.GetString() ?? string.Empty
                        : string.Empty;
                    providerContracts.Add($"{id}:{contractVersion}");
                }
            }

            return new ExistingIndexInfo(
                root.GetProperty("formatVersion").GetString()!,
                fingerprint,
                providerContracts.Order(StringComparer.Ordinal).ToArray());
        }
        catch (JsonException)
        {
            throw Failure(
                ContractValues.ErrorIndexPathConflict,
                "The existing index target is not recognized as Sanjaya-owned data.");
        }
    }

    private static string ClassifyPreviousIndex(ExistingIndexInfo? existing, IndexDocument current)
    {
        if (existing is null)
        {
            return "missing";
        }

        string[] currentProviders = current.Providers
            .Select(provider => $"{provider.Id}:{provider.ContractVersion}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (existing.FormatVersion != FormatVersion
            || !existing.ProviderContracts.SequenceEqual(currentProviders, StringComparer.Ordinal))
        {
            return "incompatible";
        }

        return existing.RepositoryFingerprint == current.RepositoryFingerprint
            ? "current"
            : "stale";
    }

    private static string CreateTemporaryPath(string indexDirectory)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string candidate = Path.Combine(indexDirectory, $"index-v1.{Path.GetRandomFileName()}.tmp");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw Failure(
            ContractValues.ErrorIndexWriteFailed,
            "Could not allocate temporary index storage.");
    }

    private static async Task WriteTemporaryAsync(
        string temporaryPath,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void DeleteOwnedTemporary(string? temporaryPath)
    {
        if (temporaryPath is null)
        {
            return;
        }

        try
        {
            File.Delete(temporaryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed cleanup must not replace the primary bounded tool result.
        }
    }

    private static void Add(List<string> warnings, string code, int count)
    {
        if (count > 0)
        {
            warnings.Add($"{code}:{count}");
        }
    }

    private static IndexBuildFailure Failure(string code, string message) => new(code, message);

    private static ToolResponse<IndexCodebaseData> Error(
        string code,
        string message,
        string? remediation = null) =>
        new(
            ContractValues.StatusError,
            PublicToolNames.IndexCodebase,
            "sanjaya-index",
            null,
            [],
            [],
            new ErrorDetail(code, message, remediation));

    private static ToolResponse<IndexCodebaseData> ProviderError(
        StructuralProviderFailure failure) => failure switch
        {
            StructuralProviderFailure.Unavailable => Error(
                ContractValues.ErrorStructuralProviderUnavailable,
                "A structural provider runtime is unavailable.",
                "Restart Sanjaya with a supported runtime."),
            StructuralProviderFailure.TimedOut => Error(
                ContractValues.ErrorStructuralProviderTimeout,
                "A structural provider exceeded its analysis time limit."),
            StructuralProviderFailure.OutputLimit => Error(
                ContractValues.ErrorStructuralProviderOutputLimit,
                "A structural provider exceeded its bounded output limit."),
            _ => Error(
                ContractValues.ErrorStructuralProviderInvalidOutput,
                "A structural provider returned invalid protocol output."),
        };

    private sealed record ExistingIndexInfo(
        string FormatVersion,
        string RepositoryFingerprint,
        IReadOnlyList<string> ProviderContracts);

    private sealed record BuildResult(
        IndexDocument Document,
        IReadOnlyList<IndexedProviderSummary> ProviderSummaries,
        long SourceBytes,
        int SyntaxDiagnosticCount,
        int TruncatedChunkCount,
        IndexScanCounters Counters);

    private sealed class IndexBuildFailure(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}
