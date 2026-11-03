using System.Collections.Immutable;
using Simulator.Syntax;
namespace Simulator.Frontend;
/// <summary>Finite evaluated waveform syntax retained for an explicit later transient selection. Timing defaults are not guessed during DC preparation.</summary>
public sealed record PreparedSourceWaveform(string WireId,SourceWaveformKind Kind,ImmutableArray<double> Parameters,SourceRange Range);
internal static class SourceWaveformPreparation
{
 // ngspice vsrc/isrc load uses time zero when DC is not explicitly given.
 // Timing fields unused by static DC remain metadata; transient admission owns
 // its stop/step-dependent defaults and stricter runtime profile validation.
 internal static bool TryInitial(SourceWaveformKind kind,ImmutableArray<double> p,out double initial)
 {
  initial=0;if(p.Any(v=>!double.IsFinite(v)))return false;
  switch(kind){
   case SourceWaveformKind.Pulse:
    if(p.Length is <2 or >7||(p.Length>2&&p[2]<0))return false;initial=p[0];break;
   case SourceWaveformKind.Sine:
    if(p.Length is <2 or >6||(p.Length>3&&p[3]<0))return false;
    initial=p[0]+p[1]*Math.Sin((p.Length>5?p[5]:0)*(Math.PI/180));break;
   case SourceWaveformKind.Exponential:
    if(p.Length is <2 or >6||(p.Length>2&&p[2]<0)||(p.Length>4&&p[4]<0))return false;initial=p[0];break;
   case SourceWaveformKind.Pwl:
    if(p.Length<2||p.Length%2!=0)return false;
    for(var i=0;i<p.Length;i+=2)if(p[i]<0||(i>0&&p[i]<=p[i-2]))return false;
    initial=p[1];break;
   default:return false;
  }
  return double.IsFinite(initial);
 }
}
