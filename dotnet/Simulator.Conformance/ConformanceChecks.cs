using Simulator.Client;
/// <summary>Shared real-engine contracts for the console runner and discoverable mutation tests.</summary>
public static class ConformanceChecks
{
    public static async Task<int> Run(string executable, string root)
    {
        var client = new SimulationClient(Path.GetFullPath(executable));
        var divider = await File.ReadAllTextAsync(Path.Combine(root, "examples/divider.json"));
        var rc = await File.ReadAllTextAsync(Path.Combine(root, "examples/rc.json"));
        var passed = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed++; }
        var result = await client.RunJsonAsync(divider);
        Check(result.Succeeded && Math.Abs(result.FinalValue("v(out)") - 5) < 1e-12, "DC");
        var dynamicResult = await client.RunJsonAsync(rc);
        Check(Math.Abs(dynamicResult.FinalValue("v(out)") - (1 - Math.Exp(-5))) < 0.001, "transient");
        var invalid = await client.RunJsonAsync("{}");
        Check(invalid.Status == "failed" && !invalid.Complete, "invalid request");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await client.RunJsonAsync(divider, cancel.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { passed++; }
        var pair = await Task.WhenAll(client.RunJsonAsync(divider), client.RunJsonAsync(rc));
        Check(pair.All(r => r.Succeeded) && pair[0].FinalValue("v(out)") != pair[1].FinalValue("v(out)"), "instance isolation");
        Check(result.Payload.GetProperty("request_sha256").GetString()!.Length == 64, "provenance");
        var typed = new SimulationRequest("typed-dc",
            new Circuit(["0","in","out"], [new Voltage("V1",1,0,10),new Resistor("R1",1,2,1000),new Resistor("R2",2,0,1000)]),
            new OperatingPoint(), "faer");
        var typedResponse = await client.RunAsync(typed);
        var typedManifest = typedResponse.ReadManifest();
        Check(typedManifest.Backend.Sparse && typedManifest.Signals[1].Unit == "V" && typedManifest.Samples[0].Values[1] == 5, "typed request/result");
        var adaptive = await client.RunJsonAsync(await File.ReadAllTextAsync(Path.Combine(root, "examples/adaptive-rc.json")));
        var adaptiveManifest = adaptive.ReadManifest();
        Check(adaptiveManifest.Integration!.RejectedSteps > 0 && adaptiveManifest.Assertions[0].Outcome == "pass", "adaptive assertions");
        Check(adaptiveManifest.Measurements[0].Value is > 0.0066 and < 0.0069, "typed measurement");
        Check(Math.Abs(typedManifest.DcPower!.SumAbsorbedW)<1e-12,"DC power");
        Check(typedManifest.Consistency!.Checks.Any(c => c.Id == "power_balance" && c.Status == "passed" && c.Unit == "W" && c.Tolerance > 0), "typed physical audit" );
        Check(typedManifest.Consistency.Checks.Any(c => c.Id == "global_accuracy" && c.Status == "not_checked" && c.Reason is not null), "typed audit limits" );
        using (var bad = System.Text.Json.JsonDocument.Parse("""{"schema_version":"1","status":"succeeded","complete":true}"""))
        {
            try { new SimulationResponse("1","succeeded",true,bad.RootElement.Clone()).ReadManifest(); throw new Exception("Malformed manifest accepted"); }
            catch (System.IO.InvalidDataException) { passed++; }
        }
        try { invalid.ReadManifest(); throw new Exception("Failed result treated as successful"); }
        catch (InvalidOperationException) { passed++; }
        try { typedResponse.FinalValue("v(missing)"); throw new Exception("Unknown signal accepted"); }
        catch (KeyNotFoundException) { passed++; }
        Check(invalid.Error?.Code=="invalid_input" && typedResponse.Error is null,"typed error classification");
        passed += await MoreChecks.Run(client);
        return passed;


    }
}



