using Sanjaya.Core.Contracts;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Discovery;

public sealed class FileOutlineService(RepositoryScope repository)
{
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

        List<FilePreviewLine> preview = [];
        int lineCount = 0;
        bool previewTruncated = false;
        using StringReader reader = new(file.Text!);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Error(ContractValues.ErrorCancelled, "File outline was cancelled.");
            }

            lineCount++;
            if (preview.Count < DiscoveryLimits.MaximumPreviewLines)
            {
                if (line.Length > DiscoveryLimits.MaximumPreviewCharactersPerLine)
                {
                    previewTruncated = true;
                }

                preview.Add(new FilePreviewLine(
                    lineCount,
                    line.Length <= DiscoveryLimits.MaximumPreviewCharactersPerLine
                        ? line
                        : line[..DiscoveryLimits.MaximumPreviewCharactersPerLine]));
            }
            else
            {
                previewTruncated = true;
            }
        }

        if (file.Text!.Length == 0)
        {
            lineCount = 0;
        }

        FileOutlineData data = new(
            resolved.RelativePath!,
            file.ByteCount,
            lineCount,
            preview,
            previewTruncated);
        int evidenceEndLine = Math.Max(1, Math.Min(lineCount, DiscoveryLimits.MaximumPreviewLines));

        return new ToolResponse<FileOutlineData>(
            ContractValues.StatusOk,
            PublicToolNames.FileOutline,
            "generic-text",
            data,
            [new EvidenceLocation(resolved.RelativePath!, 1, evidenceEndLine)],
            []);
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

    private static ToolResponse<FileOutlineData> Error(string code, string message, string? remediation = null) =>
        new(
            ContractValues.StatusError,
            PublicToolNames.FileOutline,
            "generic-text",
            null,
            [],
            [],
            new ErrorDetail(code, message, remediation));
}
