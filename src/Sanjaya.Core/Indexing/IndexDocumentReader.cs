using System.Text.Json;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Discovery;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Indexing;

internal sealed class IndexDocumentReader(
    RepositoryScope repository,
    IReadOnlyList<IStructuralChunkProvider> providers)
{
    public async Task<IndexDocument> ReadCompatibleAsync(CancellationToken cancellationToken)
    {
        string indexDirectory = Path.Combine(repository.CanonicalRoot!, ".sanjaya");
        string indexPath = Path.Combine(indexDirectory, "index-v1.json");
        ValidateStoragePath(indexDirectory, indexPath);
        byte[] bytes = await ReadBoundedAsync(indexPath, cancellationToken).ConfigureAwait(false);

        JsonElement root;
        try
        {
            using JsonDocument parsed = JsonDocument.Parse(bytes);
            root = parsed.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Corrupt("The structural index is not valid JSON.");
        }

        ValidateOwnershipAndFormat(root);
        IndexDocument document;
        try
        {
            document = JsonSerializer.Deserialize<IndexDocument>(bytes, IndexSerialization.Options)
                ?? throw Corrupt("The structural index document is empty.");
        }
        catch (JsonException)
        {
            throw Corrupt("The structural index does not match the v1 schema.");
        }

        ValidateDocument(document, bytes);
        return document;
    }

    private static void ValidateStoragePath(string indexDirectory, string indexPath)
    {
        DirectoryInfo directory = new(indexDirectory);
        if (directory.LinkTarget is not null
            || (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0))
        {
            throw Corrupt("The .sanjaya index directory must not be a symbolic link or reparse point.");
        }

        if (!directory.Exists)
        {
            throw Missing();
        }

        FileInfo file = new(indexPath);
        if (file.LinkTarget is not null
            || (file.Exists && (file.Attributes & FileAttributes.ReparsePoint) != 0)
            || Directory.Exists(indexPath))
        {
            throw Corrupt("The structural index must be a regular nonsymlink file.");
        }

        if (!file.Exists)
        {
            throw Missing();
        }

        if (file.Length <= 0 || file.Length > IndexBuildLimits.Default.MaximumOutputBytes)
        {
            throw Corrupt("The structural index is empty or exceeds the v1 size limit.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        string indexPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = new(
                indexPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length <= 0 || stream.Length > IndexBuildLimits.Default.MaximumOutputBytes)
            {
                throw Corrupt("The structural index is empty or exceeds the v1 size limit.");
            }

            byte[] bytes = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
            int offset = 0;
            while (offset < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                offset += read;
            }

            if (offset != bytes.Length || stream.ReadByte() != -1)
            {
                throw new IndexReadFailure(
                    ContractValues.ErrorIndexStateUnverifiable,
                    "The structural index changed while it was being read.");
            }

            return bytes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IndexReadFailure)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IndexReadFailure(
                ContractValues.ErrorIndexStateUnverifiable,
                "The structural index could not be read safely.");
        }
    }

    private static void ValidateOwnershipAndFormat(JsonElement root)
    {
        bool owned = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("owner", out JsonElement owner)
            && owner.ValueKind == JsonValueKind.String
            && owner.GetString() == "sanjaya"
            && root.TryGetProperty("producer", out JsonElement producer)
            && producer.ValueKind == JsonValueKind.Object
            && producer.TryGetProperty("name", out JsonElement name)
            && name.ValueKind == JsonValueKind.String
            && name.GetString() == "sanjaya";
        if (!owned)
        {
            throw Corrupt("The index target is not recognized as Sanjaya-owned data.");
        }

        string? formatVersion = root.TryGetProperty("formatVersion", out JsonElement format)
            && format.ValueKind == JsonValueKind.String
            ? format.GetString()
            : null;
        if (formatVersion != IndexCodebaseService.FormatVersion)
        {
            throw Incompatible("The structural index format is not supported by this Sanjaya build.");
        }
    }

    private void ValidateDocument(IndexDocument document, byte[] originalBytes)
    {
        if (document.Owner != "sanjaya"
            || document.FormatVersion != IndexCodebaseService.FormatVersion
            || document.Producer is null
            || document.Producer.Name != "sanjaya"
            || string.IsNullOrWhiteSpace(document.Producer.Version)
            || document.Providers is null
            || document.Files is null
            || document.Chunks is null
            || document.Providers.Any(provider => provider is null)
            || document.Files.Any(file => file is null)
            || document.Chunks.Any(chunk => chunk is null))
        {
            throw Corrupt("The structural index is missing required v1 metadata.");
        }

        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(document, IndexSerialization.Options);
        if (originalBytes.Length != canonical.Length + 1
            || originalBytes[^1] != (byte)'\n'
            || !originalBytes.AsSpan(0, canonical.Length).SequenceEqual(canonical))
        {
            throw Corrupt("The structural index is not in canonical v1 form.");
        }

        ValidateProviders(document.Providers);
        IndexProvider[] activeProviders = providers.Select(provider => new IndexProvider(
            provider.Id,
            provider.ContractVersion,
            provider.Languages.Order(StringComparer.Ordinal).ToArray())).ToArray();
        if (!ProvidersMatch(document.Providers, activeProviders))
        {
            throw Incompatible("The structural index provider contracts do not match this runtime.");
        }

        if (document.Files.Count > IndexBuildLimits.Default.MaximumEligibleFiles
            || document.Chunks.Count > IndexBuildLimits.Default.MaximumChunks
            || !IsHash(document.RepositoryFingerprint))
        {
            throw Corrupt("The structural index exceeds v1 bounds or has an invalid fingerprint.");
        }

        Dictionary<string, IndexProvider> providerById = document.Providers.ToDictionary(
            provider => provider.Id,
            StringComparer.Ordinal);
        ValidateFiles(document.Files, providerById);
        ValidateChunks(document.Chunks, document.Files, providerById);
        string expectedFingerprint = IndexFingerprint.CreateRepository(document.Providers, document.Files);
        if (document.RepositoryFingerprint != expectedFingerprint)
        {
            throw Corrupt("The structural index repository fingerprint is invalid.");
        }
    }

    private static bool ProvidersMatch(
        IReadOnlyList<IndexProvider> indexed,
        IReadOnlyList<IndexProvider> active) =>
        indexed.Count == active.Count
        && indexed.Zip(active).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.ContractVersion == pair.Second.ContractVersion
            && pair.First.Languages.SequenceEqual(pair.Second.Languages, StringComparer.Ordinal));

    private static void ValidateProviders(IReadOnlyList<IndexProvider> indexProviders)
    {
        if (!indexProviders.SequenceEqual(indexProviders.OrderBy(provider => provider.Id, StringComparer.Ordinal))
            || indexProviders.Select(provider => provider.Id).Distinct(StringComparer.Ordinal).Count() != indexProviders.Count
            || indexProviders.Any(provider => string.IsNullOrWhiteSpace(provider.Id)
                || string.IsNullOrWhiteSpace(provider.ContractVersion)
                || provider.Languages is null
                || provider.Languages.Count == 0
                || provider.Languages.Any(string.IsNullOrWhiteSpace)
                || !provider.Languages.SequenceEqual(provider.Languages.Order(StringComparer.Ordinal))))
        {
            throw Corrupt("The structural index provider metadata is invalid or noncanonical.");
        }
    }

    private static void ValidateFiles(
        IReadOnlyList<IndexFile> files,
        Dictionary<string, IndexProvider> providerById)
    {
        if (!files.SequenceEqual(files.OrderBy(file => file.Path, StringComparer.Ordinal))
            || files.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != files.Count
            || files.Sum(file => (long)file.ByteCount) > IndexBuildLimits.Default.MaximumSourceBytes
            || files.Any(file => !IsRelativePath(file.Path)
                || string.IsNullOrWhiteSpace(file.Provider)
                || !providerById.ContainsKey(file.Provider)
                || !IsHash(file.ContentHash)
                || file.ByteCount is < 0 or > DiscoveryLimits.MaximumFileBytes
                || file.SyntaxDiagnosticCount < 0))
        {
            throw Corrupt("The structural index file metadata is invalid or noncanonical.");
        }
    }

    private static void ValidateChunks(
        IReadOnlyList<IndexChunk> chunks,
        IReadOnlyList<IndexFile> files,
        Dictionary<string, IndexProvider> providerById)
    {
        Dictionary<string, IndexFile> fileByPath = files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        IndexChunk[] ordered = chunks
            .OrderBy(chunk => chunk.Path, StringComparer.Ordinal)
            .ThenBy(chunk => chunk.StartLine)
            .ThenBy(chunk => chunk.EndLine)
            .ThenBy(chunk => chunk.Kind, StringComparer.Ordinal)
            .ThenBy(chunk => chunk.Name, StringComparer.Ordinal)
            .ThenBy(chunk => chunk.Id, StringComparer.Ordinal)
            .ToArray();
        if (!chunks.SequenceEqual(ordered)
            || chunks.Select(chunk => chunk.Id).Distinct(StringComparer.Ordinal).Count() != chunks.Count)
        {
            throw Corrupt("The structural index chunks are duplicated or noncanonical.");
        }

        foreach (IndexChunk chunk in chunks)
        {
            if (!IsHash(chunk.Id)
                || !IsRelativePath(chunk.Path)
                || string.IsNullOrWhiteSpace(chunk.Kind)
                || string.IsNullOrWhiteSpace(chunk.Name)
                || chunk.StartLine < 1
                || chunk.EndLine < chunk.StartLine
                || chunk.Content is null
                || chunk.Content.Length > DiscoveryLimits.MaximumChunkCharacters
                || chunk.Content.Contains('\0')
                || string.IsNullOrWhiteSpace(chunk.Provider)
                || string.IsNullOrWhiteSpace(chunk.Language)
                || !providerById.TryGetValue(chunk.Provider, out IndexProvider? provider)
                || !provider.Languages.Contains(chunk.Language, StringComparer.Ordinal)
                || !fileByPath.TryGetValue(chunk.Path, out IndexFile? file)
                || file.Provider != chunk.Provider
                || chunk.Id != IndexFingerprint.CreateChunkId(chunk, provider.ContractVersion))
            {
                throw Corrupt("The structural index contains an invalid chunk record.");
            }
        }
    }

    private static bool IsRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path)
            || path.Contains('\\')
            || path.Contains('\0'))
        {
            return false;
        }

        string[] segments = path.Split('/');
        return segments.Length > 0 && segments.All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool IsHash(string? value)
    {
        if (value is null
            || value.Length != 71
            || !value.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char character in value.AsSpan(7))
        {
            if (character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static IndexReadFailure Missing() => new(
        ContractValues.ErrorIndexMissing,
        "The structural index is missing. Run index_codebase first.");

    private static IndexReadFailure Corrupt(string message) => new(
        ContractValues.ErrorIndexCorrupt,
        message);

    private static IndexReadFailure Incompatible(string message) => new(
        ContractValues.ErrorIndexIncompatible,
        message);
}

internal sealed class IndexReadFailure(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
