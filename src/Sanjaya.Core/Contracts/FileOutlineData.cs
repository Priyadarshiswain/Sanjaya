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
    bool PreviewTruncated);

public sealed record FilePreviewLine(
    [property: JsonPropertyName("line")]
    int Line,
    [property: JsonPropertyName("text")]
    string Text);
