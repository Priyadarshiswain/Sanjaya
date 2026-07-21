using System.Text.RegularExpressions;

namespace Sanjaya.Server.Diagnostics;

/// <summary>
/// Keeps startup diagnostics useful without exposing local filesystem paths.
/// </summary>
public static partial class DiagnosticSanitizer
{
    private const int MaximumLength = 500;

    public static string Sanitize(string? diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            return "Sanjaya could not start.";
        }

        string flattened = diagnostic.Replace('\r', ' ').Replace('\n', ' ');
        flattened = WindowsPathPattern().Replace(flattened, "[path]");
        flattened = UnixPathPattern().Replace(flattened, "[path]");
        flattened = WhitespacePattern().Replace(flattened, " ").Trim();

        return flattened.Length <= MaximumLength
            ? flattened
            : string.Concat(flattened.AsSpan(0, MaximumLength - 1), "…");
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9_])[A-Za-z]:\\(?:[^\\\s]+\\)*[^\\\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])/(?:[^/\s]+/)*[^/\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnixPathPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
