using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

public sealed record FileOutlineData(
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("byteCount")]
    long ByteCount,
    [property: JsonPropertyName("lineCount")]
    int LineCount,
    [property: JsonPropertyName("preview")]
    IReadOnlyList<FilePreviewLine> Preview,
    [property: JsonPropertyName("previewTruncated")]
    bool PreviewTruncated,
    [property: JsonPropertyName("items")]
    IReadOnlyList<OutlineItem> Items,
    [property: JsonPropertyName("itemsTruncated")]
    bool ItemsTruncated = false,
    [property: JsonPropertyName("syntaxDiagnosticCount")]
    int SyntaxDiagnosticCount = 0);

public sealed record FilePreviewLine(
    [property: JsonPropertyName("line")]
    int Line,
    [property: JsonPropertyName("text")]
    string Text);

public sealed record OutlineItem(
    [property: JsonPropertyName("kind")]
    string Kind,
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("display")]
    string Display,
    [property: JsonPropertyName("container")]
    string? Container,
    [property: JsonPropertyName("startLine")]
    int StartLine,
    [property: JsonPropertyName("endLine")]
    int EndLine);
