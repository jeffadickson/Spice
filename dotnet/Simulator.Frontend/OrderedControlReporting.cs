using System.Numerics;
using Simulator.Client;
using Simulator.Syntax;

namespace Simulator.Frontend;

public static partial class RequestedAnalysis
{
    /// <summary>Executes admitted ordered reporting against one complete source-identified result per operation. No physical dispatch or filesystem access occurs here.</summary>
    public static ComplexControlVectorReport ExecuteOrderedReporting(DeckSyntax syntax,RequestedAnalysisPlan plan,
        IReadOnlyList<RequestedOperationResult> results)
    {
        var selected=NetlistConditionalSelection.Select(syntax);
        if(selected.Diagnostic!=null)throw new InvalidDataException("ordered_control_conditional_selection_failed");
        syntax=selected.Syntax;
        if(!plan.Success||plan.ControlProfile!="ordered_direct_commands"||plan.CircuitPreparation is not {} circuit||
            syntax.Declarations?.ControlBlocks is not [{ScopeId:0} block]||results.Count!=plan.Operations.Length)
            throw new InvalidDataException("ordered_control_not_prepared");
        var plots=new Dictionary<int,IReadOnlyDictionary<string,Complex[]>>();
        for(var index=0;index<plan.Operations.Length;index++)
        {
            var operation=plan.Operations[index];var result=results[index];
            if(result.Id!=operation.Id||!result.Success||operation.ControlOrigin is not {} origin||origin.BlockRange!=block.Range)
                throw new InvalidDataException("ordered_control_result_identity");
            var inputs=new Dictionary<string,Complex[]>(StringComparer.OrdinalIgnoreCase);
            if(operation.PoleZero!=null)
            {
                if(result.PoleZero is not {Success:true} roots||!result.Points.IsEmpty)
                    throw new InvalidDataException("ordered_control_incomplete_pz");
                for(var i=0;i<roots.Poles.Length;i++)inputs.Add($"pole({i+1})",[roots.Poles[i]]);
                for(var i=0;i<roots.Zeros.Length;i++)inputs.Add($"zero({i+1})",[roots.Zeros[i]]);
            }
            else
            {
                if(result.Points.Length!=operation.Requests.Length||result.Points.IsEmpty||
                    result.Points.Any(p=>p.Response?.Succeeded!=true||p.TransportError!=null))
                    throw new InvalidDataException("ordered_control_incomplete_analysis");
                var manifests=result.Points.Select((point,i)=>
                {
                    if(point.Index!=i)throw new InvalidDataException("ordered_control_result_identity");
                    var manifest=ReportedManifest(operation,point.Response!);
                    if(manifest.CaseId!=operation.Requests[i].CaseId)
                        throw new InvalidDataException("ordered_control_result_identity");
                    return manifest;
                }).ToArray();
                if(manifests.Skip(1).Any(m=>!m.Signals.SequenceEqual(manifests[0].Signals)))
                    throw new InvalidDataException("ordered_control_result_identity");
                var signals=manifests[0].Signals;
                if(signals.Any(s=>string.IsNullOrWhiteSpace(s.Id))||signals.Select(s=>s.Id).Distinct(StringComparer.Ordinal).Count()!=signals.Length)
                    throw new InvalidDataException("ordered_control_signal_metadata");
                Complex[][] samples;
                if(operation.Kind=="ac")
                {
                    if(manifests.Any(m=>m.Samples.Length!=0))throw new InvalidDataException("ordered_control_analysis_shape");
                    var ac=manifests.SelectMany(m=>m.AcSamples).ToArray();
                    if(ac.Any(s=>s.Values.Any(v=>!double.IsFinite(v.Real)||!double.IsFinite(v.Imag))))
                        throw new InvalidDataException("ordered_control_nonfinite_result");
                    if(!ac.Select(s=>s.FrequencyHz).SequenceEqual(operation.Coordinates))
                        throw new InvalidDataException("ordered_control_analysis_grid");
                    samples=ac.Select(s=>s.Values.Select(v=>new Complex(v.Real,v.Imag)).ToArray()).ToArray();
                    inputs.Add("frequency",ac.Select(s=>new Complex(s.FrequencyHz,0)).ToArray());
                }
                else
                {
                    if(manifests.Any(m=>m.AcSamples.Length!=0))throw new InvalidDataException("ordered_control_analysis_shape");
                    var real=manifests.SelectMany(m=>m.Samples).ToArray();
                    if(real.Any(s=>!double.IsFinite(s.TimeS)||s.Values.Any(v=>!double.IsFinite(v))))
                        throw new InvalidDataException("ordered_control_nonfinite_result");
                    if(operation.Kind=="op"&&(manifests.Length!=1||real.Length!=1)||
                       operation.Kind=="dc"&&manifests.Any(m=>m.Samples.Length!=1)||
                       operation.Kind=="tran"&&(manifests.Length!=1||real.Zip(real.Skip(1)).Any(p=>p.Second.TimeS<=p.First.TimeS)))
                        throw new InvalidDataException("ordered_control_analysis_shape");
                    samples=real.Select(s=>s.Values.Select(v=>new Complex(v,0)).ToArray()).ToArray();
                    if(operation.Kind=="tran")inputs.Add("time",real.Select(s=>new Complex(s.TimeS,0)).ToArray());
                    if(operation.Kind=="dc"&&operation.CoordinateUnit=="degC")
                        inputs.Add("temp-sweep",operation.Coordinates.Select(v=>new Complex(v,0)).ToArray());
                }
                if(samples.Length is <1 or >4096||samples.Any(s=>s.Length!=manifests[0].Signals.Length))
                    throw new InvalidDataException("ordered_control_analysis_shape");
                void Add(string name,string signal,string quantity,string unit)
                {
                    var column=Array.FindIndex(signals,s=>s.Id==signal);
                    if(column<0)throw new InvalidDataException("ordered_control_missing_signal:"+signal);
                    if(signals[column].Quantity!=quantity||signals[column].Unit!=unit)
                        throw new InvalidDataException("ordered_control_signal_metadata");
                    inputs.TryAdd(name,samples.Select(s=>s[column]).ToArray());
                }
                foreach(var (name,wire) in circuit.RootNodes)
                    if(wire=="0")inputs.TryAdd("v("+name+")",[0]);
                    else Add("v("+name+")","v("+wire+")","voltage","V");
                inputs.TryAdd("v(0)",[0]);
                foreach(var element in circuit.Origins.Where(e=>e.InstancePath.IsEmpty&&e.NativeBranchName!=null))
                {
                    Add("i("+element.NativeBranchName+")","i("+element.WireId+")","current","A");
                    Add(element.NativeBranchName+"#branch","i("+element.WireId+")","current","A");
                    if(circuit.Request!.Circuit.Elements.Single(e=>e.Id==element.WireId) is Voltage source)
                        inputs.TryAdd("@"+element.NativeBranchName+"[dc]",[source.Volts]);
                }
                if(operation.Kind=="op")foreach(var (name,value) in circuit.ModelScalars)inputs.TryAdd(name,[value]);
            }
            plots.Add(origin.CommandRange.Start,inputs);
        }
        return ControlVectorExecution.ExecutePlots(block.Commands,plots,plan.ControlConstants);
    }
}
