using Simulator.Client;

/// <summary>Test-only outer budget around the real public client, including synchronous faulty continuations.</summary>
public sealed class BoundedTestClient(string executable)
{
    private readonly SimulationClient client = new(executable);

    /// <summary>Give every conformance/fixture request a five-second cancellation budget and independent seven-second wait.</summary>
    public async Task<SimulationResponse> RunJsonAsync(string request, CancellationToken cancellation = default)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        var token = budget.Token;
        // Offload invocation too: an accidentally synchronous loop must not keep
        // the test from installing its independent assertion deadline.
        return await Task.Run(() => client.RunJsonAsync(request, token), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(7));
    }

    /// <summary>Preserve the production source-generated typed authoring path.</summary>
    public Task<SimulationResponse> RunAsync(SimulationRequest request, CancellationToken cancellation = default) =>
        RunJsonAsync(System.Text.Json.JsonSerializer.Serialize(request, WireFormat.Options), cancellation);
}
