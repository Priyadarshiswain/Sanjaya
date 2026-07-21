using Sanjaya.Server.Configuration;
using Xunit;

namespace Sanjaya.Server.Tests;

public sealed class RootConfigurationTests
{
    [Fact]
    public void MissingRootDoesNotUseCurrentDirectory()
    {
        Assert.Null(RootConfiguration.Parse([]));
    }

    [Fact]
    public void RootArgumentIsForwardedWithoutInterpretation()
    {
        const string root = "folder with spaces/../repository";
        Assert.Equal(root, RootConfiguration.Parse(["--root", root]));
    }

    [Theory]
    [InlineData("--root")]
    [InlineData("--root", "first", "--root", "second")]
    public void MalformedRootConfigurationBecomesMissingRuntimeState(params string[] arguments)
    {
        Assert.Null(RootConfiguration.Parse(arguments));
    }
}
