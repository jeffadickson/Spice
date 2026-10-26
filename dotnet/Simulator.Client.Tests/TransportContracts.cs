using Simulator.Client;
using System.Text.Json;
using Xunit;

/// <summary>Adversarial native-process envelopes, isolated from the real numerical engine.</summary>
public sealed class TransportContracts
{
    private static SimulationClient Fixture()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "fixture",
            "Simulator.TransportFixture" + (OperatingSystem.IsWindows() ? ".exe" : ""));
        Assert.True(File.Exists(executable), "Build the transport fixture with the test project.");
        return new SimulationClient(executable);
    }

    private static string Request(string output, int exit = 0) =>
        JsonSerializer.Serialize(new { output, exit_code = exit });

    [Fact]
    public async Task AcceptsControlledCurrentEnvelope()
    {
        var response = await Fixture().RunJsonAsync(Request(
            """{"schema_version":"1","status":"succeeded","complete":true}"""));
        Assert.Equal("1", response.SchemaVersion);
        Assert.True(response.Succeeded);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("")]
    public async Task RejectsUnsupportedProtocolVersions(string version)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Fixture().RunJsonAsync(
            Request($$"""{"schema_version":"{{version}}","status":"succeeded","complete":true}""")));
    }

    [Fact]
    public async Task RejectsMalformedJsonFromChild()
    {
        await Assert.ThrowsAnyAsync<JsonException>(() => Fixture().RunJsonAsync(Request("{")));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"schema_version":1,"status":"succeeded","complete":true}""")]
    [InlineData("""{"schema_version":"1","status":"unknown","complete":true}""")]
    [InlineData("""{"schema_version":"1","status":"succeeded","complete":false}""")]
    [InlineData("""{"schema_version":"1","status":"succeeded","complete":"true"}""")]
    [InlineData("""{"schema_version":"1","status":"failed","complete":true,"error":{"code":"x","message":"m"}}""")]
    [InlineData("""{"schema_version":"1","status":"failed","complete":false}""")]
    [InlineData("""{"schema_version":"1","status":"failed","complete":false,"error":null}""")]
    [InlineData("""{"schema_version":"1","status":"failed","complete":false,"error":{"code":null,"message":"m"}}""")]
    [InlineData("""{"schema_version":"1","status":"succeeded","complete":true,"error":{"code":"x","message":"m"}}""")]
    public async Task RejectsMalformedOrContradictoryEnvelopes(string output)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Fixture().RunJsonAsync(Request(output)));
    }
}
