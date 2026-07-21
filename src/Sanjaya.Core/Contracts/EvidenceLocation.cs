namespace Sanjaya.Core.Contracts;

/// <summary>
/// Repository-relative evidence supporting a tool result.
/// </summary>
public sealed record EvidenceLocation(
    string Path,
    int StartLine,
    int EndLine,
    string? Symbol = null);

