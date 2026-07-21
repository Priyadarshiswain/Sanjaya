using System.Text.Json;

namespace Sanjaya.Core.Indexing;

internal static class IndexSerialization
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}
