using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Indexing;

public sealed class FindDefinitionService(
    RepositoryScope repository,
    IEnumerable<IStructuralChunkProvider> structuralProviders,
    IndexBuildLimits? configuredLimits = null)
{
    private readonly IReadOnlyList<IStructuralChunkProvider> providers = structuralProviders
        .GroupBy(provider => provider.Id, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(provider => provider.Id, StringComparer.Ordinal)
        .ToArray();
    private readonly IndexBuildLimits limits = configuredLimits ?? IndexBuildLimits.Default;

    public async Task<ToolResponse<FindDefinitionData>> FindAsync(
        string? name,
        string? kind,
        string? container,
        string? path,
        int? requestedMaximumResults,
        CancellationToken cancellationToken)
    {
        if (!repository.IsReady)
        {
            return Error(
                ContractValues.ErrorRepositoryRootRequired,
                "Definition lookup requires an explicit valid --root path.",
                "Restart Sanjaya with --root <path>.");
        }

        HashSet<string> definitionProviderIds = providers
            .Where(IsCSharpDefinitionProvider)
            .Select(provider => provider.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (definitionProviderIds.Count == 0)
        {
            return Error(
                ContractValues.ErrorDefinitionProviderUnavailable,
                "No C# syntax-definition provider is available.");
        }

        string normalizedName = name?.Trim() ?? string.Empty;
        if (!IsBoundedSingleLine(normalizedName, DefinitionLookupLimits.MaximumNameCharacters))
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                $"name must be a single line containing 1 to {DefinitionLookupLimits.MaximumNameCharacters} characters.");
        }

        string? normalizedKind = NormalizeOptional(kind);
        if (kind is not null
            && (!IsBoundedSingleLine(normalizedKind, DefinitionLookupLimits.MaximumKindCharacters)
                || !DefinitionLookupLimits.SupportedCSharpKinds.Contains(normalizedKind, StringComparer.Ordinal)))
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                $"kind must be one of: {string.Join(", ", DefinitionLookupLimits.SupportedCSharpKinds)}.");
        }

        string? normalizedContainer = NormalizeOptional(container);
        if (container is not null
            && !IsBoundedSingleLine(normalizedContainer, DefinitionLookupLimits.MaximumContainerCharacters))
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                $"container must be a single line containing 1 to {DefinitionLookupLimits.MaximumContainerCharacters} characters.");
        }

        int maximumResults = requestedMaximumResults ?? DefinitionLookupLimits.DefaultResults;
        if (maximumResults is < 1 or > DefinitionLookupLimits.MaximumResults)
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                $"maxResults must be between 1 and {DefinitionLookupLimits.MaximumResults}.");
        }

        string? normalizedPath = null;
        if (path is not null)
        {
            RepositoryPathResult resolved = repository.ResolveFile(path);
            if (!resolved.IsSuccess)
            {
                return PathError(resolved.Error);
            }

            normalizedPath = resolved.RelativePath!;
            if (!providers.Any(provider => definitionProviderIds.Contains(provider.Id)
                && provider.CanHandle(normalizedPath)))
            {
                return Error(
                    ContractValues.ErrorInvalidArgument,
                    "path must identify a file handled by the active C# definition provider.");
            }
        }

        try
        {
            IndexDocument document = await new FreshIndexReader(repository, providers, limits)
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false);
            IndexChunk[] candidates = document.Chunks
                .Where(chunk => definitionProviderIds.Contains(chunk.Provider)
                    && chunk.Name.Equals(normalizedName, StringComparison.Ordinal)
                    && (normalizedKind is null || chunk.Kind.Equals(normalizedKind, StringComparison.Ordinal))
                    && (normalizedContainer is null || string.Equals(
                        chunk.Container,
                        normalizedContainer,
                        StringComparison.Ordinal))
                    && (normalizedPath is null || chunk.Path.Equals(normalizedPath, StringComparison.Ordinal)))
                .OrderBy(chunk => chunk.Path, StringComparer.Ordinal)
                .ThenBy(chunk => chunk.StartLine)
                .ThenBy(chunk => chunk.EndLine)
                .ThenBy(chunk => chunk.Kind, StringComparer.Ordinal)
                .ThenBy(chunk => chunk.Name, StringComparer.Ordinal)
                .ThenBy(chunk => chunk.Id, StringComparer.Ordinal)
                .ToArray();
            DefinitionMatch[] matches = candidates.Take(maximumResults).Select(chunk => new DefinitionMatch(
                chunk.Id,
                chunk.Provider,
                chunk.Language,
                chunk.Path,
                chunk.Kind,
                chunk.Name,
                chunk.Container,
                chunk.StartLine,
                chunk.EndLine,
                IndexSnippet.Create(
                    chunk.Content,
                    [normalizedName],
                    StringComparison.Ordinal,
                    DefinitionLookupLimits.MaximumSnippetCharacters))).ToArray();
            EvidenceLocation[] evidence = matches.Select(match => new EvidenceLocation(
                match.Path,
                match.StartLine,
                match.EndLine,
                match.Name)).ToArray();
            string resolution = candidates.Length switch
            {
                0 => ContractValues.ResolutionNotFound,
                1 => ContractValues.ResolutionUnique,
                _ => ContractValues.ResolutionAmbiguous,
            };
            IndexQuality quality = IndexEvidenceQuality.Inspect(document);
            FindDefinitionData data = new(
                normalizedName,
                normalizedKind,
                normalizedContainer,
                normalizedPath,
                document.RepositoryFingerprint,
                resolution,
                matches,
                candidates.Length,
                candidates.Length > maximumResults);

            return new ToolResponse<FindDefinitionData>(
                quality.IsPartial ? ContractValues.StatusPartial : ContractValues.StatusOk,
                PublicToolNames.FindDefinition,
                "sanjaya-index",
                data,
                evidence,
                quality.Warnings);
        }
        catch (OperationCanceledException)
        {
            return Error(ContractValues.ErrorCancelled, "Definition lookup was cancelled.");
        }
        catch (IndexReadFailure failure)
        {
            return Error(failure.Code, failure.Message, Remediation(failure.Code));
        }
    }

    private static bool IsCSharpDefinitionProvider(IStructuralChunkProvider provider) =>
        provider.GetCapabilities().Any(capability =>
            capability.Capability == CapabilityKind.Definitions
            && capability.Status == CapabilityStatus.Supported
            && capability.Provider == provider.Id
            && capability.Language == "csharp");

    private static bool IsBoundedSingleLine(string? value, int maximumCharacters) =>
        value is not null
        && value.Length is >= 1
        && value.Length <= maximumCharacters
        && !value.Contains('\r')
        && !value.Contains('\n')
        && !value.Contains('\0');

    private static string? NormalizeOptional(string? value) => value?.Trim();

    private static string? Remediation(string code) => code switch
    {
        ContractValues.ErrorIndexMissing => "Run index_codebase, then retry find_definition.",
        ContractValues.ErrorIndexCorrupt => "Run index_codebase to replace the invalid index.",
        ContractValues.ErrorIndexIncompatible => "Run index_codebase with this Sanjaya version.",
        ContractValues.ErrorIndexStale => "Run index_codebase, then retry find_definition.",
        _ => null,
    };

    private static ToolResponse<FindDefinitionData> PathError(RepositoryPathError error) => error switch
    {
        RepositoryPathError.OutsideRepository => Error(
            ContractValues.ErrorPathOutsideRepository,
            "Path resolves outside the configured repository."),
        RepositoryPathError.NotFound => Error(
            ContractValues.ErrorFileNotFound,
            "File was not found in the repository."),
        RepositoryPathError.NotAFile => Error(
            ContractValues.ErrorNotAFile,
            "Path must identify one regular file."),
        _ => Error(
            ContractValues.ErrorInvalidPath,
            "Path must be a valid repository-relative file path."),
    };

    private static ToolResponse<FindDefinitionData> Error(
        string code,
        string message,
        string? remediation = null) => new(
        ContractValues.StatusError,
        PublicToolNames.FindDefinition,
        "sanjaya-index",
        null,
        [],
        [],
        new ErrorDetail(code, message, remediation));
}
