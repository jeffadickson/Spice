#:property PublishAot=false
#:project ../dotnet/Simulator.RequestedQualification/Simulator.RequestedQualification.csproj
using System.Text.Json;
using Simulator.RequestedQualification;

// Independent closed-form equations for hash-pinned originals. This does not
// evaluate the compiler's expression DTOs or replace stock-ngspice status.
if(args.Length is not (3 or 4))throw new ArgumentException("inventory summary output [refined-native-directory]");
using var inventory=JsonDocument.Parse(File.ReadAllText(args[0]));
using var summary=JsonDocument.Parse(File.ReadAllText(args[1]));
var pins=inventory.RootElement.GetProperty("rows").EnumerateArray().ToDictionary(r=>r.GetProperty("path").GetString()!,r=>r.GetProperty("result").GetProperty("Sha256").GetString()!,StringComparer.Ordinal);
var casesRoot=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!,"cases");
var results=new List<object>();var index=0;
foreach(var row in summary.RootElement.GetProperty("rows").EnumerateArray()) {
    var path=row.GetProperty("path").GetString()!;
    if(!pins.TryGetValue(path,out var pin)||pin!=row.GetProperty("input_sha256").GetString())throw new InvalidDataException("unrecognized_input_hash");
    var stem=Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
    var operation=row.GetProperty("operations")[0];
    if(operation.GetProperty("kind").GetString()!="dc")throw new InvalidDataException("not_original_dc");
    var maps=operation.GetProperty("comparison").GetProperty("observables").EnumerateArray().Select(m=>(Engine:m.GetProperty("engine_signal").GetString()!,Native:m.GetProperty("native_signal").GetString()!.ToLowerInvariant(),Unit:m.GetProperty("unit").GetString()!)).ToArray();
    var points=operation.GetProperty("requested_coordinates").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
    var failed=0;var samples=0;var voltageMax=0d;var currentMax=0d;
    for(var i=0;i<points.Length;i++) {
        using var response=JsonDocument.Parse(File.ReadAllText(Path.Combine(casesRoot,index.ToString("D4"),"requested_0",i.ToString("D5")+".response.json")));
        if(response.RootElement.GetProperty("status").GetString()!="succeeded")throw new InvalidDataException("missing_successful_engine_point");
        var ids=response.RootElement.GetProperty("signals").EnumerateArray().Select(v=>v.GetProperty("id").GetString()!).ToArray();
        var values=response.RootElement.GetProperty("samples")[0].GetProperty("values").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
        var actual=ids.Zip(values).ToDictionary(v=>v.First,v=>v.Second,StringComparer.Ordinal);
        foreach(var map in maps) {
            if(!actual.TryGetValue(map.Engine,out var value))throw new InvalidDataException("missing_engine_observable");
            var expected=Expected(stem,map.Native,points[i]);var error=Math.Abs(value-expected);
            var absolute=map.Unit=="V"?1e-6:map.Unit=="A"?1e-9:throw new InvalidDataException("unknown_unit");
            if(error>absolute+1e-4*Math.Max(Math.Abs(value),Math.Abs(expected)))failed++;
            if(map.Unit=="V")voltageMax=Math.Max(voltageMax,error);else currentMax=Math.Max(currentMax,error);
            samples++;
        }
    }
    object NativeCheck(string rawPath) {
        var raw=AsciiRaw.Read(File.ReadAllText(rawPath));if(raw.Complex)throw new InvalidDataException("unexpected_complex_reference");
        var columns=raw.Variables.Select((v,i)=>(v.Name,Index:i)).ToDictionary(v=>v.Name,v=>v.Index,StringComparer.OrdinalIgnoreCase);
        if(!raw.Variables[0].Name.Contains("sweep",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("missing_original_sweep_coordinate");
        var failures=0;var maximum=0d;var checkedSamples=0;
        foreach(var point in raw.Points)foreach(var map in maps) {
            if(!columns.TryGetValue(map.Native,out var column))throw new InvalidDataException("missing_native_observable");
            var value=point[column].Real;var expected=Expected(stem,map.Native,point[0].Real);var error=Math.Abs(value-expected);
            var absolute=map.Unit=="V"?1e-6:1e-9;
            if(error>absolute+1e-4*Math.Max(Math.Abs(value),Math.Abs(expected)))failures++;
            maximum=Math.Max(maximum,error);checkedSamples++;
        }
        return new{signal_samples=checkedSamples,failed=failures,maximum_absolute_error_mixed_units=maximum,agreed=failures==0};
    }
    var stock=NativeCheck(Path.Combine(casesRoot,index.ToString("D4"),"requested_0","native.raw"));
    var refined=args.Length==4?NativeCheck(Path.Combine(args[3],index.ToString("D4")+".raw")):null;
    results.Add(new{path,input_sha256=pin,points=points.Length,signal_samples=samples,failed,voltage_maximum_error=voltageMax,current_maximum_error=currentMax,agreed=failed==0,stock_native_vs_polynomial=stock,refined_native_vs_polynomial=refined});index++;
}
File.WriteAllText(args[2],JsonSerializer.Serialize(new{scope="Independent hash-pinned polynomial equations; stock-native default status remains separate",cases=results},new JsonSerializerOptions{WriteIndented=true}));

static double Expected(string stem,string signal,double x) {
    if(stem is "bsrc_a1" or "bsrc_a2")return signal switch {
        "v(1)"=>x,"v(2)"=>20*x*x+13,"i(v1)"=>-x/10000,"i(b1)"=>-(20*x*x+13)/10000,
        _=>throw new InvalidDataException("unknown_polynomial_observable:"+signal)};
    if(stem is "subckt_i1" or "subckt_i2" or "subckt_i3" or "subckt_i3_hs")return signal switch {
        "v(1)"=>x,"v(2)"=>x*x,"v(3)"=>x*x/3,"v(4)"=>2*x*x,"v(5)"=>2*x*x/3,"i(v1)"=>-x/1000,
        "i(b.x1.b1)"=>-x*x/15000,"i(b.x2.b1)"=>-2*x*x/15000,
        _=>throw new InvalidDataException("unknown_hierarchical_observable:"+signal)};
    if(stem is "subckt_k0" or "subckt_k1" or "subckt_k2" or "subckt_k3")return signal switch {
        "v(1)"=>5,"v(2)"=>x,"v(3)"=>100,"v(4)"=>100+5*x,"i(v1)" when stem!="subckt_k3"=>-5,"i(v2)"=>-x,"i(v3)"=>-100,
        "i(b1)" or "i(b.xsubk1.b1)"=>-(100+5*x),"v(xsubk1.1)" when stem=="subckt_k2"=>10000,
        "i(v.xsubk1.v1)" when stem=="subckt_k2"=>-10000,"v(xsubk1.3)" when stem=="subckt_k3"=>5,
        "i(v.xsubk1.v1)" when stem=="subckt_k3"=>-5,"i(b.xsubk1.b2)" when stem=="subckt_k3"=>-5,
        _=>throw new InvalidDataException("unknown_bilinear_observable:"+signal)};
    throw new InvalidDataException("unrecognized_polynomial_deck");
}
