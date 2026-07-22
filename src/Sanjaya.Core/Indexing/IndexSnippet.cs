namespace Sanjaya.Core.Indexing;

internal static class IndexSnippet
{
    public static string Create(
        string content,
        IReadOnlyList<string> terms,
        StringComparison comparison,
        int maximumCharacters)
    {
        if (content.Length <= maximumCharacters)
        {
            return content;
        }

        int occurrence = -1;
        foreach (string term in terms)
        {
            occurrence = content.IndexOf(term, comparison);
            if (occurrence >= 0)
            {
                break;
            }
        }

        int start = occurrence < 0
            ? 0
            : Math.Clamp(
                occurrence - (maximumCharacters / 2),
                0,
                content.Length - maximumCharacters);
        return content.Substring(start, maximumCharacters);
    }
}
