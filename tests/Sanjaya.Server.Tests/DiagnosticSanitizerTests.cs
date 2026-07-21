using Sanjaya.Server.Diagnostics;
using Xunit;

namespace Sanjaya.Server.Tests;

public sealed class DiagnosticSanitizerTests
{
    [Theory]
    [InlineData("Failed at /workspace/example/private/project/file.cs", "/workspace/example")]
    [InlineData(@"Failed at C:\Users\example\private\file.cs", @"C:\Users\example")]
    public void SanitizeRedactsAbsolutePaths(string diagnostic, string sensitiveFragment)
    {
        string result = DiagnosticSanitizer.Sanitize(diagnostic);

        Assert.DoesNotContain(sensitiveFragment, result, StringComparison.Ordinal);
        Assert.Contains("[path]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeFlattensLinesAndBoundsLength()
    {
        string diagnostic = string.Concat("first\nsecond\r\n", new string('x', 1000));

        string result = DiagnosticSanitizer.Sanitize(diagnostic);

        Assert.DoesNotContain('\n', result);
        Assert.DoesNotContain('\r', result);
        Assert.True(result.Length <= 500);
    }
}
