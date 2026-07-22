using Sanjaya.Core.Repositories;

namespace Sanjaya.Server.Configuration;

/// <summary>
/// Parses only Sanjaya's explicit immutable repository-root argument.
/// </summary>
public static class RootConfiguration
{
    public static RootConfigurationResult Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string? root = null;
        for (int index = 0; index < arguments.Length; index++)
        {
            if (!string.Equals(arguments[index], "--root", StringComparison.Ordinal))
            {
                return new(null, RepositoryConfigurationFailure.UnknownArgument);
            }

            if (root is not null)
            {
                return new(null, RepositoryConfigurationFailure.Duplicate);
            }

            if (index + 1 >= arguments.Length
                || string.IsNullOrWhiteSpace(arguments[index + 1])
                || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return new(null, RepositoryConfigurationFailure.MissingValue);
            }

            root = arguments[++index];
        }

        return root is null
            ? new(null, RepositoryConfigurationFailure.Missing)
            : new(root, RepositoryConfigurationFailure.None);
    }
}

public sealed record RootConfigurationResult(
    string? Root,
    RepositoryConfigurationFailure Failure);
