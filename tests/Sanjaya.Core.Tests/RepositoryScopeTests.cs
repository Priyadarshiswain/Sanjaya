using Sanjaya.Core.Repositories;
using Xunit;

namespace Sanjaya.Core.Tests;

public sealed class RepositoryScopeTests
{
    [Fact]
    public void MissingAndInvalidRootsRemainSafeUnreadyStates()
    {
        RepositoryScope missing = RepositoryScope.Create(null);
        RepositoryScope relative = RepositoryScope.Create("relative/repository");
        RepositoryScope nonexistent = RepositoryScope.Create(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.False(missing.IsReady);
        Assert.Equal("repository_root_required", missing.ConfigurationReason);
        Assert.False(relative.IsReady);
        Assert.Equal("repository_root_relative", relative.ConfigurationReason);
        Assert.False(nonexistent.IsReady);
        Assert.Equal("repository_root_not_found", nonexistent.ConfigurationReason);
        Assert.DoesNotContain(System.IO.Path.GetTempPath(), nonexistent.ConfigurationError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RepositoryConfigurationFailure.MissingValue, "repository_root_value_missing")]
    [InlineData(RepositoryConfigurationFailure.Duplicate, "repository_root_duplicate")]
    [InlineData(RepositoryConfigurationFailure.UnknownArgument, "repository_root_unknown_argument")]
    public void ParserFailuresRemainActionableWithoutInputEcho(
        RepositoryConfigurationFailure failure,
        string expectedReason)
    {
        RepositoryScope scope = RepositoryScope.Create(null, failure);

        Assert.False(scope.IsReady);
        Assert.Equal(expectedReason, scope.ConfigurationReason);
        Assert.NotNull(scope.ConfigurationError);
        Assert.NotNull(scope.ConfigurationRemediation);
    }

    [Fact]
    public void ResolvesOnlyRepositoryRelativeRegularFiles()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("src/example.txt", "text");
        RepositoryScope scope = RepositoryScope.Create(repository.Path);

        RepositoryPathResult valid = scope.ResolveFile("src/example.txt");
        Assert.True(valid.IsSuccess);
        Assert.Equal("src/example.txt", valid.RelativePath);
        Assert.Equal(RepositoryPathError.InvalidPath, scope.ResolveFile("../outside.txt").Error);
        Assert.Equal(RepositoryPathError.InvalidPath, scope.ResolveFile("/tmp/outside.txt").Error);
        Assert.Equal(RepositoryPathError.InvalidPath, scope.ResolveFile(@"C:\outside.txt").Error);
        Assert.Equal(RepositoryPathError.NotAFile, scope.ResolveFile("src").Error);
    }

    [Fact]
    public void PrefixCollisionCannotEscapeRepository()
    {
        using TemporaryDirectory parent = new();
        string repository = Directory.CreateDirectory(System.IO.Path.Combine(parent.Path, "repo")).FullName;
        File.WriteAllText(System.IO.Path.Combine(parent.Path, "repo2.txt"), "outside");
        RepositoryScope scope = RepositoryScope.Create(repository);

        Assert.NotEqual(RepositoryPathError.None, scope.ResolveFile("../repo2.txt").Error);
    }

    [Fact]
    public void RejectsExternalAndInternalFileSymlinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory repository = new();
        using TemporaryDirectory outside = new();
        string internalTarget = repository.WriteFile("target.txt", "inside");
        string externalTarget = outside.WriteFile("secret.txt", "outside");
        File.CreateSymbolicLink(System.IO.Path.Combine(repository.Path, "internal-link.txt"), internalTarget);
        File.CreateSymbolicLink(System.IO.Path.Combine(repository.Path, "external-link.txt"), externalTarget);
        File.CreateSymbolicLink(
            System.IO.Path.Combine(repository.Path, "broken-link.txt"),
            System.IO.Path.Combine(outside.Path, "missing.txt"));
        RepositoryScope scope = RepositoryScope.Create(repository.Path);

        Assert.Equal(RepositoryPathError.Symlink, scope.ResolveFile("internal-link.txt").Error);
        Assert.Equal(RepositoryPathError.OutsideRepository, scope.ResolveFile("external-link.txt").Error);
        Assert.NotEqual(RepositoryPathError.NotFound, scope.ResolveFile("broken-link.txt").Error);
    }

    [Fact]
    public void GitMetadataCandidateRejectsSymlinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory repository = new();
        using TemporaryDirectory outside = new();
        Directory.CreateDirectory(System.IO.Path.Combine(outside.Path, "metadata"));
        Directory.CreateSymbolicLink(
            System.IO.Path.Combine(repository.Path, ".git"),
            System.IO.Path.Combine(outside.Path, "metadata"));

        Assert.False(RepositoryScope.Create(repository.Path).IsGitWorktreeCandidate);
    }
}
