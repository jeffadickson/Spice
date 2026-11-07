using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Simulator.Client;
using Simulator.Frontend;
using Simulator.Syntax;
namespace Simulator.RequestedQualification;
/// <summary>One native ASCII RAW variable, retaining native identity and dimension spelling.</summary>
public sealed record RawVariable(string Name,string Dimension);
/// <summary>One independently evaluated original analysis; values retain column and point ordering.</summary>
public sealed record NativeRaw(bool Complex,ImmutableArray<RawVariable> Variables,ImmutableArray<ComplexValue[]> Points)
{
 /// <summary>Native plot identity, independently checked against the retained requested directive.</summary>
 public string PlotName { get; init; } = "";
}
/// <summary>Bounded ASCII RAW reader. Existing benchmark print-table readers cannot represent descending DC or full complex RAW columns.</summary>
public static class AsciiRaw
{
 /// <summary>Reads exactly one plot, refusing binary, non-finite data, duplicate columns, malformed shapes and extra plots.</summary>
 public static NativeRaw Read(string text)
 {
  ArgumentNullException.ThrowIfNull(text);if(text.Length>64*1024*1024)throw new InvalidDataException("raw_character_budget");
  using var reader=new StringReader(text);var count=-1;var variables=-1;bool? complex=null;string? line;string? plotName=null;
  while((line=reader.ReadLine())!="Variables:"){
   if(line==null)throw new InvalidDataException("missing_raw_variables");
   if(line.StartsWith("Plotname:",StringComparison.Ordinal)){if(plotName!=null)throw new InvalidDataException("duplicate_plot_identity");plotName=line[9..].Trim();}
   if(line.StartsWith("Flags:",StringComparison.Ordinal)){if(complex!=null)throw new InvalidDataException("duplicate_flags");complex=line[6..].Trim() switch{"real"=>false,"complex"=>true,_=>throw new InvalidDataException("unsupported_raw_flags")};}
   if(line.StartsWith("No. Variables:",StringComparison.Ordinal)){if(variables!=-1)throw new InvalidDataException("duplicate_variable_count");variables=Count(line[14..],2048);}
   if(line.StartsWith("No. Points:",StringComparison.Ordinal)){if(count!=-1)throw new InvalidDataException("duplicate_point_count");count=Count(line[11..],100000);}
   if(line.StartsWith("Binary:",StringComparison.Ordinal))throw new InvalidDataException("binary_raw_not_supported");
  }
  if(string.IsNullOrWhiteSpace(plotName)||count<1||variables<1||complex==null||(long)count*variables>4_000_000)throw new InvalidDataException("raw_shape_budget");
  var names=ImmutableArray.CreateBuilder<RawVariable>();var identities=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  for(var i=0;i<variables;i++){
   var parts=Words(reader.ReadLine());
   // ngspice marks logarithmic frequency display scales with grid=3; it does not change numeric samples.
   var displayGrid=parts.Length==4&&parts[1]=="frequency"&&parts[2]=="frequency"&&parts[3]=="grid=3";
   if(parts.Length!=3&&!displayGrid||parts[0]!=i.ToString(CultureInfo.InvariantCulture)||!identities.Add(parts[1]))throw new InvalidDataException("invalid_raw_variable");
   names.Add(new(parts[1],parts[2]));
  }
  if(reader.ReadLine()!="Values:")throw new InvalidDataException("missing_ascii_values");
  var points=ImmutableArray.CreateBuilder<ComplexValue[]>();
  for(var i=0;i<count;i++){
   var values=new ComplexValue[variables];
   for(var v=0;v<variables;v++){
    var row=reader.ReadLine();if(v==0)while(row!=null&&string.IsNullOrWhiteSpace(row))row=reader.ReadLine();
    var words=Words(row);if(words.Length!=(v==0?2:1)||v==0&&words[0]!=i.ToString(CultureInfo.InvariantCulture))throw new InvalidDataException("invalid_raw_point_index_or_shape");
    var pair=words[^1].Split(',');if(pair.Length!=(complex.Value?2:1))throw new InvalidDataException("invalid_raw_complex_shape");
    values[v]=new(Number(pair[0]),complex.Value?Number(pair[1]):0);
   }
   points.Add(values);
  }
  if(!string.IsNullOrWhiteSpace(reader.ReadToEnd()))throw new InvalidDataException("extra_raw_plot_or_data");
  return new(complex.Value,names.ToImmutable(),points.ToImmutable()){PlotName=plotName};
 }
 private static string[] Words(string? line)=>line?.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)??throw new InvalidDataException("truncated_raw");
 private static int Count(string value,int maximum)=>int.TryParse(value.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out var n)&&n>=1&&n<=maximum?n:throw new InvalidDataException("invalid_raw_count");
 private static double Number(string text)=>double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)?value:throw new InvalidDataException("nonfinite_or_invalid_raw_number");
}
/// <summary>Generated-range omission with its explicit oracle-only reason.</summary>
public sealed record ProjectionOmission(string Kind,string Reason,SourceRange Range);
/// <summary>Original physical source with exactly one retained original analysis; no evaluated argument is copied from the engine request.</summary>
public sealed record NativeProjection(string Text,ImmutableArray<ProjectionOmission> Omissions)
{
 /// <summary>Control jobs use a qualification-owned relative RAW write, rather than batch -r streaming.</summary>
 public bool RequiresControlWrite { get; init; }
 /// <summary>Exact inserted oracle-only output/exit instructions; original reporting and external IO never execute.</summary>
 public ImmutableArray<string> Instrumentation { get; init; } = [];
}
/// <summary>Projects independent requested analyses using generated physical ranges only.</summary>
public static class OriginalProjection
{
 /// <summary>Masks other analyses and recorded reporting/observations; only validated finite control dispatch is eligible for an original-command oracle projection.</summary>
 public static NativeProjection Create(string expanded,DeckSyntax syntax,RequestedOperation operation,RequestedAnalysisPlan plan)
 {
  ArgumentNullException.ThrowIfNull(expanded);ArgumentNullException.ThrowIfNull(syntax);ArgumentNullException.ThrowIfNull(operation);ArgumentNullException.ThrowIfNull(plan);
  if(!plan.Success||syntax.Declarations is not {} catalog)throw new InvalidDataException("unsafe_or_unprepared_projection");
  if(operation.ControlOrigin is {} controlOrigin)return Control(expanded,syntax,operation,plan,controlOrigin);
  if(catalog.Cards.Any(c=>c.Kind=="control"))throw new InvalidDataException("unsafe_or_unprepared_projection");
  if(!catalog.Analyses.Any(a=>a.ScopeId==0&&a.Range==operation.Range&&a.Kind==operation.Kind))throw new InvalidDataException("analysis_identity_gap");
  var omissions=catalog.Analyses.Where(a=>a.Range!=operation.Range).Select(a=>new ProjectionOmission(a.Kind,"other_requested_analysis_not_in_this_oracle_job",a.Range))
   .Concat(ReportingOmissions(plan)).OrderBy(o=>o.Range.Start).ToImmutableArray();
  var chars=expanded.ToCharArray();var end=0;
  foreach(var omission in omissions){var range=omission.Range;if(range.Start<end||range.Start<0||range.Length<0||range.Start>chars.Length-range.Length)throw new InvalidDataException("invalid_projection_ranges");end=range.Start+range.Length;for(var i=range.Start;i<end;i++)if(chars[i] is not ('\r' or '\n'))chars[i]=' ';}
  var text=new string(chars);var parsed=DeckParser.Parse(text,collectDeclarations:true);
  if(parsed.Status!="accepted_structure"||parsed.Declarations?.Analyses.Length!=1||parsed.Declarations.Analyses[0].Range!=operation.Range||parsed.Declarations.Analyses[0].Kind!=operation.Kind)throw new InvalidDataException("projected_analysis_identity_gap");
  return new(text,omissions);
 }
 // Preserve physical source cards even when a particular analysis has no use
 // for them. The independent engine must see the original temperature and IC.
 private static IEnumerable<ProjectionOmission> ReportingOmissions(RequestedAnalysisPlan plan) =>
  plan.Observations.Where(o=>o.Reason is not ("fixed_default_temperature" or "initial_conditions_not_used_by_requested_analyses"))
      .Select(o=>new ProjectionOmission(o.Kind,"reporting_or_observation_not_executed",o.Range));

 private static NativeProjection Control(string expanded,DeckSyntax syntax,RequestedOperation operation,RequestedAnalysisPlan plan,RequestedControlOrigin origin){
  var catalog=syntax.Declarations!;var blocks=catalog.ControlBlocks.Where(b=>b.ScopeId==0&&b.Range==origin.BlockRange).ToArray();
  if(blocks.Length!=1||catalog.ControlBlocks.Length!=1||plan.ControlProfile is not ("direct_commands" or "finite_deck_run"))throw new InvalidDataException("control_projection_identity_gap");
  var block=blocks[0];var commands=block.Commands.Where(c=>c.Range==origin.CommandRange).ToArray();
  if(commands.Length!=1||origin.Kind=="direct"&&(commands[0].Analysis?.Kind!=operation.Kind||operation.Range!=commands[0].Range)||origin.Kind=="run"&&(commands[0].Kind!="run"||origin.DeclaredAnalysisRange!=operation.Range)||origin.Kind is not ("direct" or "run"))throw new InvalidDataException("control_analysis_identity_gap");
  var omissions=catalog.Analyses.Where(a=>origin.Kind=="direct"||a.Range!=operation.Range).Select(a=>new ProjectionOmission(a.Kind,"other_deck_analysis_not_in_this_control_oracle",a.Range))
   .Concat(block.Commands.Where(c=>c.Range!=origin.CommandRange).Select(c=>new ProjectionOmission("control_"+c.Kind,"other_control_instruction_not_in_this_individual_oracle",c.Range)))
   .Concat(ReportingOmissions(plan)).OrderBy(o=>o.Range.Start).ToImmutableArray();
  var characters=expanded.ToCharArray();var end=0;foreach(var omission in omissions){var range=omission.Range;if(range.Start<end||range.Start<0||range.Length<0||range.Start>characters.Length-range.Length)throw new InvalidDataException("invalid_projection_ranges");end=range.Start+range.Length;for(var i=range.Start;i<end;i++)if(characters[i] is not ('\r' or '\n'))characters[i]=' ';}
  var projected=new string(characters);var checkedSyntax=DeckParser.Parse(projected,collectDeclarations:true);
  if(checkedSyntax.Status!="accepted_structure"||checkedSyntax.Declarations?.ControlBlocks.Length!=1||origin.Kind=="run"&&(checkedSyntax.Declarations.Analyses.Length!=1||checkedSyntax.Declarations.Analyses[0].Range!=operation.Range)||origin.Kind=="direct"&&checkedSyntax.Declarations.Analyses.Length!=0)throw new InvalidDataException("projected_control_identity_gap");
  // Constant relative output name avoids interpreting a user-supplied path or
  // original write/shell command. The native process works in its owned case folder.
  ImmutableArray<string> instrumentation=["write native.raw all","quit"];
  return new(projected.Insert(block.CloseRange.Start,string.Join('\n',instrumentation)+"\n"),omissions){RequiresControlWrite=true,Instrumentation=instrumentation};
 }
}
/// <summary>All values for one mapped root observable, with explicit SI tolerances and maximum discrepancy.</summary>
public sealed record ObservableComparison(string EngineSignal,string NativeSignal,string Unit,int Points,int Failed,double MaximumAbsoluteError,double AbsoluteTolerance,double RelativeTolerance);
/// <summary>Independent grid/shape validation outcome; unavailable observables and grid anomalies never count as agreement.</summary>
public sealed record ComparisonResult(string Status,ImmutableArray<ObservableComparison> Observables,ImmutableArray<string> Gaps)
{
 /// <summary>Every required observable and all points passed, without shape or grid gaps.</summary>
 public bool Agreed=>Status=="agreed"&&!Observables.IsEmpty&&Gaps.IsEmpty;
}
/// <summary>Compares complete root voltages and supported root branch currents on the original requested grids.</summary>
public static class RequestedComparison
{
 /// <summary>Validates identity, units, shapes, finiteness and complete grids before any bound checks. Transient interpolation never extrapolates.</summary>
 public static ComparisonResult Compare(RequestedOperation operation,BridgeResult circuit,IReadOnlyList<SimulationResponse> responses,NativeRaw native,double voltageAbsolute=1e-6,double currentAbsolute=1e-9,double relative=1e-4)
 {
  if(!double.IsFinite(voltageAbsolute)||voltageAbsolute<=0||!double.IsFinite(currentAbsolute)||currentAbsolute<=0||!double.IsFinite(relative)||relative<0)throw new ArgumentOutOfRangeException(nameof(relative));
  try{
   if(responses.Count!=operation.Requests.Length||responses.Any(r=>!r.Succeeded))throw new InvalidDataException("incomplete_engine_operation");
   var manifests=responses.Select((response,i)=>{
    var m=RequestedAnalysis.ReportedManifest(operation,response);var request=operation.Requests[i];
    if(m.CaseId!=request.CaseId||m.Backend.Id!=(request.Backend=="lu"?"nalgebra_dense_lu_reference":request.Backend=="faer"?"faer_sparse_lu":"nalgebra_dense_qr_reference")||m.Backend.Gpu||m.Backend.Placement!="cpu"||m.Signals.Select(s=>s.Id).Distinct(StringComparer.Ordinal).Count()!=m.Signals.Length)throw new InvalidDataException("engine_identity_gap");
    if(m.Samples.Any(s=>!double.IsFinite(s.TimeS)||s.Values.Any(v=>!double.IsFinite(v)))||m.AcSamples.Any(s=>!double.IsFinite(s.FrequencyHz)||s.Values.Any(v=>!double.IsFinite(v.Real)||!double.IsFinite(v.Imag))))throw new InvalidDataException("nonfinite_engine_samples");return m;
   }).ToArray();
   var expectedPlot=operation.Kind switch{"op"=>"Operating Point","dc"=>"DC transfer characteristic","ac"=>"AC Analysis","tran"=>"Transient Analysis",_=>""};
   if(native.PlotName!=expectedPlot)throw new InvalidDataException("native_plot_identity_gap");
   var isAc=operation.Kind=="ac";if(native.Complex!=isAc)throw new InvalidDataException("native_analysis_shape_gap");
   var axis=native.Points.Select(p=>p[0].Real).ToArray();double[] actualAxis;
   if(operation.Kind=="op"){
    if(native.Points.Length!=1||manifests.Length!=1||manifests[0].Samples.Length!=1||manifests[0].AcSamples.Length!=0)throw new InvalidDataException("op_shape_gap");actualAxis=[0];
   }else if(operation.Kind=="dc"){
    if(native.Variables[0].Dimension!=(operation.CoordinateUnit=="V"?"voltage":"current")||manifests.Any(m=>m.Samples.Length!=1||m.AcSamples.Length!=0))throw new InvalidDataException("dc_shape_or_unit_gap");actualAxis=operation.Coordinates.ToArray();Grid(actualAxis,axis);
   }else if(isAc){
    if(native.Variables[0].Dimension!="frequency"||manifests.Length!=1||manifests[0].Samples.Length!=0)throw new InvalidDataException("ac_shape_or_unit_gap");actualAxis=manifests[0].AcSamples.Select(s=>s.FrequencyHz).ToArray();Grid(operation.Coordinates.ToArray(),actualAxis);Grid(actualAxis,axis);StrictAxis(axis,true);
   }else if(operation.Kind=="tran"){
    if(native.Variables[0].Dimension!="time"||manifests.Length!=1||manifests[0].AcSamples.Length!=0)throw new InvalidDataException("transient_shape_or_unit_gap");actualAxis=manifests[0].Samples.Select(s=>s.TimeS).ToArray();StrictAxis(axis,false);StrictAxis(actualAxis,false);
        var stop=operation.Arguments[1];var start=operation.OutputStartS;
    if((actualAxis[0]<start&&!Near(actualAxis[0],start))||(axis[0]<start&&!Near(axis[0],start))||start==0&&(!Near(actualAxis[0],0)||!Near(axis[0],0))||!Near(actualAxis[^1],stop)||!Near(axis[^1],stop))throw new InvalidDataException("incomplete_transient_reporting_grid");
   }else throw new InvalidDataException("unsupported_comparison_analysis");
   var mappings=new List<(string Engine,string Native,string Unit)>();var gaps=ImmutableArray.CreateBuilder<string>();
   var internalFallback=circuit.RootNodes.IsEmpty;
   var observableNodes=internalFallback?circuit.InternalNodes:circuit.RootNodes;
   foreach(var node in observableNodes.OrderBy(n=>n.Key,StringComparer.OrdinalIgnoreCase))mappings.Add(("v("+node.Value+")","v("+node.Key+")","V"));
   foreach(var origin in circuit.Origins.Where(o=>(o.InstancePath.IsEmpty||internalFallback)&&o.NativeBranchName!=null)){
    var element=circuit.Request!.Circuit.Elements.Single(e=>e.Id==origin.WireId);
    if(element is Voltage or Inductor or Vcvs or Ccvs){
     var branch=origin.InstancePath.IsEmpty?origin.NativeBranchName:
        origin.NativeBranchName![0]+"."+string.Join('.',origin.InstancePath)+"."+origin.NativeBranchName;
     var name="i("+branch+")";var alternate=branch+"#branch";
     mappings.Add(("i("+origin.WireId+")",native.Variables.Any(v=>v.Name.Equals(name,StringComparison.OrdinalIgnoreCase))?name:alternate,"A"));
    }else gaps.Add("invalid_native_branch_metadata:"+origin.WireId);
   }
   var comparisons=ImmutableArray.CreateBuilder<ObservableComparison>();
   foreach(var mapping in mappings){
    var column=Enumerable.Range(0,native.Variables.Length).Where(i=>native.Variables[i].Name.Equals(mapping.Native,StringComparison.OrdinalIgnoreCase)).ToArray();
    if(column.Length!=1||native.Variables[column[0]].Dimension!=(mapping.Unit=="V"?"voltage":"current")){gaps.Add("missing_native_observable:"+mapping.Native);continue;}
    var columns=manifests.Select(m=>Array.FindIndex(m.Signals,s=>s.Id==mapping.Engine&&s.Unit==mapping.Unit&&s.Quantity==(mapping.Unit=="V"?"voltage":"current"))).ToArray();
    if(columns.Any(c=>c<0)){gaps.Add("missing_engine_observable:"+mapping.Engine);continue;}
    var failed=0;var maximum=0d;var absolute=mapping.Unit=="V"?voltageAbsolute:currentAbsolute;
    for(var i=0;i<actualAxis.Length;i++){
     var expected=operation.Kind=="tran"?Interpolate(native,axis,column[0],actualAxis[i]):native.Points[i][column[0]];
     var actual=isAc?manifests[0].AcSamples[i].Values[columns[0]]:operation.Kind=="dc"?new ComplexValue(manifests[i].Samples[0].Values[columns[i]],0):new ComplexValue(manifests[0].Samples[operation.Kind=="op"?0:i].Values[columns[0]],0);
     var error=System.Numerics.Complex.Abs(new(expected.Real-actual.Real,expected.Imag-actual.Imag));var scale=System.Numerics.Complex.Abs(new(expected.Real,expected.Imag));var bound=absolute+relative*scale;
     if(!double.IsFinite(error)||!double.IsFinite(bound))throw new InvalidDataException("nonfinite_comparison");if(error>bound)failed++;maximum=Math.Max(maximum,error);
    }
    comparisons.Add(new(mapping.Engine,mapping.Native,mapping.Unit,actualAxis.Length,failed,maximum,absolute,relative));
   }
   if(mappings.Count==0)gaps.Add("no_root_observables");
   return new(gaps.Count>0?"observable_gap":comparisons.Any(c=>c.Failed>0)?"numerical_disagreement":"agreed",comparisons.ToImmutable(),gaps.ToImmutable());
  }catch(InvalidDataException e){return new(e.Message,[],[e.Message]);}
 }
 private static void Grid(double[] requested,double[] actual){if(requested.Length==0||requested.Length!=actual.Length||requested.Where((v,i)=>!Near(v,actual[i])).Any())throw new InvalidDataException("original_grid_discrepancy");}
 private static bool Near(double a,double b)=>double.IsFinite(a)&&double.IsFinite(b)&&Math.Abs(a-b)<=Math.Max(double.Epsilon*16,Math.Max(Math.Abs(a),Math.Abs(b))*1e-12);
 private static void StrictAxis(double[] values,bool positive){if(values.Length==0||values.Any(v=>!double.IsFinite(v)||positive&&v<=0||!positive&&v<0)||values.Zip(values.Skip(1)).Any(p=>p.First>=p.Second))throw new InvalidDataException("invalid_axis");}
 private static ComplexValue Interpolate(NativeRaw raw,double[] axis,int column,double coordinate){
  if(coordinate<axis[0]&&!Near(coordinate,axis[0])||coordinate>axis[^1]&&!Near(coordinate,axis[^1]))throw new InvalidDataException("transient_extrapolation");
  if(coordinate<=axis[0])return raw.Points[0][column];if(coordinate>=axis[^1])return raw.Points[^1][column];var index=Array.BinarySearch(axis,coordinate);if(index>=0)return raw.Points[index][column];var right=~index;var left=right-1;var fraction=(coordinate-axis[left])/(axis[right]-axis[left]);var a=raw.Points[left][column];var b=raw.Points[right][column];return new(a.Real+fraction*(b.Real-a.Real),a.Imag+fraction*(b.Imag-a.Imag));
 }
}
/// <summary>One stage's measured completions, known refusals/failures and unattempted or unavailable outcomes.</summary>
public sealed record StageCount(string Name,int Passed,int Refused,int Unknown);
/// <summary>Four-stage progress rendering; unknown outcomes are visibly distinct from zero completions.</summary>
public static class StageBurndown
{
 /// <summary>Counts observed tri-state values. A failed immutable-source gate invalidates all stage claims.</summary>
 public static StageCount Count(string name,IEnumerable<bool?> values,bool provenanceValid=true){var array=values.ToArray();return provenanceValid?new(name,array.Count(v=>v==true),array.Count(v=>v==false),array.Count(v=>v==null)):new(name,0,0,array.Length);}
 /// <summary>Produces a standalone four-stage SVG with numeric unknown counts and no fabricated historical points.</summary>
 public static string Svg(IReadOnlyList<StageCount> stages){
  if(stages.Count!=4||stages.Any(s=>s.Passed<0||s.Refused<0||s.Unknown<0)||stages.Select(s=>s.Passed+s.Refused+s.Unknown).Distinct().Count()!=1)throw new ArgumentException("Four equally scoped nonnegative stages required.",nameof(stages));
  var total=stages[0].Passed+stages[0].Refused+stages[0].Unknown;var svg=new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 1000 350' role='img' aria-label='Four-stage requested analysis qualification'><rect width='1000' height='350' fill='#101827'/><g font-family='sans-serif' fill='#e5e7eb'><text x='24' y='30' font-size='21'>Requested analysis qualification — "+total+" inputs</text><text x='24' y='55' font-size='13'>green: measured completion · red: known refusal/failure · gray: unknown/unattempted</text>");
  for(var i=0;i<4;i++){var s=stages[i];var y=85+i*64;var divisor=Math.Max(1,total);var good=600d*s.Passed/divisor;var bad=600d*s.Refused/divisor;var unknown=600d*s.Unknown/divisor;string N(double n)=>n.ToString("0.###",CultureInfo.InvariantCulture);svg.Append($"<text x='24' y='{y}' font-size='14'>{System.Security.SecurityElement.Escape(s.Name)}</text><rect x='300' y='{y-17}' width='{N(good)}' height='22' fill='#22c55e'/><rect x='{N(300+good)}' y='{y-17}' width='{N(bad)}' height='22' fill='#ef4444'/><rect x='{N(300+good+bad)}' y='{y-17}' width='{N(unknown)}' height='22' fill='#64748b'/><text x='300' y='{y+23}' font-size='12'>{s.Passed} passed / {s.Refused} refused or failed / {s.Unknown} unknown</text>");}
  return svg.Append("</g></svg>").ToString();
 }
}
