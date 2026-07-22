using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Indexing;

public sealed class SearchCodeService(
    RepositoryScope repository,
    IEnumerable<IStructuralChunkProvider> structuralProviders,
    IndexBuildLimits? configuredLimits = null)
{
    private static readonly string[] FieldOrder = ["name", "container", "kind", "path", "content"];

    private readonly IReadOnlyList<IStructuralChunkProvider> providers = structuralProviders
        .GroupBy(provider => provider.Id, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(provider => provider.Id, StringComparer.Ordinal)
        .ToArray();
    private readonly IndexBuildLimits limits = configuredLimits ?? IndexBuildLimits.Default;

    public async Task<ToolResponse<SearchCodeData>> SearchAsync(
        string? query,
        bool caseSensitive,
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

        if (providers.Count == 0)
        {
            return Error(
                ContractValues.ErrorStructuralProviderUnavailable,
                "No structural provider is available for indexed search.");
        }

        string normalizedQuery = query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length is < 1 or > SearchCodeLimits.MaximumQueryCharacters
            || normalizedQuery.Contains('\r')
            || normalizedQuery.Contains('\n')
            || normalizedQuery.Contains('\0'))
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                $"Query must be a single line containing 1 to {SearchCodeLimits.MaximumQueryCharacters} characters.");
        }

        int maximumResults = requestedMaximumResults ?? SearchCodeLimits.DefaultResults;
        if (maximumResults is < 1 or > SearchCodeLimits.MaximumResults)
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                $"maxResults must be between 1 and {SearchCodeLimits.MaximumResults}.");
        }

        StringComparer comparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        string[] terms = normalizedQuery.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(comparer)
            .ToArray();

        try
        {
            IndexDocument document = await new FreshIndexReader(repository, providers, limits)
                .ReadAsync(cancellationToken)
                .ConfigureAwait(false);

            List<Candidate> candidates = [];
            foreach (IndexChunk chunk in document.Chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Candidate? candidate = Match(chunk, terms, caseSensitive);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }

            Candidate[] ordered = candidates
                .OrderByDescending(candidate => candidate.Match.Score)
                .ThenBy(candidate => candidate.Match.Path, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Match.StartLine)
                .ThenBy(candidate => candidate.Match.EndLine)
                .ThenBy(candidate => candidate.Match.Kind, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Match.Name, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Match.ChunkId, StringComparer.Ordinal)
                .ToArray();
            CodeSearchMatch[] matches = ordered
                .Take(maximumResults)
                .Select(candidate => candidate.Match)
                .ToArray();
            EvidenceLocation[] evidence = matches.Select(match => new EvidenceLocation(
                match.Path,
                match.StartLine,
                match.EndLine,
                match.Name)).ToArray();
            IndexQuality quality = IndexEvidenceQuality.Inspect(document);
            SearchCodeData data = new(
                normalizedQuery,
                caseSensitive,
                document.RepositoryFingerprint,
                matches,
                ordered.Length,
                ordered.Length > maximumResults);

            return new ToolResponse<SearchCodeData>(
                quality.IsPartial ? ContractValues.StatusPartial : ContractValues.StatusOk,
                PublicToolNames.SearchCode,
                "sanjaya-index",
                data,
                evidence,
                quality.Warnings);
        }
        catch (OperationCanceledException)
        {
            return Error(ContractValues.ErrorCancelled, "Structural search was cancelled.");
        }
        catch (IndexReadFailure failure)
        {
            return Error(failure.Code, failure.Message, Remediation(failure.Code));
        }
    }

    private static Candidate? Match(IndexChunk chunk, IReadOnlyList<string> terms, bool caseSensitive)
    {
        StringComparison comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        HashSet<string> matchedFields = new(StringComparer.Ordinal);
        int score = 0;

        foreach (string term in terms)
        {
            int best = 0;
            if (chunk.Name.Equals(term, comparison))
            {
                best = 1000;
                matchedFields.Add("name");
            }
            else if (chunk.Name.StartsWith(term, comparison))
            {
                best = 800;
                matchedFields.Add("name");
            }
            else if (chunk.Name.Contains(term, comparison))
            {
                best = 600;
                matchedFields.Add("name");
            }

            best = MatchField(chunk.Container, term, comparison, "container", 400, best, matchedFields);
            best = MatchField(chunk.Kind, term, comparison, "kind", 300, best, matchedFields);
            best = MatchField(chunk.Path, term, comparison, "path", 200, best, matchedFields);
            best = MatchField(chunk.Content, term, comparison, "content", 100, best, matchedFields);
            if (best == 0)
            {
                return null;
            }

            score += best;
        }

        string[] orderedFields = FieldOrder.Where(matchedFields.Contains).ToArray();
        CodeSearchMatch match = new(
            chunk.Id,
            chunk.Path,
            chunk.Kind,
            chunk.Name,
            chunk.Container,
            chunk.StartLine,
            chunk.EndLine,
            score,
            orderedFields,
            IndexSnippet.Create(
                chunk.Content,
                terms,
                comparison,
                SearchCodeLimits.MaximumSnippetCharacters));
        return new Candidate(match);
    }

    private static int MatchField(
        string? value,
        string term,
        StringComparison comparison,
        string field,
        int fieldScore,
        int best,
        HashSet<string> matchedFields)
    {
        if (value?.Contains(term, comparison) == true)
        {
            matchedFields.Add(field);
            return Math.Max(best, fieldScore);
        }

        return best;
    }

    private static string? Remediation(string code) => code switch
    {
        ContractValues.ErrorIndexMissing => "Run index_codebase, then retry search_code.",
        ContractValues.ErrorIndexCorrupt => "Run index_codebase to replace the invalid index.",
        ContractValues.ErrorIndexIncompatible => "Run index_codebase with this Sanjaya version.",
        ContractValues.ErrorIndexStale => "Run index_codebase, then retry search_code.",
        _ => null,
    };

    private static ToolResponse<SearchCodeData> Error(
        string code,
        string message,
        string? remediation = null) => new(
        ContractValues.StatusError,
        PublicToolNames.SearchCode,
        "sanjaya-index",
        null,
        [],
        [],
        new ErrorDetail(code, message, remediation));

    private sealed record Candidate(CodeSearchMatch Match);
}
