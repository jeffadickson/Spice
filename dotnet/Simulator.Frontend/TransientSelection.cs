using System.Collections.Immutable;
using Simulator.Client;
using Simulator.Syntax;
namespace Simulator.Frontend;
/// <summary>Explicit transient operation over the supported generated circuit, not execution of the original deck program.</summary>
public sealed record SelectedTransient(BridgeResult Prepared,ImmutableArray<OperatingPointOmission> Omissions)
{
 /// <summary>Prepared request exists and all selected source profiles were admitted atomically.</summary>
 public bool Success=>Prepared.Success;
}
/// <summary>Maps retained source fields to existing Rust waveform models with named stop/step-dependent SPICE defaults.</summary>
public static class TransientSelection
{
 /// <summary>Selects backward Euler with explicit seconds and accepted-step budget. No UIC or original control program is executed.</summary>
 /// <remarks>Source DC bias differing from its transient initial value, overlapping pulse edges, negative delays/damping and extra waveform fields are explicit refusals.</remarks>
 public static SelectedTransient Prepare(DeckSyntax syntax,double stopS,double maxStepS,int maxSteps=10000,BridgeLimits? limits=null,string backend="lu")
  =>PrepareCore(syntax,stopS,maxStepS,maxStepS,maxSteps,limits,backend);
 // Requested TSTEP controls source defaults independently from the integration ceiling.
 internal static SelectedTransient PrepareRequested(DeckSyntax syntax,double stopS,double outputStepS,double maxStepS,int maxSteps,BridgeLimits? limits,string backend)
  =>PrepareCore(syntax,stopS,maxStepS,outputStepS,maxSteps,limits,backend);
 private static SelectedTransient PrepareCore(DeckSyntax syntax,double stopS,double maxStepS,double defaultStepS,int maxSteps,BridgeLimits? limits,string backend)
 {
  SelectedTransient Fail(string code,SourceRange? range)=>new(new(null,ImmutableDictionary.Create<string,string>(StringComparer.OrdinalIgnoreCase),[],[new(code,range)]),[]);
  if(!double.IsFinite(stopS)||stopS<=0||!double.IsFinite(maxStepS)||maxStepS<=0||maxStepS>stopS||maxSteps is <1 or >100000)return Fail("invalid_transient_selection",null);
  if(Math.Ceiling(stopS/maxStepS)>maxSteps)return Fail("transient_step_budget",null);
  var selected=OperatingPointSelection.Select(syntax,limits,backend);if(!selected.Success)return new(selected.Prepared,[]);
  var prepared=selected.Prepared;if(prepared.SourceWaveforms.Length>128)return Fail("transient_source_budget",null);
  var sources=new List<SourceOverride>();
  foreach(var source in prepared.SourceWaveforms){
   if(!SourceWaveformPreparation.TryInitial(source.Kind,source.Parameters,out var initial))return Fail("unsupported_source_waveform",source.Range);
   var element=prepared.Request!.Circuit.Elements.Single(e=>e.Id==source.WireId);
   var nominal=element switch{Voltage v=>v.Volts,Current i=>i.Amps,_=>double.NaN};
   // The current configured runtime initializes from the time-zero source RHS.
   // Never silently substitute it for a distinct explicitly declared DC bias.
   if(nominal!=initial)return Fail("unsupported_transient_dc_bias",source.Range);
   var p=source.Parameters;
   double Field(int i,double fallback)=>p.Length>i?p[i]:fallback;
   Waveform waveform;
   switch(source.Kind){
    case SourceWaveformKind.Pulse:
     var rise=Field(3,defaultStepS);if(rise<=0)rise=defaultStepS;
     var fall=Field(4,defaultStepS);if(fall<=0)fall=defaultStepS;
     var width=p.Length==5?0:Field(5,stopS);if(width<0)width=stopS;
     var period=Field(6,stopS);if(period<=0)period=stopS;
     if(rise+width+fall>period||!double.IsFinite(rise+width+fall))return Fail("unsupported_pulse_overlap",source.Range);
     waveform=new Pulse(p[0],p[1],Field(2,0),rise,fall,width,period);break;
    case SourceWaveformKind.Sine:
     var frequency=Field(2,1/stopS);if(frequency==0)frequency=1/stopS;
     var delay=Field(3,0);var damping=Field(4,0);var phase=Field(5,0)*(Math.PI/180);
     if(!double.IsFinite(frequency)||frequency<=0||delay<0||damping<0||!double.IsFinite(phase))return Fail("unsupported_sine_profile",source.Range);
     waveform=new DampedSine(p[0],p[1],frequency,delay,damping,phase);break;
    case SourceWaveformKind.Pwl:
     waveform=new Pwl(Enumerable.Range(0,p.Length/2).Select(i=>new Point(p[2*i],p[2*i+1])).ToArray());break;
    case SourceWaveformKind.Exponential:
     var td1=Field(2,defaultStepS);if(td1==0)td1=defaultStepS;
     var tau1=Field(3,defaultStepS);if(tau1==0)tau1=defaultStepS;
     var td2=Field(4,td1+defaultStepS);if(td2==0)td2=td1+defaultStepS;
     var tau2=Field(5,defaultStepS);if(tau2==0)tau2=defaultStepS;
     if(td1<0||td2<td1||tau1<=0||tau2<=0||!double.IsFinite(td2))return Fail("unsupported_exponential_profile",source.Range);
     waveform=new Exponential(p[0],p[1],td1,tau1,td2,tau2);break;
    case SourceWaveformKind.FrequencyModulated:
    case SourceWaveformKind.AmplitudeModulated:
     var fm=source.Kind==SourceWaveformKind.FrequencyModulated;
     var carrier=Field(fm?2:4,fm?5/stopS:500/stopS);
     var modulation=Field(fm?4:3,fm?500/stopS:5/stopS);
     // Native SFFM uses its stop-dependent modulation default when the
     // explicit frequency is zero. AM has no such zero substitution.
     if(fm&&modulation==0)modulation=500/stopS;
     var modulationDelay=Field(5,0);var modulationPhase=Field(6,0)*(Math.PI/180);var carrierPhase=Field(7,0)*(Math.PI/180);
     if(!double.IsFinite(carrier)||!double.IsFinite(modulation)||carrier<=0||modulation<=0||!double.IsFinite(modulationPhase)||!double.IsFinite(carrierPhase))return Fail("unsupported_modulated_source_profile",source.Range);
     waveform=fm?new FrequencyModulated(p[0],p[1],carrier,Math.Clamp(Field(3,90),0,carrier/modulation),modulation,modulationDelay,modulationPhase,carrierPhase)
        :new AmplitudeModulated(p[0],p[1],Field(2,1),modulation,carrier,modulationDelay,modulationPhase,carrierPhase);break;
    default:return Fail("unsupported_source_waveform",source.Range);
   }
   sources.Add(new(source.WireId,waveform));
  }
  var request=prepared.Request! with{CaseId="frontend_transient",Analysis=new TransientConfigured(new(stopS,maxStepS,maxSteps,Sources:sources.ToArray()))};
  var omissions=selected.Omissions.Where(o=>o.Kind!="source_transient").Select(o=>o.Reason=="analysis_replaced_by_explicit_op"?o with{Reason="analysis_not_selected"}:o).ToImmutableArray();
  return new(prepared with{Request=request},omissions);
 }
}
