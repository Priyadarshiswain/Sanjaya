namespace Sanjaya.Core.Providers;

public static class ReferenceLookupLimits
{
    public const int MaximumNameCharacters = 240;
    public const int DefaultResults = 50;
    public const int MaximumResults = 200;
    public const int MaximumMatchesPerFile = 10_000;
    public const int MaximumTotalMatches = 50_000;
    public const int MaximumSnippetCharacters = 320;
    public const string Classification = "syntax_candidate";
}
