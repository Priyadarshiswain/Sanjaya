using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Indexing;

public sealed class FindReferencesService(
    RepositoryScope repository,
    IEnumerable<IStructuralChunkProvider> structuralProviders,
    IEnumerable<IReferenceProvider> configuredReferenceProviders,
    IndexBuildLimits? configuredLimits = null)
{
    private readonly IReadOnlyList<IStructuralChunkProvider> structural = structuralProviders
        .GroupBy(provider => provider.Id, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(provider => provider.Id, StringComparer.Ordinal)
        .ToArray();
    private readonly Dictionary<string, IReferenceProvider> references = configuredReferenceProviders
        .Where(IsCSharpReferenceProvider)
        .GroupBy(provider => provider.Id, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    private readonly IndexBuildLimits limits = configuredLimits ?? IndexBuildLimits.Default;

    public async Task<ToolResponse<FindReferencesData>> FindAsync(
        string? name,
        string? path,
        int? requestedMaximumResults,
        CancellationToken cancellationToken)
    {
        if (!repository.IsReady)
        {
            return Error(
                repository.ConfigurationReason!,
                repository.ConfigurationError!,
                repository.ConfigurationRemediation);
        }

        if (references.Count == 0)
        {
            return Error(
                ContractValues.ErrorReferenceProviderUnavailable,
                "No C# syntax-reference provider is available.");
        }

        string normalizedName = name?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > ReferenceLookupLimits.MaximumNameCharacters
            || normalizedName.Any(char.IsWhiteSpace)
            || normalizedName.Contains('\0')
            || !references.Values.Any(provider => provider.IsValidName(normalizedName)))
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                $"name must be one identifier containing 1 to {ReferenceLookupLimits.MaximumNameCharacters} characters.");
        }

        int maximumResults = requestedMaximumResults ?? ReferenceLookupLimits.DefaultResults;
        if (maximumResults is < 1 or > ReferenceLookupLimits.MaximumResults)
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                $"maxResults must be between 1 and {ReferenceLookupLimits.MaximumResults}.");
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
            if (!references.Values.Any(provider => provider.CanHandle(normalizedPath)))
            {
                return Error(
                    ContractValues.ErrorInvalidArgument,
                    "path must identify a file handled by the active C# reference provider.");
            }
        }

        try
        {
            FreshIndexState state = await new FreshIndexReader(repository, structural, limits)
                .ReadWithSourcesAsync(includeText: true, cancellationToken)
                .ConfigureAwait(false);
            List<ReferenceMatch> candidates = [];
            int filesScanned = 0;
            int syntaxDiagnostics = 0;
            foreach (IndexSourceFile source in state.Sources.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (normalizedPath is not null && source.RelativePath != normalizedPath)
                {
                    continue;
                }

                if (!references.TryGetValue(source.Provider.Id, out IReferenceProvider? provider)
                    || !provider.CanHandle(source.RelativePath))
                {
                    continue;
                }

                filesScanned++;
                ReferenceAnalysis analysis = provider.AnalyzeReferences(
                    source.RelativePath,
                    source.Text!,
                    normalizedName,
                    cancellationToken);
                if (analysis.MatchesTruncated)
                {
                    return Error(
                        ContractValues.ErrorReferenceLimit,
                        $"A source file exceeded the {ReferenceLookupLimits.MaximumMatchesPerFile}-candidate reference limit.");
                }

                if (candidates.Count + analysis.Matches.Count > ReferenceLookupLimits.MaximumTotalMatches)
                {
                    return Error(
                        ContractValues.ErrorReferenceLimit,
                        $"Reference candidates exceed the {ReferenceLookupLimits.MaximumTotalMatches}-match total limit.");
                }

                syntaxDiagnostics += analysis.SyntaxDiagnosticCount;
                candidates.AddRange(analysis.Matches.Select(match => new ReferenceMatch(
                    ReferenceLookupLimits.Classification,
                    source.RelativePath,
                    match.SyntaxKind,
                    match.EnclosingKind,
                    match.EnclosingName,
                    match.EnclosingContainer,
                    match.StartLine,
                    match.StartColumn,
                    match.EndLine,
                    match.EndColumn,
                    match.Snippet)));
            }

            ReferenceMatch[] ordered = candidates
                .OrderBy(match => match.Path, StringComparer.Ordinal)
                .ThenBy(match => match.StartLine)
                .ThenBy(match => match.StartColumn)
                .ThenBy(match => match.EndLine)
                .ThenBy(match => match.EndColumn)
                .ThenBy(match => match.SyntaxKind, StringComparer.Ordinal)
                .ToArray();
            ReferenceMatch[] matches = ordered.Take(maximumResults).ToArray();
            EvidenceLocation[] evidence = matches.Select(match => new EvidenceLocation(
                match.Path,
                match.StartLine,
                match.EndLine,
                match.EnclosingName)).ToArray();
            List<string> warnings = [];
            if (syntaxDiagnostics > 0)
            {
                warnings.Add($"csharp_syntax_diagnostics_recovered:{syntaxDiagnostics}");
            }

            FindReferencesData data = new(
                normalizedName,
                normalizedPath,
                state.Document.RepositoryFingerprint,
                ReferenceLookupLimits.Classification,
                matches,
                ordered.Length,
                ordered.Length > maximumResults,
                filesScanned,
                syntaxDiagnostics);
            return new ToolResponse<FindReferencesData>(
                syntaxDiagnostics > 0 ? ContractValues.StatusPartial : ContractValues.StatusOk,
                PublicToolNames.FindReferences,
                "sanjaya-index",
                data,
                evidence,
                warnings);
        }
        catch (OperationCanceledException)
        {
            return Error(ContractValues.ErrorCancelled, "Reference lookup was cancelled.");
        }
        catch (IndexReadFailure failure)
        {
            return Error(failure.Code, failure.Message, Remediation(failure.Code));
        }
    }

    private static bool IsCSharpReferenceProvider(IReferenceProvider provider) =>
        provider.GetCapabilities().Any(capability =>
            capability.Capability == CapabilityKind.References
            && capability.Status == CapabilityStatus.Supported
            && capability.Provider == provider.Id
            && capability.Language == "csharp");

    private static string? Remediation(string code) => code switch
    {
        ContractValues.ErrorIndexMissing => "Run index_codebase, then retry find_references.",
        ContractValues.ErrorIndexCorrupt => "Run index_codebase to replace the invalid index.",
        ContractValues.ErrorIndexIncompatible => "Run index_codebase with this Sanjaya version.",
        ContractValues.ErrorIndexStale => "Run index_codebase, then retry find_references.",
        _ => null,
    };

    private static ToolResponse<FindReferencesData> PathError(RepositoryPathError error) => error switch
    {
        RepositoryPathError.OutsideRepository => Error(
            ContractValues.ErrorPathOutsideRepository,
            "Path resolves outside the configured repository."),
        RepositoryPathError.NotFound => Error(ContractValues.ErrorFileNotFound, "File was not found in the repository."),
        RepositoryPathError.NotAFile => Error(ContractValues.ErrorNotAFile, "Path must identify one regular file."),
        _ => Error(ContractValues.ErrorInvalidPath, "Path must be a valid repository-relative file path."),
    };

    private static ToolResponse<FindReferencesData> Error(
        string code,
        string message,
        string? remediation = null) => new(
        ContractValues.StatusError,
        PublicToolNames.FindReferences,
        "sanjaya-index",
        null,
        [],
        [],
        new ErrorDetail(code, message, remediation));
}
