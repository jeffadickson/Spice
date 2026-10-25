#r "../dotnet/Simulator.Client/bin/Debug/net10.0/Simulator.Client.dll"
open System
open System.IO
open Simulator.Client
// Run from repository root after building the client library.
let args = fsi.CommandLineArgs |> Array.skip 1
if args.Length <> 2 then failwith "Expected simulator executable and request JSON path."
let client = SimulationClient(Path.GetFullPath(args.[0]))
let result = client.RunJsonAsync(File.ReadAllText(args.[1])).GetAwaiter().GetResult()
printfn "status=%s complete=%b" result.Status result.Complete
if result.Succeeded then printfn "v(out)=%g V" (result.FinalValue("v(out)"))

