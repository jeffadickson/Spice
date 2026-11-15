#:property PublishAot=false
#:project ../dotnet/Simulator.RequestedQualification/Simulator.RequestedQualification.csproj
using System.Security.Cryptography;
using System.Text.Json;
using Simulator.RequestedQualification;

// Closed-form voltage laws and KCL of the exact hash-pinned three-resistor
// inverse-trig original. This never evaluates the compiler/model expression tree.
try {
if(args.Length!=2)throw new ArgumentException("summary.json output.json");
using var summary=JsonDocument.Parse(File.ReadAllText(args[0]));
if(!summary.RootElement.GetProperty("provenance_valid").GetBoolean())throw new InvalidDataException("invalid_source_provenance");
var selected=summary.RootElement.GetProperty("rows").EnumerateArray().Select((r,i)=>(Row:r,Index:i)).Single(v=>v.Row.GetProperty("path").GetString()=="tests/xyce/Netlists/ABM_ACOS_ASIN/acos_asin.cir");
var row=selected.Row;
var input=row.GetProperty("input_path").GetString()!;var hash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input)));
if(hash!=row.GetProperty("input_sha256").GetString())throw new InvalidDataException("input_hash_changed");
const string pin="ddfbf8b97eeeac1f3a41be051cc888b80f1f400d41745b5955d948c2a1608429";
if(hash!=pin)throw new InvalidDataException("not_the_reviewed_inverse_trig_fixture");
var operation=row.GetProperty("operations")[0];if(operation.GetProperty("kind").GetString()!="dc")throw new InvalidDataException("wrong_analysis");
var coordinates=operation.GetProperty("requested_coordinates").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
var maps=operation.GetProperty("comparison").GetProperty("observables").EnumerateArray().ToArray();
if(maps.Length!=6)throw new InvalidDataException("incomplete_signal_map");
var expectedNames=new HashSet<string>(new[]{"v(1)","v(2)","v(3)","i(v1)","i(b2)","i(b3)"},StringComparer.Ordinal);
if(!expectedNames.SetEquals(maps.Select(m=>m.GetProperty("native_signal").GetString()!.ToLowerInvariant())))throw new InvalidDataException("unexpected_signal_map");
var folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!,"cases",selected.Index.ToString("D4"),"requested_0");
var nativePath=Path.Combine(folder,"native.raw");
if(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(nativePath)))!=operation.GetProperty("native_raw_sha256").GetString())throw new InvalidDataException("native_hash_changed");
var native=AsciiRaw.Read(File.ReadAllText(nativePath));
if(native.PlotName!="DC transfer characteristic"||native.Complex||native.Points.Length!=coordinates.Length)throw new InvalidDataException("native_shape");
static double Law(string name,double x)=>name switch{"v(1)"=>x,"v(2)"=>Math.Asin(x),"v(3)"=>Math.Acos(x),"i(v1)"=>-x,"i(b2)"=>-Math.Asin(x),"i(b3)"=>-Math.Acos(x),_=>throw new InvalidDataException("unknown_signal")};
var records=new List<object>();var engineFailed=0;
foreach(var map in maps){
 var engineering=map.GetProperty("engine_signal").GetString()!;var name=map.GetProperty("native_signal").GetString()!.ToLowerInvariant();var unit=map.GetProperty("unit").GetString();
 if(unit!=(name.StartsWith('v')?"V":"A"))throw new InvalidDataException("unit_mismatch");
 var absolute=map.GetProperty("absolute_tolerance").GetDouble();var relative=map.GetProperty("relative_tolerance").GetDouble();
 if(absolute!=(unit=="V"?1e-6:1e-9)||relative!=1e-4)throw new InvalidDataException("changed_bounds");
 var nc=Enumerable.Range(0,native.Variables.Length).Single(i=>native.Variables[i].Name.ToLowerInvariant()==name);
 var failures=0;var nativeFailures=0;var max=0d;var nativeMax=0d;
 for(var i=0;i<coordinates.Length;i++){
  using var response=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,i.ToString("D5")+".response.json")));
  using var request=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,i.ToString("D5")+".request.json")));
  var root=response.RootElement;if(root.GetProperty("schema_version").GetString()!="1"||root.GetProperty("case_id").GetString()!=request.RootElement.GetProperty("case_id").GetString())throw new InvalidDataException("engine_identity_mismatch");
  if(!root.GetProperty("complete").GetBoolean()||root.GetProperty("samples").GetArrayLength()!=1||root.GetProperty("signals").GetArrayLength()!=6)throw new InvalidDataException("incomplete_engine_data");
  var column=root.GetProperty("signals").EnumerateArray().Select((s,j)=>(s,j)).Single(v=>v.s.GetProperty("id").GetString()==engineering);
  if(column.s.GetProperty("unit").GetString()!=unit)throw new InvalidDataException("engine_unit_mismatch");
  var actual=root.GetProperty("samples")[0].GetProperty("values")[column.j].GetDouble();var gold=Law(name,coordinates[i]);var error=Math.Abs(actual-gold);
  if(!double.IsFinite(actual)||!double.IsFinite(gold))throw new InvalidDataException("nonfinite_equation_value");
  if(error>absolute+relative*Math.Max(Math.Abs(actual),Math.Abs(gold)))failures++;max=Math.Max(max,error);
  var nx=native.Points[i][0].Real;var ng=Law(name,nx);var nv=native.Points[i][nc].Real;var ne=Math.Abs(nv-ng);nativeMax=Math.Max(nativeMax,ne);
  if(ne>absolute+relative*Math.Max(Math.Abs(nv),Math.Abs(ng)))nativeFailures++;
 }
 engineFailed+=failures;records.Add(new{native_signal=name,unit,points=coordinates.Length,engine_failed=failures,engine_maximum_absolute_error=max,stock_native_failed=nativeFailures,stock_native_maximum_absolute_error=nativeMax});
}
File.WriteAllText(args[1],JsonSerializer.Serialize(new{scope="Independent inverse-trig equations and 1-ohm KCL; does not replace stock-default qualification",input_sha256=hash,engine_failed=engineFailed,observables=records},new JsonSerializerOptions{WriteIndented=true}));
return engineFailed==0?0:1;

} catch(Exception error) when(error is InvalidDataException or IOException or ArgumentException or JsonException) {
 Console.Error.WriteLine(JsonSerializer.Serialize(new{status="failed",error=error.GetType().Name,message=error.Message}));
 return 2;
}