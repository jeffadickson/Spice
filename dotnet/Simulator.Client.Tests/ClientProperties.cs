using FsCheck.Xunit;
using Simulator.Client;
using System.Text.Json;
using Xunit;

/// <summary>Library-generated materialized-result contracts with shrinking and seed replay.</summary>
public sealed class ClientProperties
{
    /// <summary>Signal IDs retain their physical values across column permutations and sample history.</summary>
    [Property(MaxTest = 256, EndSize = 64, Replay = "362,20261027")]
    public void FinalSignalSelectionSurvivesPermutation(int[] source, uint selected, uint rotation)
    {
        // Ensure even the empty generated array exercises a real signal.
        var values = source.Length == 0 ? new[] { 0 } : source;
        var index = (int)(selected % (uint)values.Length);
        var offset = (int)(rotation % (uint)values.Length);
        var order = Enumerable.Range(0, values.Length).Select(i => (i + offset) % values.Length).ToArray();
        var payload = JsonSerializer.SerializeToElement(new {
            signals = order.Select(i => new { id = $"signal-{i}" }).ToArray(),
            samples = new[] {
                new { values = order.Select(i => -(double)values[i] - 0.5).ToArray() },
                new { values = order.Select(i => (double)values[i]).ToArray() }
            }
        });
        var response = new SimulationResponse("1", "succeeded", true, payload);
        Assert.Equal((double)values[index], response.FinalValue($"signal-{index}"));
        Assert.Throws<KeyNotFoundException>(() => response.FinalValue("missing-signal"));
    }
}
