using Simulator.Client;
using Simulator.Syntax;

namespace Simulator.Frontend;

public static partial class RequestedAnalysis
{
    /// <summary>Runs the terminal reporting program over the last completed real analysis.</summary>
    public static ControlVectorReport ExecuteTerminalReporting(DeckSyntax syntax,RequestedAnalysisPlan plan,
        IReadOnlyList<SimulationResponse> responses)
    {
        if(!plan.Success || plan.CircuitPreparation is not {} circuit ||
            syntax.Declarations?.ControlBlocks is not { Length: 1 } blocks)
            throw new InvalidDataException("control_reporting_not_prepared");
        var operation=plan.Operations[^1];
        if(operation.ControlOrigin is not {} origin || origin.BlockRange!=blocks[0].Range ||
           operation.Kind=="ac" || responses.Count!=operation.Requests.Length || responses.Any(r=>!r.Succeeded))
            throw new InvalidDataException("control_reporting_requires_complete_real_analysis");
        var manifests=responses.Select((response,index)=>
        {
            var manifest=ReportedManifest(operation,response);
            if(manifest.CaseId!=operation.Requests[index].CaseId ||
               manifest.Samples.Any(s=>s.Values.Length!=manifest.Signals.Length))
                throw new InvalidDataException("control_reporting_result_identity");
            return manifest;
        }).ToArray();
        if(manifests.Skip(1).Any(m=>!m.Signals.SequenceEqual(manifests[0].Signals)))
            throw new InvalidDataException("control_reporting_result_identity");
        var points=manifests.Sum(m=>(long)m.Samples.Length);
        if(points is <1 or >4096)throw new InvalidDataException("control_reporting_point_budget");
        var samples=manifests.SelectMany(m=>m.Samples).ToArray();
        var inputs=new Dictionary<string,double[]>(StringComparer.OrdinalIgnoreCase);
        foreach(var (name,wire) in circuit.RootNodes)
            Add("v("+name+")","v("+wire+")");
        inputs.TryAdd("v(0)",[0]);
        foreach(var element in circuit.Origins.Where(e=>e.InstancePath.IsEmpty&&e.NativeBranchName!=null))
        {
            Add("i("+element.NativeBranchName+")","i("+element.WireId+")");
            // This is the original source card's DC setting, not the DC sweep axis.
            if(circuit.Request!.Circuit.Elements.Single(e=>e.Id==element.WireId) is Voltage source)
                inputs.TryAdd("@"+element.NativeBranchName+"[dc]",[source.Volts]);
        }
        if(operation.Kind=="tran")inputs.Add("time",samples.Select(s=>s.TimeS).ToArray());
        var prefix=plan.ProgramOmissions.Any(o=>o.Kind=="control_prefix_reporting");
        return ControlVectorExecution.Execute(prefix?blocks[0].Commands:
            blocks[0].Commands.Where(c=>c.Range.Start>origin.CommandRange.Start),inputs,activateAtAnalysis:prefix,constants:plan.ControlConstants);

        void Add(string name,string wire)
        {
            var index=Array.FindIndex(manifests[0].Signals,s=>s.Id==wire);
            if(index>=0)inputs.TryAdd(name,samples.Select(s=>s.Values[index]).ToArray());
        }
    }
}
