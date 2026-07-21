using Sanjaya.Core.Repositories;
using Xunit;

namespace Sanjaya.Core.Tests;

public sealed class RepositoryScopeTests
{
    [Fact]
    public void MissingAndInvalidRootsRemainSafeUnreadyStates()
    {
        Assert.False(RepositoryScope.Create(null).IsReady);
        Assert.False(RepositoryScope.Create("relative/repository").IsReady);
        Assert.False(RepositoryScope.Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"))).IsReady);
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
        RepositoryScope scope = RepositoryScope.Create(repository.Path);

        Assert.Equal(RepositoryPathError.Symlink, scope.ResolveFile("internal-link.txt").Error);
        Assert.Equal(RepositoryPathError.OutsideRepository, scope.ResolveFile("external-link.txt").Error);
    }
}
