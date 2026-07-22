using Sanjaya.Server.Configuration;
using Sanjaya.Core.Repositories;
using Xunit;

namespace Sanjaya.Server.Tests;

public sealed class RootConfigurationTests
{
    [Fact]
    public void MissingRootDoesNotUseCurrentDirectory()
    {
        RootConfigurationResult result = RootConfiguration.Parse([]);

        Assert.Null(result.Root);
        Assert.Equal(RepositoryConfigurationFailure.Missing, result.Failure);
    }

    [Fact]
    public void RootArgumentIsForwardedWithoutInterpretation()
    {
        const string root = "folder with spaces/../repository";
        RootConfigurationResult result = RootConfiguration.Parse(["--root", root]);

        Assert.Equal(root, result.Root);
        Assert.Equal(RepositoryConfigurationFailure.None, result.Failure);
    }

    [Theory]
    [InlineData(RepositoryConfigurationFailure.MissingValue, "--root")]
    [InlineData(RepositoryConfigurationFailure.MissingValue, "--root", "--unknown")]
    [InlineData(RepositoryConfigurationFailure.Duplicate, "--root", "/first", "--root", "/second")]
    [InlineData(RepositoryConfigurationFailure.UnknownArgument, "--unknown")]
    public void MalformedRootConfigurationPreservesStableFailure(
        RepositoryConfigurationFailure expected,
        params string[] arguments)
    {
        RootConfigurationResult result = RootConfiguration.Parse(arguments);

        Assert.Null(result.Root);
        Assert.Equal(expected, result.Failure);
    }
}
