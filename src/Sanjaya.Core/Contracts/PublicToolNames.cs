namespace Sanjaya.Core.Contracts;

/// <summary>
/// Proposed v0.1 public tool names. Contract tests protect this list from accidental drift.
/// </summary>
public static class PublicToolNames
{
    public const string Capabilities = "capabilities";
    public const string HealthCheck = "health_check";
    public const string FileOutline = "file_outline";
    public const string SearchText = "search_text";
    public const string RecentChanges = "recent_changes";
    public const string IndexCodebase = "index_codebase";
    public const string SearchCode = "search_code";
    public const string FindDefinition = "find_definition";
    public const string FindReferences = "find_references";
    public const string GetSource = "get_source";

    public static IReadOnlyList<string> All { get; } =
    [
        Capabilities,
        HealthCheck,
        FileOutline,
        SearchText,
        RecentChanges,
        IndexCodebase,
        SearchCode,
        FindDefinition,
        FindReferences,
        GetSource,
    ];

    /// <summary>
    /// Tools implemented independently of repository-root readiness.
    /// </summary>
    public static IReadOnlyList<string> ProtocolFoundation { get; } =
    [
        Capabilities,
        HealthCheck,
    ];

    /// <summary>
    /// Discovery tools implemented by the current runtime when a repository root is ready.
    /// </summary>
    public static IReadOnlyList<string> ImmediateDiscovery { get; } =
    [
        FileOutline,
        SearchText,
    ];
}
