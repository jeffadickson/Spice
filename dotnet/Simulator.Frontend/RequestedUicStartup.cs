using System.Collections.Immutable;
using Simulator.Client;
using Simulator.Syntax;

namespace Simulator.Frontend;

public static partial class RequestedAnalysis
{
    // Under UIC, grounded storage consumes the corresponding node hint.
    // Other node voltages follow the algebraic network and are not clamped.
    // Substitute grounded storage with ideal voltage constraints and open
    // zero-current inductors only in this separate consistency solve.
    private static (SimulationRequest,ImmutableArray<PreparedNodeInitialCondition>) UicStartup(
        SimulationRequest request,string id,ImmutableArray<PreparedNodeInitialCondition> hints,
        TransientConfiguration options,SourceRange range,ImmutableDictionary<string,double> initialCurrents)
    {
        if(options.Sources is {Length:>0})
            throw new PreparationFailure("unsupported_uic_waveform",range);

        var hintsByNode=hints.ToDictionary(h=>h.NodeIndex);
        var constrainedNodes=new HashSet<int>();
        var biasElements=new List<Element>();
        var hasStorage=false;
        foreach(var element in request.Circuit.Elements)
        {
            switch(element)
            {
                case Resistor or Voltage or Current:
                    biasElements.Add(element);
                    break;
                case MutualInductance: break;
                case Inductor winding:
                    // The original branch still exists in the transient state
                    // and its initial P-to-N current is the evaluated device hint.
                    hasStorage=true;
                    biasElements.Add(new Current(winding.Id,winding.P,winding.N,initialCurrents.GetValueOrDefault(winding.Id)));
                    break;
                case Capacitor {N:0} or SpiceDiode {N:0,Model:{JunctionFarads:>0,Grading:0,TransitSeconds:0}}:
                    hasStorage=true;
                    if(constrainedNodes.Add(element.P))
                        biasElements.Add(new Voltage(element.Id,element.P,0,hintsByNode.GetValueOrDefault(element.P)?.Volts??0));
                    break;
                default:
                    throw new PreparationFailure("unsupported_uic_device_profile",range);
            }
        }

        if(!hasStorage)
            throw new PreparationFailure("unsupported_transient_uic",range);
        if(request.Circuit.Nodes.Length-1+biasElements.Count(Branch)>128)
            throw new PreparationFailure("initial_condition_state_budget",range);
        var initialization=request with
        {
            CaseId=id+"_uic_startup",
            Circuit=request.Circuit with{Elements=biasElements.ToArray()},
            Analysis=new OperatingPoint(),
            Experiment=null
        };
        return (initialization,hints.Where(h=>constrainedNodes.Contains(h.NodeIndex)).ToImmutableArray());
    }
}
