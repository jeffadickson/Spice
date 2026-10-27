using Simulator.Client;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

/// <summary>Materialized output contracts targeted at real Stryker survivor findings.</summary>
public sealed class ManifestContracts
{
    private static JsonObject Valid() => JsonNode.Parse("""
        {"schema_version":"1","case_id":"manifest-fixture","status":"succeeded","complete":true,
         "signals":[{"id":"v(x)","quantity":"voltage","unit":"V"}],
         "samples":[{"time_s":0,"values":[1]}],"ac_samples":[],
         "measurements":[],"assertions":[],
         "backend":{"id":"fixture","placement":"cpu","max_unknowns":256,"sparse":false,"gpu":false,
                    "complex":false,"prepared_real":false,"provider_version":"fixture"},
         "request_sha256":"0000000000000000000000000000000000000000000000000000000000000000"}
        """)!.AsObject();

    private static SimulationResponse Response(JsonNode? payload, string version = "1") =>
        new(version, "succeeded", true, JsonSerializer.SerializeToElement(payload));

    [Theory]
    [InlineData("schema_version")]
    [InlineData("status")]
    [InlineData("complete")]
    [InlineData("signals")]
    [InlineData("samples")]
    [InlineData("ac_samples")]
    [InlineData("measurements")]
    [InlineData("assertions")]
    [InlineData("backend")]
    public void RejectsEachInvalidManifestFieldIndependently(string field)
    {
        var payload = Valid();
        payload[field] = field switch {
            "schema_version" => JsonValue.Create("2"),
            "status" => JsonValue.Create("failed"),
            "complete" => JsonValue.Create(false),
            _ => null
        };
        var error = Assert.Throws<InvalidDataException>(() => Response(payload).ReadManifest());
        Assert.NotEmpty(error.Message);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void RejectsNullOrWrongLengthSampleVectors(bool complex, bool nullValues)
    {
        var payload = Valid();
        if (complex)
            payload["ac_samples"] = JsonNode.Parse(nullValues
                ? """[{"frequency_hz":1,"values":null}]"""
                : """[{"frequency_hz":1,"values":[]}]""");
        else payload["samples"]![0]!["values"] = nullValues ? null : new JsonArray();
        Assert.Throws<InvalidDataException>(() => Response(payload).ReadManifest());
    }

    [Theory]
    [InlineData("signals")]
    [InlineData("samples")]
    [InlineData("ac_samples")]
    [InlineData("measurements")]
    [InlineData("assertions")]
    public void RejectsNullEntriesInsideNonnullableResultArrays(string field)
    {
        var payload = Valid();
        payload[field] = new JsonArray((JsonNode?)null);
        Assert.Throws<InvalidDataException>(() => Response(payload).ReadManifest());
    }

    [Fact]
    public void RejectsNullComplexComponents()
    {
        var payload = Valid();
        payload["ac_samples"] = JsonNode.Parse("""[{"frequency_hz":1,"values":[null]}]""");
        Assert.Throws<InvalidDataException>(() => Response(payload).ReadManifest());
    }

    [Fact]
    public void NullManifestAndUnknownOuterVersionCannotBeRead()
    {
        Assert.NotEmpty(Assert.Throws<InvalidDataException>(() => Response(null).ReadManifest()).Message);
        Assert.NotEmpty(Assert.Throws<InvalidDataException>(() => Response(Valid(), "2").ReadManifest()).Message);
    }

    [Fact]
    public void MissingFinalDescriptorHasTheDeclaredKeyError()
    {
        var response = Response(Valid());
        Assert.Throws<KeyNotFoundException>(() => response.FinalValue("v(missing)"));
    }

    [Fact]
    public void AcOnlyResultsHaveNoInventedFinalRealValue()
    {
        var payload = Valid();
        payload["samples"] = new JsonArray();
        var response = Response(payload);
        Assert.Empty(response.ReadManifest().Samples);
        Assert.NotEmpty(Assert.Throws<InvalidOperationException>(() => response.FinalValue("v(x)")).Message);
        var failed = response with { Status = "failed", Complete = false };
        Assert.NotEmpty(Assert.Throws<InvalidOperationException>(() => failed.FinalValue("v(x)")).Message);
    }
}
