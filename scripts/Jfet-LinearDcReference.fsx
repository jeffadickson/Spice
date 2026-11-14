open System
open System.IO
open System.Text.Json

// Independent, bounded reference for the four diagnosed original JFET points.
// This solves the B=1, 27 C channel plus RD=200 ohms and gate leakage by
// scalar bisection. It does not call the product model or Newton controller.
// It is a finite numerical reference, not a formal proof or full JFET model.
let args = fsi.CommandLineArgs |> Array.skip 1
if args.Length <> 2 then failwith "Expected existing error JSON and output JSON paths."
let input = Path.GetFullPath args[0]
let output = Path.GetFullPath args[1]
if not (File.Exists input) then failwith "Missing error JSON."
if not (Directory.Exists(Path.GetDirectoryName output)) then failwith "Missing output parent."
if File.Exists output then failwith "Fresh output required."

let thermalVolts = 1.38064852e-23 / 1.6021766208e-19 * 300.15
let drainSourceCurrent gateVolts drainVolts =
    if not (Double.IsFinite gateVolts && Double.IsFinite drainVolts)
       || gateVolts > 0.0 || gateVolts <= -3.5 || drainVolts <= 0.0 then
        failwith "Reference only covers positive-drain, reverse-gate, conducting NJFET bias."
    let residual intrinsicDrainVolts =
        let overdriveVolts = gateVolts + 3.5
        let shape =
            if intrinsicDrainVolts >= overdriveVolts then overdriveVolts*overdriveVolts
            else intrinsicDrainVolts*(2.0*overdriveVolts-intrinsicDrainVolts)
        let channelAmps = 4.1e-4 * (1.0+0.002*intrinsicDrainVolts) * shape
        let reverseGateAmps = 1e-14*(1.0-exp((gateVolts-intrinsicDrainVolts)/thermalVolts))
        let gminAmps = 1e-12*(intrinsicDrainVolts-gateVolts)
        (drainVolts-intrinsicDrainVolts)/200.0-channelAmps-reverseGateAmps-gminAmps
    let mutable lower = 0.0
    let mutable upper = drainVolts
    if residual lower < 0.0 || residual upper > 0.0 then failwith "Root is not bracketed."
    for _ in 1..100 do
        let midpoint = (lower+upper)/2.0
        if residual midpoint > 0.0 then lower <- midpoint else upper <- midpoint
    // Voltage-source current is positive into its positive terminal, opposite Id.
    -(drainVolts-(lower+upper)/2.0)/200.0

let errors = JsonDocument.Parse(File.ReadAllText input)
let rows =
    errors.RootElement.EnumerateArray()
    |> Seq.map(fun row ->
        let vd = row.GetProperty("vd").GetDouble()
        let vg = row.GetProperty("vg").GetDouble()
        let reference = drainSourceCurrent vg vd
        let engine = row.GetProperty("engine").GetDouble()
        let stock = row.GetProperty("oracle").GetDouble()
        {|vd=vd;vg=vg;reference=reference;engine=engine;stock=stock
          engine_error=abs(engine-reference);stock_error=abs(stock-reference)|})
    |> Seq.toArray
File.WriteAllText(output,JsonSerializer.Serialize(rows,JsonSerializerOptions(WriteIndented=true)))
errors.Dispose()
printfn "Wrote %d finite reference points." rows.Length
