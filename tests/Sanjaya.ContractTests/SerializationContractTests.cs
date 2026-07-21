using System.Text.Json;
using Sanjaya.Core.Contracts;
using Xunit;

namespace Sanjaya.ContractTests;

public sealed class SerializationContractTests
{
    [Fact]
    public void ResponseEnvelopeUsesExactCamelCasePropertyNamesAndStringStatuses()
    {
        ToolResponse<HealthReportData> response = new(
            ContractValues.StatusOk,
            PublicToolNames.HealthCheck,
            "sanjaya-runtime",
            new HealthReportData(2, [new HealthCheckEntry("server", ContractValues.StatusOk, "Running.")]),
            [],
            []);

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(response));
        JsonElement root = document.RootElement;

        Assert.Equal("1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("health_check", root.GetProperty("capability").GetString());
        Assert.Equal("sanjaya-runtime", root.GetProperty("provider").GetString());
        Assert.Equal(2, root.GetProperty("data").GetProperty("registeredToolCount").GetInt32());
        Assert.Equal("ok", root.GetProperty("data").GetProperty("checks")[0].GetProperty("status").GetString());

        string[] expectedNames =
        [
            "status",
            "capability",
            "provider",
            "data",
            "evidence",
            "warnings",
            "error",
            "schemaVersion",
        ];

        Assert.Equal(expectedNames.Order(), root.EnumerateObject().Select(property => property.Name).Order());
    }

    [Fact]
    public void StableContractValuesDoNotDrift()
    {
        Assert.Equal("ok", ContractValues.StatusOk);
        Assert.Equal("partial", ContractValues.StatusPartial);
        Assert.Equal("error", ContractValues.StatusError);
        Assert.Equal("supported", ContractValues.AvailabilitySupported);
        Assert.Equal("unavailable", ContractValues.AvailabilityUnavailable);
        Assert.Equal("not_implemented", ContractValues.ReasonNotImplemented);
    }
}
