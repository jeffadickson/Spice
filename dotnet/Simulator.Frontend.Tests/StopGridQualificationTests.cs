using System.Collections.Immutable;
using Simulator.Client;
using Simulator.RequestedQualification;
using Xunit;
using FsCheck;
using FsCheck.Xunit;
namespace Simulator.Frontend.Tests;
public sealed class StopGridQualificationTests
{
 internal static ResultManifest Engine()=>new("1","retained","succeeded",true,[new("v(n1)","voltage","V"),new("v(n2)","voltage","V"),new("i(e0)","current","A"),new("i(e3)","current","A")],Enumerable.Range(0,101).Select(i=>new RealSample(i*1e-5,[i,i*10d,-i/1000d,-i/100d])).ToArray(),[],[],[],null,new("fixture","cpu",256,false,false,true,true,"fixture"),"retained");
 internal static NativeRaw Native(ResultManifest e)=>new(false,[new("time","time"),new("v(1)","voltage"),new("v(2)","voltage"),new("i(v1)","current"),new("i(a1)","current")],e.Samples.Select(s=>new[]{new ComplexValue(s.TimeS,0)}.Concat(s.Values.Select(v=>new ComplexValue(v,0))).ToArray()).ToImmutableArray()){PlotName="Transient Analysis"};
 [Theory][InlineData(".control\nshell echo bad\n.endc")][InlineData(".include other.cir")][InlineData(".tran 1u 1m")]
 public void Unpinned_physical_or_control_text_is_refused(string input)=>Assert.Throws<InvalidDataException>(()=>StopGridQualification.Prepare(System.Text.Encoding.UTF8.GetBytes(input),Engine()));
 [Fact] public void Grid_gaps_duplicates_dimensions_and_nonfinite_values_never_pass(){
  var e=Engine();var raw=Native(e);Assert.True(StopGridQualification.Compare(e,raw).Agreed);
  Assert.Throws<InvalidDataException>(()=>StopGridQualification.Compare(e,raw with {Points=raw.Points.RemoveAt(50)}));
  Assert.Throws<InvalidDataException>(()=>StopGridQualification.Compare(e,raw with {Points=raw.Points.Insert(50,raw.Points[50])}));
  Assert.Throws<InvalidDataException>(()=>StopGridQualification.Compare(e,raw with {Variables=raw.Variables.SetItem(2,new("v(2)","current"))}));
  var point=raw.Points[50].ToArray();point[1]=new(double.NaN,0);Assert.Throws<InvalidDataException>(()=>StopGridQualification.Compare(e,raw with {Points=raw.Points.SetItem(50,point)}));
  var enginePoint=e.Samples[50] with {TimeS=e.Samples[49].TimeS};var samples=e.Samples.ToArray();samples[50]=enginePoint;Assert.Throws<InvalidDataException>(()=>StopGridQualification.Compare(e with {Samples=samples},raw));
  Assert.Throws<InvalidDataException>(()=>StopGridQualification.Compare(e with {Samples=e.Samples.Take(100).ToArray()},raw));
 }
 [Fact] public void ULP_matching_compares_real_rows_without_resampling_or_interpolation(){
  var e=Engine();var raw=Native(e);var p=raw.Points[50].ToArray();p[0]=new(Math.BitIncrement(Math.BitIncrement(p[0].Real)),0);var r=StopGridQualification.Compare(e,raw with {Points=raw.Points.SetItem(50,p)});Assert.True(r.Agreed);Assert.Equal(2,r.MaximumUlp);Assert.Equal(101,r.Matches.Length);
  p[0]=new(e.Samples[50].TimeS+1e-9,0);Assert.Throws<InvalidDataException>(()=>StopGridQualification.Compare(e,raw with {Points=raw.Points.SetItem(50,p)}));
  p=raw.Points[50].ToArray();p[2]=new(p[2].Real+1,0);Assert.False(StopGridQualification.Compare(e,raw with {Points=raw.Points.SetItem(50,p)}).Agreed);
 }
 [Property(MaxTest=100,Replay="2399,20261103")]
 public bool Row_identity_accepts_only_the_declared_small_ULP_rounding(PositiveInt value){var steps=1+value.Get%64;var e=Engine();var raw=Native(e);var p=raw.Points[50].ToArray();for(var i=0;i<steps;i++)p[0]=new(Math.BitIncrement(p[0].Real),0);raw=raw with {Points=raw.Points.SetItem(50,p)};try{return StopGridQualification.Compare(e,raw).Agreed==(steps<=32);}catch(InvalidDataException){return steps>32;}}
 [Fact] public async Task Invalid_paths_and_existing_output_do_not_dispatch_or_modify_files(){var missing=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));await Assert.ThrowsAsync<FileNotFoundException>(()=>StopGridQualification.RunAsync([missing,missing,missing,missing]));}
 [StopGridFact] public async Task Pinned_native_replay_preserves_original_bytes_and_all_101_common_points(){
  var parent=Environment.GetEnvironmentVariable("SPICE_STOP_GRID_ARTIFACTS")!;var projection=Environment.GetEnvironmentVariable("SPICE_STOP_GRID_PROJECTION")!;var response=Environment.GetEnvironmentVariable("SPICE_STOP_GRID_RESPONSE")!;var native=Environment.GetEnvironmentVariable("SPICE_STOP_GRID_NATIVE")!;
  var output=Path.Combine(parent,"verified-"+Guid.NewGuid().ToString("N"));Assert.Equal(0,await StopGridQualification.RunAsync([projection,response,native,output]));
  var summaryPath=Path.Combine(output,"summary.json");var summary=await File.ReadAllTextAsync(summaryPath);using var d=System.Text.Json.JsonDocument.Parse(summary);var r=d.RootElement;Assert.True(r.GetProperty("provenance_valid").GetBoolean());Assert.True(r.GetProperty("physical_and_analysis_bytes_preserved").GetBoolean());Assert.False(r.GetProperty("primary_statuses_changed").GetBoolean());Assert.Equal(99,r.GetProperty("observed_pauses").GetInt32());var c=r.GetProperty("comparison");Assert.True(c.GetProperty("agreed").GetBoolean());Assert.Equal(101,c.GetProperty("engine_points").GetInt32());Assert.Equal(403,c.GetProperty("native_extra_points").GetInt32());Assert.InRange(c.GetProperty("maximum_ulp").GetDouble(),0,2);
  await Assert.ThrowsAsync<IOException>(()=>StopGridQualification.RunAsync([projection,response,native,output]));Assert.Equal(summary,await File.ReadAllTextAsync(summaryPath));
  var malicious=Path.Combine(parent,"unpinned-"+Guid.NewGuid().ToString("N")+".cir");await File.WriteAllTextAsync(malicious,await File.ReadAllTextAsync(projection)+"\n.control\nshell echo unsafe\n.endc\n");var refused=output+"-refused";await Assert.ThrowsAsync<InvalidDataException>(()=>StopGridQualification.RunAsync([malicious,response,native,refused]));Assert.False(Directory.Exists(refused));
 }
}
public sealed class StopGridFactAttribute:FactAttribute
{
 public StopGridFactAttribute(){if(!Directory.Exists(Environment.GetEnvironmentVariable("SPICE_STOP_GRID_ARTIFACTS"))||new[]{"SPICE_STOP_GRID_PROJECTION","SPICE_STOP_GRID_RESPONSE","SPICE_STOP_GRID_NATIVE"}.Any(name=>!File.Exists(Environment.GetEnvironmentVariable(name))))Skip="Set pinned stop-grid inputs/native and existing artifact directory.";}
}
