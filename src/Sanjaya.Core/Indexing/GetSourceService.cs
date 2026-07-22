using System.Text;
using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Indexing;

public sealed class GetSourceService(
    RepositoryScope repository,
    IEnumerable<IStructuralChunkProvider> structuralProviders,
    IEnumerable<ISourceRetrievalProvider> configuredSourceProviders,
    IndexBuildLimits? configuredLimits = null)
{
    private readonly IReadOnlyList<IStructuralChunkProvider> structural = structuralProviders
        .GroupBy(provider => provider.Id, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(provider => provider.Id, StringComparer.Ordinal)
        .ToArray();
    private readonly Dictionary<string, ISourceRetrievalProvider> sources = configuredSourceProviders
        .Where(IsCSharpSourceProvider)
        .GroupBy(provider => provider.Id, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    private readonly IndexBuildLimits limits = configuredLimits ?? IndexBuildLimits.Default;

    public async Task<ToolResponse<GetSourceData>> GetAsync(
        string? chunkId,
        int? requestedStartLine,
        int? requestedEndLine,
        CancellationToken cancellationToken)
    {
        if (!repository.IsReady)
        {
            return Error(
                ContractValues.ErrorRepositoryRootRequired,
                "Source retrieval requires an explicit valid --root path.",
                "Restart Sanjaya with --root <path>.");
        }

        if (sources.Count == 0)
        {
            return Error(
                ContractValues.ErrorSourceProviderUnavailable,
                "No C# syntax-source provider is available.");
        }

        string normalizedChunkId = chunkId?.Trim() ?? string.Empty;
        if (!IsValidChunkId(normalizedChunkId))
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                "chunkId must be an exact lowercase sha256 identifier returned by search_code or find_definition.");
        }

        if (requestedStartLine.HasValue != requestedEndLine.HasValue
            || requestedStartLine is <= 0
            || requestedEndLine is <= 0
            || requestedStartLine > requestedEndLine)
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                "startLine and endLine must be supplied together as a valid one-based inclusive range.");
        }

        try
        {
            FreshIndexState state = await new FreshIndexReader(repository, structural, limits)
                .ReadWithSourcesAsync(includeText: true, cancellationToken)
                .ConfigureAwait(false);
            IndexChunk[] indexedMatches = state.Document.Chunks
                .Where(chunk => chunk.Id.Equals(normalizedChunkId, StringComparison.Ordinal))
                .ToArray();
            if (indexedMatches.Length == 0)
            {
                return Error(
                    ContractValues.ErrorChunkNotFound,
                    "chunkId was not found in the current structural index.");
            }

            if (indexedMatches.Length > 1)
            {
                return Error(
                    ContractValues.ErrorSourceAmbiguous,
                    "chunkId identifies more than one indexed declaration; source retrieval refused to guess.");
            }

            IndexChunk chunk = indexedMatches[0];
            if (!sources.TryGetValue(chunk.Provider, out ISourceRetrievalProvider? provider)
                || !provider.CanHandle(chunk.Path))
            {
                return Error(
                    ContractValues.ErrorSourceProviderUnavailable,
                    "The indexed declaration does not have an active C# syntax-source provider.");
            }

            IndexSourceFile[] sourceMatches = state.Sources.Files
                .Where(source => source.Provider.Id == chunk.Provider && source.RelativePath == chunk.Path)
                .ToArray();
            if (sourceMatches.Length != 1)
            {
                return Error(
                    ContractValues.ErrorSourceResolutionFailed,
                    "The indexed declaration could not be resolved in the verified source snapshot.");
            }

            SourceRetrievalTarget target = new(
                chunk.Kind,
                chunk.Name,
                chunk.Container,
                chunk.StartLine,
                chunk.EndLine,
                chunk.Content,
                chunk.ContentTruncated);
            SourceRetrievalAnalysis analysis = provider.AnalyzeSource(
                chunk.Path,
                sourceMatches[0].Text!,
                target,
                cancellationToken);
            if (analysis.Matches.Count == 0)
            {
                return Error(
                    ContractValues.ErrorSourceResolutionFailed,
                    "The indexed declaration could not be matched to an exact current syntax span.");
            }

            if (analysis.Matches.Count > 1)
            {
                return Error(
                    ContractValues.ErrorSourceAmbiguous,
                    "The indexed declaration matches more than one current syntax span; source retrieval refused to guess.");
            }

            SourceDeclaration declaration = analysis.Matches[0];
            List<SourceLine> declarationLines = CreateLines(declaration.Content);
            if (!IsValidDeclaration(declaration, declarationLines))
            {
                return Error(
                    ContractValues.ErrorSourceResolutionFailed,
                    "The source provider returned an inconsistent declaration span.");
            }

            SourceWindow window;
            if (requestedStartLine.HasValue)
            {
                if (requestedStartLine < declaration.StartLine
                    || requestedEndLine > declaration.EndLine)
                {
                    return Error(
                        ContractValues.ErrorInvalidArgument,
                        "The requested line window must remain inside the selected declaration.");
                }

                window = CreateWindow(
                    declaration,
                    declarationLines,
                    requestedStartLine.Value,
                    requestedEndLine!.Value);
            }
            else
            {
                window = new SourceWindow(
                    declaration.Content,
                    new SourceRange(
                        declaration.StartLine,
                        declaration.StartColumn,
                        declaration.EndLine,
                        declaration.EndColumn));
            }

            if (Encoding.UTF8.GetByteCount(window.Content) > SourceRetrievalLimits.MaximumSourceBytes)
            {
                return Error(
                    ContractValues.ErrorSourceRangeTooLarge,
                    $"The selected source exceeds the {SourceRetrievalLimits.MaximumSourceBytes}-byte response limit.",
                    "Retry get_source with a smaller startLine and endLine window inside the declaration.");
            }

            bool complete = !requestedStartLine.HasValue;
            List<string> warnings = [];
            if (!complete)
            {
                warnings.Add("source_window_applied");
            }

            if (analysis.SyntaxDiagnosticCount > 0)
            {
                warnings.Add($"csharp_syntax_diagnostics_recovered:{analysis.SyntaxDiagnosticCount}");
            }

            GetSourceData data = new(
                chunk.Id,
                state.Document.RepositoryFingerprint,
                chunk.Provider,
                chunk.Language,
                chunk.Path,
                chunk.Kind,
                chunk.Name,
                chunk.Container,
                new SourceRange(
                    declaration.StartLine,
                    declaration.StartColumn,
                    declaration.EndLine,
                    declaration.EndColumn),
                window.Range,
                window.Content,
                complete,
                analysis.SyntaxDiagnosticCount);
            return new ToolResponse<GetSourceData>(
                !complete || analysis.SyntaxDiagnosticCount > 0
                    ? ContractValues.StatusPartial
                    : ContractValues.StatusOk,
                PublicToolNames.GetSource,
                chunk.Provider,
                data,
                [new EvidenceLocation(chunk.Path, window.Range.StartLine, window.Range.EndLine, chunk.Name)],
                warnings);
        }
        catch (OperationCanceledException)
        {
            return Error(ContractValues.ErrorCancelled, "Source retrieval was cancelled.");
        }
        catch (IndexReadFailure failure)
        {
            return Error(failure.Code, failure.Message, Remediation(failure.Code));
        }
    }

    private static List<SourceLine> CreateLines(string content)
    {
        List<SourceLine> lines = [];
        int lineStart = 0;
        for (int index = 0; index < content.Length; index++)
        {
            if (!IsLineBreak(content[index]))
            {
                continue;
            }

            lines.Add(new SourceLine(lineStart, index));
            if (content[index] == '\r'
                && index + 1 < content.Length
                && content[index + 1] == '\n')
            {
                index++;
            }

            lineStart = index + 1;
        }

        lines.Add(new SourceLine(lineStart, content.Length));
        return lines;
    }

    private static bool IsValidDeclaration(
        SourceDeclaration declaration,
        IReadOnlyList<SourceLine> lines)
    {
        if (declaration.Content.Length == 0
            || declaration.StartLine <= 0
            || declaration.StartColumn <= 0
            || declaration.EndLine < declaration.StartLine
            || declaration.EndColumn <= 0
            || lines.Count != declaration.EndLine - declaration.StartLine + 1)
        {
            return false;
        }

        if (lines.Count == 1)
        {
            return declaration.EndColumn - declaration.StartColumn == declaration.Content.Length;
        }

        SourceLine last = lines[^1];
        return declaration.EndColumn == last.End - last.Start + 1;
    }

    private static SourceWindow CreateWindow(
        SourceDeclaration declaration,
        IReadOnlyList<SourceLine> lines,
        int startLine,
        int endLine)
    {

        int relativeStart = startLine - declaration.StartLine;
        int relativeEnd = endLine - declaration.StartLine;
        int startOffset = relativeStart == 0 ? 0 : lines[relativeStart].Start;
        int endOffset = endLine == declaration.EndLine
            ? declaration.Content.Length
            : lines[relativeEnd].End;
        int endColumn = endLine == declaration.EndLine
            ? declaration.EndColumn
            : endOffset - lines[relativeEnd].Start + 1;

        int startColumn = startLine == declaration.StartLine ? declaration.StartColumn : 1;
        return new SourceWindow(
            declaration.Content[startOffset..endOffset],
            new SourceRange(startLine, startColumn, endLine, endColumn));
    }

    private static bool IsLineBreak(char character) =>
        character is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';

    private static bool IsValidChunkId(string value)
    {
        if (value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char character in value.AsSpan(7))
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCSharpSourceProvider(ISourceRetrievalProvider provider) =>
        provider.GetCapabilities().Any(capability =>
            capability.Capability == CapabilityKind.SourceRetrieval
            && capability.Status == CapabilityStatus.Supported
            && capability.Provider == provider.Id
            && capability.Language == "csharp");

    private static string? Remediation(string code) => code switch
    {
        ContractValues.ErrorIndexMissing => "Run index_codebase, then retry get_source.",
        ContractValues.ErrorIndexCorrupt => "Run index_codebase to replace the invalid index.",
        ContractValues.ErrorIndexIncompatible => "Run index_codebase with this Sanjaya version.",
        ContractValues.ErrorIndexStale => "Run index_codebase, then retry get_source.",
        _ => null,
    };

    private static ToolResponse<GetSourceData> Error(
        string code,
        string message,
        string? remediation = null) => new(
        ContractValues.StatusError,
        PublicToolNames.GetSource,
        "sanjaya-index",
        null,
        [],
        [],
        new ErrorDetail(code, message, remediation));

    private sealed record SourceWindow(string Content, SourceRange Range);

    private sealed record SourceLine(int Start, int End);
}
