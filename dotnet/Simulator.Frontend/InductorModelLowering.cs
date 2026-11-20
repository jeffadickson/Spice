using Simulator.Syntax;
namespace Simulator.Frontend;

// Resolve a constant flux coefficient once, before the solver builds its MNA
// graph. This adapter does not implement saturating or solution-dependent coils.
internal static class InductorModelLowering
{
    internal sealed record Result(double? Henries,double? InitialAmps,BridgeDiagnostic? Error);

    internal static Result Prepare(DeclarationCatalog catalog,PrimitiveSyntax primitive,ParameterEvaluation environment,double temperature)
    {
        Result Fail(string code,SourceRange? range=null)=>new(null,null,new(code,range??primitive.Range));
        var positions=primitive.StorageParameters.Where(p=>p.Name==null).ToArray();
        if(positions.Length>2)return Fail("unsupported_inductor_header");
        NamedDeclaration? model=null;double? scalar=null;
        foreach(var position in positions)
        {
            var value=ParameterEvaluator.EvaluateValue(catalog,environment,position.Value);
            if(value.Value is {} number)
            {
                if(scalar!=null)return Fail("ambiguous_inductance",position.Range);
                scalar=number;continue;
            }
            if(value.Diagnostics.Any(d=>d.Code!="missing_parameter"))return Fail(value.Diagnostics[0].Code,position.Range);
            var lookup=catalog.Lookup(DeclarationKind.Model,position.Value.Text,primitive.ScopeId);
            if(model!=null||lookup.Provisional||lookup.Status!=NameLookupStatus.Found)return Fail("unbound_inductor_model",position.Range);
            model=lookup.Candidates.Single();
        }
        if(model!=null&&(model.ModelFamily!="l"||model.HasOpaqueModelTerms))return Fail("unsupported_inductor_model",model.Range);
        var modelValues=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        var instance=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var term in model?.Parameters??[])
        {
            if(term.Name is not {} key||key.ToLowerInvariant() is not ("l" or "ind" or "tc1" or "tc2" or "tnom")||modelValues.ContainsKey(key))
                return Fail("unsupported_inductor_model_parameter",term.Range);
            var value=ParameterEvaluator.EvaluateValue(catalog,environment,term.Value,model!.ScopeId);
            if(value.Value is not {} number)return Fail(value.Diagnostics[0].Code,term.Range);
            modelValues.Add(key,number);
        }
        foreach(var term in primitive.StorageParameters.Where(p=>p.Name!=null))
        {
            var key=term.Name!;
            if(key.ToLowerInvariant() is not ("l" or "inductance" or "temp" or "dtemp" or "tc1" or "tc2" or "m" or "scale" or "ic"))
                return Fail("unsupported_inductor_instance_parameter",term.Range);
            // L and INDUCTANCE name one field; aliases cannot bypass duplicate checks.
            if(key.Equals("inductance",StringComparison.OrdinalIgnoreCase))key="l";
            if(instance.ContainsKey(key))return Fail("ambiguous_inductor_parameter",term.Range);
            var value=ParameterEvaluator.EvaluateValue(catalog,environment,term.Value);
            if(value.Value is not {} number)return Fail(value.Diagnostics[0].Code,term.Range);
            instance.Add(key,number);
        }
        if(scalar!=null&&instance.ContainsKey("l"))return Fail("ambiguous_inductance");
        // Unlike Xyce's model L multiplier, ngspice L is only a marker flag.
        // An explicit instance value wins over numeric model IND.
        var nominal=instance.TryGetValue("l",out var explicitValue)?explicitValue:scalar??modelValues.GetValueOrDefault("ind",double.NaN);
        var celsius=instance.GetValueOrDefault("temp",temperature);
        if(!instance.ContainsKey("temp"))celsius+=instance.GetValueOrDefault("dtemp");
        var tnom=modelValues.GetValueOrDefault("tnom",27);
        if(!double.IsFinite(celsius)||celsius<=-273.15||tnom<=-273.15)return Fail("invalid_inductor_temperature");
        var delta=celsius-tnom;
        var tc1=instance.GetValueOrDefault("tc1",modelValues.GetValueOrDefault("tc1"));
        var tc2=instance.GetValueOrDefault("tc2",modelValues.GetValueOrDefault("tc2"));
        var scale=instance.GetValueOrDefault("scale",1);var m=instance.GetValueOrDefault("m",1);
        var henries=nominal*(1+tc1*delta+tc2*delta*delta)*scale/m;
        if(nominal<=0||scale<=0||m<=0||!double.IsFinite(henries)||henries<=0)return Fail("invalid_inductance");
        // IC is a total winding current; changing M changes L, never this value.
        return new(henries,instance.TryGetValue("ic",out var initial)?initial:null,null);
    }
}
