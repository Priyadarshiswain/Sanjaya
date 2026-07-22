namespace Sanjaya.Core.Indexing;

internal static class IndexEvidenceQuality
{
    public static IndexQuality Inspect(IndexDocument document)
    {
        int diagnostics = document.Files.Sum(file => file.SyntaxDiagnosticCount);
        int truncatedChunks = document.Chunks.Count(chunk => chunk.ContentTruncated);
        List<string> warnings = [];
        Add(warnings, "index_syntax_diagnostics_recovered", diagnostics);
        Add(warnings, "index_chunk_content_truncated", truncatedChunks);
        return new IndexQuality(diagnostics > 0 || truncatedChunks > 0, warnings);
    }

    private static void Add(List<string> warnings, string code, int count)
    {
        if (count > 0)
        {
            warnings.Add($"{code}:{count}");
        }
    }
}

internal sealed record IndexQuality(bool IsPartial, IReadOnlyList<string> Warnings);
