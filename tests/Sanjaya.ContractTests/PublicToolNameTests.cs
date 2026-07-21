using System.Text.RegularExpressions;
using Sanjaya.Core.Contracts;
using Xunit;

namespace Sanjaya.ContractTests;

public sealed partial class PublicToolNameTests
{
    [Fact]
    public void NamesAreUniqueAndUseSnakeCase()
    {
        Assert.Equal(PublicToolNames.All.Count, PublicToolNames.All.Distinct().Count());
        Assert.All(PublicToolNames.All, name => Assert.Matches(ToolNamePattern(), name));
    }

    [Fact]
    public void ResponseSchemaStartsAtVersionOne()
    {
        Assert.Equal("1", ToolResponse<object>.CurrentSchemaVersion);
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$")]
    private static partial Regex ToolNamePattern();
}
