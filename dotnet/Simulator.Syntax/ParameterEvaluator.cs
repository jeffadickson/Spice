using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;

/// <summary>A bounded parameter failure at an original assignment location.</summary>
public sealed record ParameterDiagnostic(string Code,string Name,SourceRange Range);
/// <summary>Atomic effective finite values; failures publish an empty value dictionary.</summary>
public sealed record ParameterEvaluation(ImmutableDictionary<string,double> Values,ImmutableArray<ParameterDiagnostic> Diagnostics)
{
    /// <summary>True only when all requested scope parameters were evaluated.</summary>
    public bool Success=>Diagnostics.IsEmpty;
    // Per-scope snapshots retain actual enclosing-instance overrides. They are
    // catalog-branded so unrelated or modified caller results cannot be reused.
    internal DeclarationCatalog? Catalog { get; init; }
    internal int ScopeId { get; init; }
    internal ImmutableDictionary<int,ImmutableDictionary<string,double>> Environments { get; init; } = ImmutableDictionary<int,ImmutableDictionary<string,double>>.Empty;
}
/// <summary>One finite scalar or an explicit source-located failure; missing data is never zero.</summary>
public sealed record ScalarEvaluation(double? Value,ImmutableArray<ParameterDiagnostic> Diagnostics);
/// <summary>Finite, bounded numeric semantics over the existing generated ANTLR expression tree.</summary>
public static class ParameterEvaluator
{
    private static readonly ImmutableDictionary<string,double> Empty=ImmutableDictionary.Create<string,double>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Evaluates lexical scopes from ancestors to the requested scope; later same-scope assignments win.</summary>
    /// <remarks>Each scope's dependency graph is simultaneous; self-reference is a cycle, not an implicit prior-value update.</remarks>
    public static ParameterEvaluation Evaluate(DeclarationCatalog catalog,int scopeId=0)=>EvaluateScope(catalog,scopeId,null);

    /// <summary>Evaluates named call overrides in the caller, then defaults/dependencies in the callee.</summary>
    /// <remarks>Overrides must name header defaults. Collision with a local .param is explicitly unsupported. Caller must be an unmodified evaluation from this catalog and the call scope; evaluated lexical ancestors preserve enclosing-instance overrides.</remarks>
    public static ParameterEvaluation ForInstance(DeclarationCatalog catalog,BoundInstance call,ParameterEvaluation caller)
    {
        ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(call);ArgumentNullException.ThrowIfNull(caller);
        if(!ReferenceEquals(call.Catalog,catalog)||call.Status!=BindingStatus.Bound||call.Header.Family!="X"||call.Target?.BodyScopeId is not {} scope)
            return Failure("unbound_parameter_instance",call.Header.Name,call.Header.Range);
        if(!caller.Success||!ReferenceEquals(caller.Catalog,catalog)||caller.ScopeId!=call.Header.ScopeId||!caller.Environments.TryGetValue(call.Header.ScopeId,out var callerValues)||!ReferenceEquals(callerValues,caller.Values)||caller.Values.Any(p=>!double.IsFinite(p.Value)))return Failure("invalid_caller_parameters",call.Header.Name,call.Header.Range);
        if(call.Header.Parameters.Length>4096)return Failure("parameter_budget",call.Header.Name,call.Header.Range);
        var values=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var parameter in call.Header.Parameters)
        {
            if(parameter.Name==null)return Failure("opaque_parameter_target",call.Header.Name,parameter.Range);
            if(!call.Target.Parameters.Any(p=>StringComparer.OrdinalIgnoreCase.Equals(p.Name,parameter.Name)))
                return Failure("unknown_parameter_override",parameter.Name,parameter.Range);
            if(catalog.Parameters.Any(p=>p.ScopeId==scope&&p.Origin=="param_card"&&StringComparer.OrdinalIgnoreCase.Equals(p.Syntax.Name,parameter.Name)))
                return Failure("ambiguous_local_override",parameter.Name,parameter.Range);
            try{values[parameter.Name]=Expression(parameter.Value.Text,name=>caller.Values.TryGetValue(name,out var value)?value:throw new EvaluationFailure("missing_parameter",name));}
            catch(EvaluationFailure e){return Failure(e.Code,e.Name??parameter.Name,parameter.Range);}
        }
        return EvaluateScope(catalog,scope,values,caller);
    }
    /// <summary>Evaluates a source expression in an unmodified environment belonging to this catalog. An optional declaration scope selects a retained lexical ancestor snapshot, preserving enclosing instance overrides.</summary>
    public static ScalarEvaluation EvaluateValue(DeclarationCatalog catalog,ParameterEvaluation environment,SourceSyntax syntax,int? declarationScopeId=null)
    {
        ArgumentNullException.ThrowIfNull(catalog);ArgumentNullException.ThrowIfNull(environment);ArgumentNullException.ThrowIfNull(syntax);
        if(!environment.Success||!ReferenceEquals(environment.Catalog,catalog)||!environment.Environments.TryGetValue(environment.ScopeId,out var values)||!ReferenceEquals(values,environment.Values))
            return new(null,[new("invalid_parameter_environment","",syntax.Range)]);
        if(declarationScopeId is {} scope&&!environment.Environments.TryGetValue(scope,out values))return new(null,[new("invalid_parameter_environment","",syntax.Range)]);
        try{return new(Expression(syntax.Text,name=>values.TryGetValue(name,out var value)?value:throw new EvaluationFailure("missing_parameter",name)),[]);}
        catch(EvaluationFailure e){return new(null,[new(e.Code,e.Name??"",syntax.Range)]);}
    }
    private static ParameterEvaluation EvaluateScope(DeclarationCatalog catalog,int scopeId,IReadOnlyDictionary<string,double>? overrides,ParameterEvaluation? enclosing=null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if(scopeId<0||scopeId>=catalog.Scopes.Length)throw new ArgumentOutOfRangeException(nameof(scopeId));
        if(!catalog.Resources.IsEmpty||!catalog.DeferredContexts.IsEmpty)return Failure("deferred_parameters","",catalog.Scopes[scopeId].Range);
        var scopes=new Stack<int>();
        var environments=ImmutableDictionary<int,ImmutableDictionary<string,double>>.Empty;
        var result=Empty;var count=0;
        if(enclosing==null)
        {
            for(int? s=scopeId;s!=null;s=catalog.Scopes[s.Value].ParentId)scopes.Push(s.Value);
        }
        else
        {
            // Inherit the evaluated lexical declaration environment, not every
            // local name in a possibly deeper calling scope and not defaults.
            for(var parent=catalog.Scopes[scopeId].ParentId;parent!=null;parent=catalog.Scopes[parent.Value].ParentId)
            {
                if(!enclosing.Environments.TryGetValue(parent.Value,out var inherited))return Failure("missing_enclosing_parameters","",catalog.Scopes[scopeId].Range);
                environments=environments.SetItem(parent.Value,inherited);
            }
            if(catalog.Scopes[scopeId].ParentId is {} parentId)result=environments[parentId];
            scopes.Push(scopeId);
        }
        foreach(var scope in scopes)
        {
            var assignments=new Dictionary<string,ParameterSyntax>(StringComparer.OrdinalIgnoreCase);
            foreach(var p in catalog.Parameters.Where(p=>p.ScopeId==scope))
            {
                if(++count>4096)return Failure("parameter_budget","",p.Syntax.Range);
                if(p.Syntax.Name==null)return Failure("opaque_parameter_target","",p.Syntax.Range);
                assignments[p.Syntax.Name]=p.Syntax;
            }
            var current=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
            if(scope==scopeId&&overrides!=null)foreach(var p in overrides)current[p.Key]=p.Value;
            var active=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var depth=0;
            var origin=catalog.Scopes[scope].Range;
            try
            {
                foreach(var name in assignments.Keys){origin=assignments[name].Range;Resolve(name);}
                result=result.SetItems(current);environments=environments.SetItem(scope,result);
            }
            catch(EvaluationFailure e){return Failure(e.Code,e.Name??"",e.Range??origin);}
            double Resolve(string name)
            {
                if(current.TryGetValue(name,out var value))return value;
                if(!assignments.TryGetValue(name,out var syntax))return result.TryGetValue(name,out value)?value:throw new EvaluationFailure("missing_parameter",name);
                if(!active.Add(name))throw new EvaluationFailure("parameter_cycle",name);
                if(++depth>64)throw new EvaluationFailure("parameter_depth",name);
                try{value=Expression(syntax.Value.Text,Resolve);current[name]=value;return value;}
                catch(EvaluationFailure e){e.Range??=syntax.Range;throw;}
                finally{depth--;active.Remove(name);}
            }
        }
        return new(result,[]){Catalog=catalog,ScopeId=scopeId,Environments=environments};
    }
    private static ParameterEvaluation Failure(string code,string name,SourceRange range)=>new(Empty,[new(code,name,range)]);
    private sealed class EvaluationFailure(string code,string? name=null):Exception(code)
    {internal string Code{get;}=code;internal string? Name{get;}=name;internal SourceRange? Range{get;set;}}

    // Reuse the selected grammar, including its resource guards. Only the
    // adapter wrapper is synthetic; diagnostic origins remain real assignments.
    private static double Expression(string source,Func<string,double> resolve)
    {
        var value=source.Trim();
        if(value.Length>4096)throw new EvaluationFailure("expression_budget");
        if(value.Length>=2&&((value[0]=='{'&&value[^1]=='}')||(value[0]=='\''&&value[^1]=='\'')))value=value[1..^1];
        var text="Parameter expression\n.param __value={"+value+"}\n.end\n";
        if(DeckParser.Parse(text).Status!="accepted_structure")throw new EvaluationFailure("unsupported_expression");
        var lexer=new SpiceLexer(new AntlrInputStream(text));var parser=new SpiceParser(new CommonTokenStream(lexer));
        lexer.RemoveErrorListeners();parser.RemoveErrorListeners();
        var tree=parser.deck();var assignment=tree.statement().Select(s=>s.paramCard()).First(p=>p!=null).assignment(0);
        var expression=assignment.value()?.expression()??throw new EvaluationFailure("unsupported_expression");
        var nodes=0;
        return Eval(expression,0);
        double Eval(SpiceParser.ExpressionContext node,int depth)
        {
            if(++nodes>1024||depth>64)throw new EvaluationFailure("expression_budget");
            var children=node.expression();double Child(int i)=>Eval(children[i],depth+1);
            double result;
            if(node.ChildCount==1)
                result=node.NUMBER()!=null?Number(node.NUMBER().GetText()):node.identifier()!=null?resolve(node.identifier().GetText()):throw new EvaluationFailure("unsupported_expression");
            else if(node.ChildCount==2)
                result=node.GetChild(0).GetText() switch{"+"=>Child(0),"-"=>-Child(0),"!"=>Child(0)==0?1:0,_=>throw new EvaluationFailure("unsupported_expression")};
            else if(node.identifier()!=null&&node.GetChild(1).GetText()=="(")
            {
                var args=children.Select(c=>Eval(c,depth+1)).ToArray();
                result=(node.identifier().GetText().ToLowerInvariant(),args.Length) switch
                {("abs",1)=>Math.Abs(args[0]),("sqrt",1)=>Math.Sqrt(args[0]),("exp",1)=>Math.Exp(args[0]),("ln",1)=>Math.Log(args[0]),
                 // numparam log is natural (unlike some interactive Nutmeg
                 // conventions); nint follows IEEE ties-to-even, int truncates.
                 ("log",1)=>Math.Log(args[0]),("log10",1)=>Math.Log10(args[0]),("sqr",1)=>args[0]*args[0],
                 ("int",1)=>Math.Truncate(args[0]),("nint",1)=>Math.Round(args[0],MidpointRounding.ToEven),
                 ("ceil",1)=>Math.Ceiling(args[0]),("floor",1)=>Math.Floor(args[0]),("sgn",1)=>Math.Sign(args[0]),
                 ("sin",1)=>Math.Sin(args[0]),("cos",1)=>Math.Cos(args[0]),("tan",1)=>Math.Tan(args[0]),
                 ("asin",1)=>Math.Asin(args[0]),("acos",1)=>Math.Acos(args[0]),("atan",1) or ("arctan",1)=>Math.Atan(args[0]),
                 ("sinh",1)=>Math.Sinh(args[0]),("cosh",1)=>Math.Cosh(args[0]),("tanh",1)=>Math.Tanh(args[0]),
                 ("asinh",1)=>Math.Asinh(args[0]),("acosh",1)=>Math.Acosh(args[0]),("atanh",1)=>Math.Atanh(args[0]),
                 ("min",2)=>Math.Min(args[0],args[1]),("max",2)=>Math.Max(args[0],args[1]),
                 ("pow",2)=>Math.Pow(args[0],args[1]),("pwr",2)=>Math.Pow(Math.Abs(args[0]),args[1]),_=>throw new EvaluationFailure("unsupported_expression")};
            }
            else if(node.ChildCount==3&&node.GetChild(0).GetText() is "(" or "{")result=Child(0);
            else if(node.ChildCount==5&&node.GetChild(1).GetText()=="?")result=Child(0)!=0?Child(1):Child(2);
            else if(children.Length==2)
            {
                var op=node.GetChild(1).GetText();var left=Child(0);
                result=op switch
                {
                    "+"=>left+Child(1),"-"=>left-Child(1),"*"=>left*Child(1),"/"=>left/Child(1),"%"=>left%Child(1),
                    "^" or "**"=>Math.Pow(left,Child(1)),"<"=>left<Child(1)?1:0,">"=>left>Child(1)?1:0,
                    "<="=>left<=Child(1)?1:0,">="=>left>=Child(1)?1:0,"=" or "=="=>left==Child(1)?1:0,"!=" or "<>"=>left!=Child(1)?1:0,
                    "&&" or "&"=>left!=0&&Child(1)!=0?1:0,"||" or "|"=>left!=0||Child(1)!=0?1:0,
                    _=>throw new EvaluationFailure("unsupported_expression")
                };
            }
            else throw new EvaluationFailure("unsupported_expression");
            if(!double.IsFinite(result))throw new EvaluationFailure("nonfinite_parameter");
            return result;
        }
    }
    private static double Number(string text)
    {
        // NUM is already a generated NUMBER token. This decodes its engineering
        // multiplier, not the input language. Longest suffixes precede M.
        var match=Regex.Match(text,@"^(?<n>(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:e[+-]?[0-9]+)?)(?<s>[a-z]*)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        if(!match.Success)throw new EvaluationFailure("unsupported_number");
        var number=double.Parse(match.Groups["n"].Value,CultureInfo.InvariantCulture);var suffix=match.Groups["s"].Value.ToLowerInvariant();
        var scale=suffix.StartsWith("meg",StringComparison.Ordinal)?1e6:suffix.StartsWith("mil",StringComparison.Ordinal)?25.4e-6:suffix.FirstOrDefault() switch
        {'t'=>1e12,'g'=>1e9,'k'=>1e3,'m'=>1e-3,'u'=>1e-6,'n'=>1e-9,'p'=>1e-12,'f'=>1e-15,_=>1.0};
        return number*scale;
    }
}
