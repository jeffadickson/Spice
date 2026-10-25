using Simulator.Client;
// Self-contained contract runner: no test framework packages or fabricated coverage.
if (args.Length != 1) throw new ArgumentException("Expected executable path.");
var client = new SimulationClient(Path.GetFullPath(args[0]));
var divider = await File.ReadAllTextAsync("examples/divider.json");
var rc = await File.ReadAllTextAsync("examples/rc.json");
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
Console.WriteLine($"Managed conformance: {passed} checks passed.");

