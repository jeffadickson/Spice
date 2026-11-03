using System.Collections.Immutable;
using Simulator.Syntax;
namespace Simulator.Frontend;

/// <summary>An explicitly unexecuted instruction in the selected static-DC profile; ranges refer to original expanded source.</summary>
public sealed record OperatingPointOmission(int ScopeId,string Kind,string Name,string Reason,SourceRange Range);
/// <summary>Selected static operating point with an auditable list of excluded analyses, input controls and observations.</summary>
public sealed record SelectedOperatingPoint(BridgeResult Prepared,ImmutableArray<OperatingPointOmission> Omissions)
{
    /// <summary>True only for a complete supported typed request; numerical accuracy still requires execution/reference evidence.</summary>
    public bool Success=>Prepared.Success;
}
/// <summary>Explicit static-DC selection for bounded R/C/L, independent V/I, linear E/G and smooth PSpice VSWITCH declarations. Does not execute original analysis/control programs.</summary>
public static class OperatingPointSelection
{
    private static readonly ImmutableHashSet<string> Observations=ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
        ".print",".plot",".save",".probe",".meas",".measure",".four",".title");
    private static readonly ImmutableHashSet<string> Analyses=ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,".tf",".noise",".sens",".pz");

    /// <summary>Selects a bounded static operating point instead of executing the deck's analyses, controls or output instructions.</summary>
    /// <remarks>Every omission is reported, including controls in unused bodies. Physical directives such as .global/.ic/.options remain unsupported.
    /// Capacitors and inductors retain typed SI values; the existing Rust operating-point equations supply their open/short steady behavior.
    /// Failed preparation returns no request or omission plan. The strict OperatingPointBridge.Lower API is unchanged.</remarks>
    public static SelectedOperatingPoint Select(DeckSyntax syntax,BridgeLimits? limits=null,string backend="lu")
    {
        ArgumentNullException.ThrowIfNull(syntax);
        var omissions=ImmutableArray.CreateBuilder<OperatingPointOmission>();
        if(syntax.Declarations is {} catalog)
            foreach(var card in catalog.Cards)
            {
                var reason=OmissionReason(card);
                if(reason!=null)omissions.Add(new(card.ScopeId,card.Kind,card.Name,reason,card.Range));
            }
        var omittedCards=omissions.Select(o=>o.Range.Start).ToImmutableHashSet();
        if(syntax.Declarations is {} declarations)
            foreach(var source in declarations.Primitives.Where(p=>p.Excitation?.AcMagnitude!=null))
                omissions.Add(new(source.ScopeId,"source_ac",source.Name,"ac_excitation_not_selected",source.Range));
        var prepared=OperatingPointBridge.LowerSelected(syntax,limits,backend,omittedCards);
        return new(prepared,prepared.Success?omissions.ToImmutable():[]);
    }
    internal static string? OmissionReason(ScopedCard card)=>card.Kind switch
    {
        "control"=>"input_control_not_executed",
        "op"=>"analysis_replaced_by_explicit_op",
        "ac" or "dc" or "tran"=>"analysis_not_selected",
        "extension" when Observations.Contains(card.Name)=>"observation_not_selected",
        "extension" when card.ReportingOptionsOnly=>"reporting_options_not_selected",
        "extension" when Analyses.Contains(card.Name)=>"analysis_not_selected",
        _=>null
    };
}
