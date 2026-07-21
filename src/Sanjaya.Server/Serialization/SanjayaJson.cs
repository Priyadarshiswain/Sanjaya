using System.Text.Json;

namespace Sanjaya.Server.Serialization;

/// <summary>
/// Shared JSON settings for tool schemas and structured results.
/// </summary>
public static class SanjayaJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}
