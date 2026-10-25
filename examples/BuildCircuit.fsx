#r "../dotnet/Simulator.Client/bin/Debug/net10.0/Simulator.Client.dll"
open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open Simulator.Client
// Typed host functions produce canonical request data; no solver logic lives here.
let resistor (id:string) (p:int) (n:int) (ohms:float) : JsonNode =
    JsonSerializer.SerializeToNode({|kind="resistor";id=id;p=p;n=n;ohms=ohms|})
let voltage (id:string) (p:int) (n:int) (volts:float) : JsonNode =
    JsonSerializer.SerializeToNode({|kind="voltage";id=id;p=p;n=n;volts=volts|})
let request =
    {| schema_version="1"; case_id="fsharp-builder"; backend="lu"
       circuit={|nodes=[|"0";"in";"out"|];elements=[|voltage "V1" 1 0 10.0;resistor "R1" 1 2 1000.0;resistor "R2" 2 0 1000.0|]|}
       analysis={|kind="operating_point"|} |}
let args=fsi.CommandLineArgs |> Array.skip 1
if args.Length<>1 then failwith "Expected simulator executable."
let result=SimulationClient(Path.GetFullPath(args.[0])).RunJsonAsync(JsonSerializer.Serialize(request)).GetAwaiter().GetResult()
if abs(result.FinalValue("v(out)")-5.0)>1e-12 then failwith "Builder reference failed."
printfn "F# builder: v(out)=%g V" (result.FinalValue("v(out)"))

