// Independent closed forms for the two named RC/RLC ramp fixtures. Physical
// circuit and waveform validation prevents applying a convenient oracle to a
// different input. All retained samples and signed currents are checked.
open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
// Pass --reference for the same build's Client, Syntax, Frontend and
// RequestedQualification DLLs. Reuse its strict RAW reader; the independent
// analytical equations below do not reuse solver/device implementation.
open Simulator.RequestedQualification
if fsi.CommandLineArgs.Length<>3 then failwith "Expected baseline directory and fresh output JSON"
let baseline=fsi.CommandLineArgs[1]
let output=fsi.CommandLineArgs[2]
if not(Directory.Exists baseline) || not(Directory.Exists(Path.GetDirectoryName(Path.GetFullPath output))) || File.Exists output then failwith "Missing directories or existing output"
let circuits=[|
 """{"nodes":["0","n1","n2"],"elements":[{"kind":"voltage","volts":0,"id":"e0","p":1,"n":0},{"kind":"resistor","ohms":1000,"id":"e1","p":1,"n":2},{"kind":"capacitor","farads":1e-9,"id":"e2","p":2,"n":0}]}"""
 """{"nodes":["0","n1","n2","n3"],"elements":[{"kind":"voltage","volts":0,"id":"e0","p":1,"n":0},{"kind":"resistor","ohms":100,"id":"e1","p":1,"n":2},{"kind":"inductor","henries":0.001,"id":"e2","p":2,"n":3},{"kind":"capacitor","farads":1e-9,"id":"e3","p":3,"n":0}]}"""
|]
let waveform="""{"kind":"pulse","low":0,"high":1,"delay_s":0,"rise_s":1e-6,"fall_s":1e-6,"width_s":2e-6,"period_s":4e-6}"""
let equalJson a b=JsonNode.DeepEquals(JsonNode.Parse(a:string),JsonNode.Parse(b:string))
let exact kind t =
 if kind=0 then
  let u=t/1e-6
  // Avoid subtracting nearly equal numbers in u-1+exp(-u).
  let mutable v=u-1.+exp(-u)
  if u<0.1 then
   let mutable term=u*u/2.
   v<-term
   for k in 3..24 do
    term<-term*(-u)/float k
    v<-v+term
  [|u;v;-(u-v)/1000.|]
 else
  // LC*v'' + RC*v' + v = k*t, with v(0)=v'(0)=0.
  let k,alpha=1e6,50000.
  let w=sqrt(1e12-alpha*alpha)
  let a=0.1
  let b=(alpha*a-k)/w
  let v=k*(t-1e-7)+exp(-alpha*t)*(a*cos(w*t)+b*sin(w*t))
  let i=1e-3*(1.-exp(-alpha*t)*(cos(w*t)+alpha/w*sin(w*t)))
  [|k*t;k*t-100.*i;v;-i;i|]
let results=[|
 for kind in 0..1 do
  let folder=Path.Combine(baseline,"cases",kind.ToString("D4"),"requested_2")
  use request=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"00000.request.json")))
  let options=request.RootElement.GetProperty("analysis").GetProperty("options")
  if request.RootElement.GetProperty("analysis").GetProperty("kind").GetString()<>"transient_configured" ||
     options.GetProperty("sources").GetArrayLength()<>1 || options.GetProperty("sources").[0].GetProperty("id").GetString()<>"e0" ||
     not(equalJson (request.RootElement.GetProperty("circuit").GetRawText()) circuits[kind]) || options.GetProperty("stop_s").GetDouble()<>1e-6 ||
     not(equalJson (options.GetProperty("sources").[0].GetProperty("waveform").GetRawText()) waveform) then failwith "Outside named physical ramp contract"
  let response=Path.Combine(folder,"00000.response.json")
  if FileInfo(response).Length>16L*1024L*1024L then failwith "Response budget"
  use doc=JsonDocument.Parse(File.ReadAllText response)
  if not(doc.RootElement.GetProperty("complete").GetBoolean()) || doc.RootElement.GetProperty("status").GetString()<>"succeeded" then failwith "Incomplete engine response"
  if doc.RootElement.GetProperty("case_id").GetString()<>request.RootElement.GetProperty("case_id").GetString() then failwith "Engine case identity"
  let actual=[|for p in doc.RootElement.GetProperty("samples").EnumerateArray() -> p.GetProperty("time_s").GetDouble(),[|for v in p.GetProperty("values").EnumerateArray()->v.GetDouble()|]|]
  let n=if kind=0 then 4 else 6
  let signalIds=if kind=0 then [|"v(n1)";"v(n2)";"i(e0)"|] else [|"v(n1)";"v(n2)";"v(n3)";"i(e0)";"i(e2)"|]
  if [|for s in doc.RootElement.GetProperty("signals").EnumerateArray()->s.GetProperty("id").GetString()|]<>signalIds then failwith "Engine observable identity"
  for j,s in doc.RootElement.GetProperty("signals").EnumerateArray() |> Seq.indexed do
   let voltage=j<(if kind=0 then 2 else 3)
   if s.GetProperty("unit").GetString()<>(if voltage then "V" else "A") || s.GetProperty("quantity").GetString()<>(if voltage then "voltage" else "current") then failwith "Engine observable units"
  let raw=Path.Combine(folder,"native.raw")
  if FileInfo(raw).Length>64L*1024L*1024L then failwith "RAW budget"
  let table=AsciiRaw.Read(File.ReadAllText raw)
  if table.Complex || table.PlotName<>"Transient Analysis" || table.Variables.Length<>n || table.Variables[0].Name<>"time" || table.Variables[0].Dimension<>"time" then failwith "RAW identity/shape"
  let expected=if kind=0 then [|"v(in)";"v(out)";"i(v1)"|] else [|"v(in)";"v(mid)";"v(out)";"i(v1)";"i(l1)"|]
  let columns=expected |> Array.mapi(fun j name->
    let i=table.Variables |> Seq.findIndex(fun v->v.Name=name)
    let unit=if j<(if kind=0 then 2 else 3) then "voltage" else "current"
    if table.Variables[i].Dimension<>unit then failwith "RAW observable unit"
    i)
  let native=[|for p in table.Points -> p[0].Real,columns|>Array.map(fun i->p[i].Real)|]
  for label,points in ["engine",actual;"native",native] do
   if points.Length<2 || fst points[0]<>0. || fst points[points.Length-1]<>1e-6 ||
      (points|>Array.exists(fun(t,v)->not(Double.IsFinite t)||v.Length<>n-1||Array.exists (Double.IsFinite>>not) v)) ||
      (points|>Array.pairwise|>Array.exists(fun((a,_),(b,_))->b<=a)) then failwith "Missing/nonfinite/incomplete samples"
   let errors=Array.zeroCreate (n-1)
   let failures=Array.zeroCreate (n-1)
   for t,values in points do
    let gold=exact kind t
    for j in 0..n-2 do
     let error=abs(values[j]-gold[j])
     errors[j]<-max errors[j] error
     let bound=(if j<(if kind=0 then 2 else 3) then 1e-6 else 1e-9)+1e-4*abs gold[j]
     if error>bound then failures[j]<-failures[j]+1
   yield {|circuit=(if kind=0 then "RC" else "RLC");provider=label;points=points.Length;maximum_absolute_errors=errors;failed_samples=failures|}
|]
File.WriteAllText(output,JsonSerializer.Serialize(results,JsonSerializerOptions(WriteIndented=true)))
if results |> Array.exists(fun r->r.provider="engine" && Array.exists ((<)0) r.failed_samples) then failwith "Analytical engine disagreement"
printfn "Checked all samples; native-reference discrepancies remain explicit in %s" output
