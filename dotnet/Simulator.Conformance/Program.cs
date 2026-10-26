// Keep the standalone contract entry point while sharing assertions with xUnit/Stryker.
if (args.Length != 1) throw new ArgumentException("Expected executable path.");
var passed = await ConformanceChecks.Run(args[0], Directory.GetCurrentDirectory());
Console.WriteLine($"Managed conformance: {passed} checks passed.");
