namespace Sanjaya.Core.Indexing;

public static class DefinitionLookupLimits
{
    public const int MaximumNameCharacters = 240;
    public const int MaximumContainerCharacters = 240;
    public const int MaximumKindCharacters = 64;
    public const int DefaultResults = 25;
    public const int MaximumResults = 100;
    public const int MaximumSnippetCharacters = 480;

    public static IReadOnlyList<string> SupportedCSharpKinds { get; } =
    [
        "namespace",
        "record",
        "record_struct",
        "class",
        "struct",
        "interface",
        "enum",
        "delegate",
        "method",
        "constructor",
        "destructor",
        "property",
        "indexer",
        "operator",
        "conversion_operator",
    ];
}
