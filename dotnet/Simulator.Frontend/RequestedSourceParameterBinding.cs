using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>Borrowed metadata for explicit root pulse targets in an admitted post-OP program.</summary>
internal static class RequestedSourceParameterBinding
{
    internal static Dictionary<string,double[]> Bind(BridgeResult circuit,IEnumerable<ControlSourceParameterAlterSyntax> alterations)
    {
        var targets=alterations.Select(a=>a.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result=new Dictionary<string,double[]>(StringComparer.OrdinalIgnoreCase);
        foreach(var waveform in circuit.SourceWaveforms.Where(w=>w.Kind==SourceWaveformKind.Pulse&&w.Parameters.Length==7))
        {
            var origin=circuit.Origins.SingleOrDefault(o=>o.WireId==waveform.WireId&&o.InstancePath.IsEmpty);
            if(origin==null)continue;
            var key="@"+origin.LocalName+"[pulse]";
            if(targets.Contains(key))result.Add(key,waveform.Parameters.ToArray());
        }
        return result;
    }
}
