#r "../dotnet/Simulator.Client/bin/Debug/net10.0/Simulator.Client.dll"
open System
open System.IO
open Simulator.Client
// Typed DTOs are the same public surface used by C# GUI consumers.
let args=fsi.CommandLineArgs |> Array.skip 1
if args.Length<>1 then failwith "Expected simulator executable."
let elements : Element[] = [| Capacitor("C1",1,0,1e-6); Resistor("R1",1,2,10.0); Inductor("L1",2,0,1e-3) |]
let circuit=Circuit([|"0";"a";"b"|],elements)
let options=TransientConfiguration(0.0002,1e-6,10000,"trapezoidal",[|1.0;1.0;0.0|],null,null)
let analysis=TransientConfigured(options)
let request=SimulationRequest("typed-fsharp-rlc",circuit,analysis,"faer",null)
let response=SimulationClient(Path.GetFullPath(args.[0])).RunAsync(request).GetAwaiter().GetResult()
let manifest=response.ReadManifest()
if not manifest.Complete || not manifest.Backend.Sparse then failwith "Expected complete sparse result."
printfn "Typed F# RLC: samples=%d, backend=%s" manifest.Samples.Length manifest.Backend.Id

