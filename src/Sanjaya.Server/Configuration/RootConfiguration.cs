namespace Sanjaya.Server.Configuration;

/// <summary>
/// Parses only Sanjaya's explicit immutable repository-root argument.
/// </summary>
public static class RootConfiguration
{
    public static string? Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string? root = null;
        for (int index = 0; index < arguments.Length; index++)
        {
            if (!string.Equals(arguments[index], "--root", StringComparison.Ordinal))
            {
                continue;
            }

            if (root is not null || index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                return null;
            }

            root = arguments[++index];
        }

        return root;
    }
}
