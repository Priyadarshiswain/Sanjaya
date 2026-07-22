using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Discovery;

public sealed class FileOutlineService(
    RepositoryScope repository,
    IEnumerable<IFileOutlineProvider>? providers = null)
{
    private readonly IReadOnlyList<IFileOutlineProvider> providers =
        (providers ?? []).OrderBy(provider => provider.Id, StringComparer.Ordinal).ToArray();

    public async Task<ToolResponse<FileOutlineData>> OutlineAsync(
        string? path,
        CancellationToken cancellationToken)
    {
        RepositoryPathResult resolved = repository.ResolveFile(path);
        if (!resolved.IsSuccess)
        {
            return PathError(resolved.Error);
        }

        TextFileReadResult file;
        try
        {
            file = await BoundedTextFile.ReadAsync(
                resolved.FullPath!,
                DiscoveryLimits.MaximumFileBytes,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Error(ContractValues.ErrorCancelled, "File outline was cancelled.");
        }

        if (!file.IsSuccess)
        {
            return file.Error switch
            {
                TextFileReadError.TooLarge => Error(
                    ContractValues.ErrorFileTooLarge,
                    $"File exceeds the {DiscoveryLimits.MaximumFileBytes}-byte outline limit."),
                TextFileReadError.Binary => Error(
                    ContractValues.ErrorBinaryFile,
                    "File is not readable UTF-8 text."),
                _ => Error(ContractValues.ErrorFileInaccessible, "File could not be read."),
            };
        }

        int lineCount = CountLines(file.Text!);
        IFileOutlineProvider? provider = providers.FirstOrDefault(candidate => candidate.CanHandle(resolved.RelativePath!));
        if (provider is not null)
        {
            return CreateProviderResponse(provider, resolved.RelativePath!, file, lineCount, cancellationToken);
        }

        return CreateGenericResponse(resolved.RelativePath!, file, lineCount, cancellationToken);
    }

    private static ToolResponse<FileOutlineData> CreateGenericResponse(
        string relativePath,
        TextFileReadResult file,
        int lineCount,
        CancellationToken cancellationToken)
    {
        List<FilePreviewLine> preview = [];
        bool previewTruncated = false;
        using StringReader reader = new(file.Text!);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Error(ContractValues.ErrorCancelled, "File outline was cancelled.");
            }

            if (preview.Count < DiscoveryLimits.MaximumPreviewLines)
            {
                if (line.Length > DiscoveryLimits.MaximumPreviewCharactersPerLine)
                {
                    previewTruncated = true;
                }

                preview.Add(new FilePreviewLine(
                    preview.Count + 1,
                    line.Length <= DiscoveryLimits.MaximumPreviewCharactersPerLine
                        ? line
                        : line[..DiscoveryLimits.MaximumPreviewCharactersPerLine]));
            }
            else
            {
                previewTruncated = true;
            }
        }

        FileOutlineData data = new(
            relativePath,
            file.ByteCount,
            lineCount,
            preview,
            previewTruncated,
            []);
        int evidenceEndLine = Math.Max(1, Math.Min(lineCount, DiscoveryLimits.MaximumPreviewLines));

        return new ToolResponse<FileOutlineData>(
            ContractValues.StatusOk,
            PublicToolNames.FileOutline,
            "generic-text",
            data,
            [new EvidenceLocation(relativePath, 1, evidenceEndLine)],
            []);
    }

    private static ToolResponse<FileOutlineData> CreateProviderResponse(
        IFileOutlineProvider provider,
        string relativePath,
        TextFileReadResult file,
        int lineCount,
        CancellationToken cancellationToken)
    {
        try
        {
            FileOutlineAnalysis analysis = provider.AnalyzeOutline(
                relativePath,
                file.Text!,
                cancellationToken);
            List<string> warnings = [];
            if (analysis.SyntaxDiagnosticCount > 0)
            {
                warnings.Add("syntax_diagnostics_recovered");
            }

            if (analysis.ItemsTruncated)
            {
                warnings.Add("outline_item_limit_reached");
            }

            string status = warnings.Count == 0 ? ContractValues.StatusOk : ContractValues.StatusPartial;
            int evidenceStartLine = analysis.Items.Count == 0 ? 1 : analysis.Items.Min(item => item.StartLine);
            int evidenceEndLine = analysis.Items.Count == 0
                ? Math.Max(1, lineCount)
                : analysis.Items.Max(item => item.EndLine);
            FileOutlineData data = new(
                relativePath,
                file.ByteCount,
                lineCount,
                [],
                false,
                analysis.Items,
                analysis.ItemsTruncated,
                analysis.SyntaxDiagnosticCount);

            return new ToolResponse<FileOutlineData>(
                status,
                PublicToolNames.FileOutline,
                provider.Id,
                data,
                [new EvidenceLocation(relativePath, evidenceStartLine, evidenceEndLine)],
                warnings);
        }
        catch (OperationCanceledException)
        {
            return Error(
                ContractValues.ErrorCancelled,
                "File outline was cancelled.",
                provider: provider.Id);
        }
        catch (StructuralProviderException exception)
        {
            return ProviderError(exception.Failure, provider.Id);
        }
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        int count = 1;
        foreach (char character in text)
        {
            if (character == '\n')
            {
                count++;
            }
        }

        return text.EndsWith('\n') ? count - 1 : count;
    }

    private static ToolResponse<FileOutlineData> PathError(RepositoryPathError error) => error switch
    {
        RepositoryPathError.RootRequired => Error(
            ContractValues.ErrorRepositoryRootRequired,
            "Repository discovery requires an explicit valid --root path.",
            "Restart Sanjaya with --root <path>."),
        RepositoryPathError.OutsideRepository => Error(
            ContractValues.ErrorPathOutsideRepository,
            "Path resolves outside the configured repository."),
        RepositoryPathError.NotFound => Error(ContractValues.ErrorFileNotFound, "File was not found in the repository."),
        RepositoryPathError.NotAFile => Error(ContractValues.ErrorNotAFile, "Path must identify one regular file."),
        _ => Error(ContractValues.ErrorInvalidPath, "Path must be a valid repository-relative file path."),
    };

    private static ToolResponse<FileOutlineData> Error(
        string code,
        string message,
        string? remediation = null,
        string provider = "generic-text") =>
        new(
            ContractValues.StatusError,
            PublicToolNames.FileOutline,
            provider,
            null,
            [],
            [],
            new ErrorDetail(code, message, remediation));

    private static ToolResponse<FileOutlineData> ProviderError(
        StructuralProviderFailure failure,
        string provider) => failure switch
        {
            StructuralProviderFailure.Unavailable => Error(
                ContractValues.ErrorStructuralProviderUnavailable,
                "The structural provider runtime is unavailable.",
                "Restart Sanjaya with a supported runtime.",
                provider),
            StructuralProviderFailure.TimedOut => Error(
                ContractValues.ErrorStructuralProviderTimeout,
                "The structural provider exceeded its analysis time limit.",
                provider: provider),
            StructuralProviderFailure.OutputLimit => Error(
                ContractValues.ErrorStructuralProviderOutputLimit,
                "The structural provider exceeded its bounded output limit.",
                provider: provider),
            _ => Error(
                ContractValues.ErrorStructuralProviderInvalidOutput,
                "The structural provider returned invalid protocol output.",
                provider: provider),
        };
}
