using Simulator.Syntax;
namespace Simulator.Frontend;

// Preparation resolves the constant linear charge coefficient once. This is not
// a voltage-dependent semiconductor capacitance model or a hidden runtime parser.
internal static class CapacitorModelLowering
{
    internal sealed record Result(double? Farads,BridgeDiagnostic? Error);
    internal static Result Prepare(DeclarationCatalog catalog,PrimitiveSyntax primitive,ParameterEvaluation environment,double temperature)
    {
        Result Fail(string code,SourceRange? range=null)=>new(null,new(code,range??primitive.Range));
        var positions=primitive.StorageParameters.Where(p=>p.Name==null).ToArray();
        var named=primitive.StorageParameters.Where(p=>p.Name!=null).ToArray();
        if(primitive.StorageParameters.IsEmpty||positions.Length>2)return Fail("unsupported_capacitor_header");
        NamedDeclaration? model=null;var scalars=new List<double>();
        foreach(var position in positions)
        {
            var value=ParameterEvaluator.EvaluateValue(catalog,environment,position.Value);
            if(value.Value is {} number){scalars.Add(number);continue;}
            if(value.Diagnostics.Any(d=>d.Code!="missing_parameter"))return Fail(value.Diagnostics[0].Code,position.Range);
            var lookup=catalog.Lookup(DeclarationKind.Model,position.Value.Text,primitive.ScopeId);
            if(model!=null||lookup.Provisional||lookup.Status!=NameLookupStatus.Found)return Fail("unbound_capacitor_model",position.Range);
            model=lookup.Candidates.Single();
        }
        if(scalars.Count>1)return Fail("ambiguous_capacitance");
        if(model!=null&&(model.ModelFamily!="c"||model.HasOpaqueModelTerms))return Fail("unsupported_capacitor_model",model.Range);
        var instance=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        var modelValues=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var term in model?.Parameters??[])
        {
            if(term.Name is not {} key||key.ToLowerInvariant() is not ("c" or "cap" or "cj" or "cjsw" or "narrow" or "short" or "tc1" or "tc2" or "tnom")||modelValues.ContainsKey(key))
                return Fail("unsupported_capacitor_model_parameter",term.Range);
            var value=ParameterEvaluator.EvaluateValue(catalog,environment,term.Value,model!.ScopeId);
            if(value.Value is not {} number)return Fail(value.Diagnostics[0].Code,term.Range);
            modelValues.Add(key,number);
        }
        foreach(var term in named)
        {
            var key=term.Name!;
            if(key.ToLowerInvariant() is not ("c" or "l" or "w" or "temp" or "dtemp" or "tc1" or "tc2" or "m" or "scale")||instance.ContainsKey(key))
                return Fail("unsupported_capacitor_instance_parameter",term.Range);
            var value=ParameterEvaluator.EvaluateValue(catalog,environment,term.Value);
            if(value.Value is not {} number)return Fail(value.Diagnostics[0].Code,term.Range);
            instance.Add(key,number);
        }
        if(scalars.Count!=0&&instance.ContainsKey("c"))return Fail("ambiguous_capacitance");
        // ngspice CAP is numeric model capacitance; C is the model marker
        // flag and does not scale charge. Instance C still denotes farads.
        double nominal;
        if(instance.TryGetValue("c",out var explicitValue))nominal=explicitValue;
        else if(scalars.Count!=0)nominal=scalars[0];
        else if(modelValues.TryGetValue("cap",out var fromModel))nominal=fromModel;
        else
        {
            // Explicit dimensions only in this qualified subset. Native default
            // width/length profiles must be qualified before being admitted.
            if(!instance.TryGetValue("l",out var length)||!instance.TryGetValue("w",out var width))return Fail("missing_capacitor_geometry");
            length-=modelValues.GetValueOrDefault("short");width-=modelValues.GetValueOrDefault("narrow");
            if(length<=0||width<=0||!double.IsFinite(length)||!double.IsFinite(width))return Fail("invalid_capacitor_geometry");
            nominal=modelValues.GetValueOrDefault("cj")*length*width+2*modelValues.GetValueOrDefault("cjsw")*(length+width);
        }
        if(instance.Where(p=>p.Key.ToLowerInvariant() is "l" or "w").Any(p=>p.Value<=0))return Fail("invalid_capacitor_geometry");
        var celsius=instance.GetValueOrDefault("temp",temperature);
        if(!instance.ContainsKey("temp"))celsius+=instance.GetValueOrDefault("dtemp");
        var tnom=modelValues.GetValueOrDefault("tnom",27);
        if(!double.IsFinite(celsius)||celsius<=-273.15||tnom<=-273.15)return Fail("invalid_capacitor_temperature");
        var delta=celsius-tnom;
        var tc1=instance.GetValueOrDefault("tc1",modelValues.GetValueOrDefault("tc1"));
        var tc2=instance.GetValueOrDefault("tc2",modelValues.GetValueOrDefault("tc2"));
        var m=instance.GetValueOrDefault("m",1);var scale=instance.GetValueOrDefault("scale",1);
        var farads=nominal*(1+tc1*delta+tc2*delta*delta)*m*scale;
        if(nominal<=0||m<=0||scale<=0||!double.IsFinite(farads)||farads<=0)return Fail("invalid_capacitance");
        return new(farads,null);
    }
}
