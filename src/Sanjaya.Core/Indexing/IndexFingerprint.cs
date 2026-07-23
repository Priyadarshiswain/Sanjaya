using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sanjaya.Core.Providers;

namespace Sanjaya.Core.Indexing;

internal static class IndexFingerprint
{
    public static string CreateRepository(
        IReadOnlyList<IndexProvider> providers,
        IEnumerable<IndexSourceFile> files)
    {
        IEnumerable<string> fileParts = files.Select(
            file => $"file:{file.Provider.Id}:{file.RelativePath}:{file.ContentHash}");
        return CreateRepository(providers, fileParts);
    }

    public static string CreateRepository(
        IReadOnlyList<IndexProvider> providers,
        IEnumerable<IndexFile> files)
    {
        IEnumerable<string> fileParts = files.Select(
            file => $"file:{file.Provider}:{file.Path}:{file.ContentHash}");
        return CreateRepository(providers, fileParts);
    }

    public static string CreateChunkId(
        IStructuralChunkProvider provider,
        string relativePath,
        StructuralChunk chunk) => Hash(
            provider.Id,
            provider.ContractVersion,
            relativePath,
            chunk.Kind,
            chunk.Name,
            chunk.Container ?? string.Empty,
            chunk.StartLine.ToString(CultureInfo.InvariantCulture),
            chunk.EndLine.ToString(CultureInfo.InvariantCulture),
            Hash(chunk.Content));

    public static string CreateChunkId(IndexChunk chunk, string providerContractVersion) => Hash(
        chunk.Provider,
        providerContractVersion,
        chunk.Path,
        chunk.Kind,
        chunk.Name,
        chunk.Container ?? string.Empty,
        chunk.StartLine.ToString(CultureInfo.InvariantCulture),
        chunk.EndLine.ToString(CultureInfo.InvariantCulture),
        Hash(chunk.Content));

    private static string CreateRepository(
        IReadOnlyList<IndexProvider> providers,
        IEnumerable<string> fileParts)
    {
        IEnumerable<string> providerParts = providers.Select(
            provider => $"provider:{provider.Id}:{provider.ContractVersion}:{string.Join(',', provider.Languages)}");
        return Hash(providerParts.Concat(fileParts.Order(StringComparer.Ordinal)).ToArray());
    }

    private static string Hash(params string[] parts)
    {
        string canonical = string.Join('\0', parts);
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
    }
}
