using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Simulator.Syntax.Generated;

namespace Simulator.Syntax;

/// <summary>Output and local values from a successful bounded reporting program.</summary>
public sealed record ControlVectorReport(ImmutableArray<string> Output,
    ImmutableDictionary<string,ImmutableArray<double>> Locals, int? ExitCode)
{
    /// <summary>Recoverable condition and assignment diagnostics.</summary>
    public ImmutableArray<string> Diagnostics { get; init; } = [];
    /// <summary>Output captured under logical names. No filesystem is accessed.</summary>
    public ImmutableDictionary<string,string> Redirected { get; init; } = ImmutableDictionary<string,string>.Empty;
}

/// <summary>Executes bounded real-vector reporting against caller-supplied data.</summary>
public static partial class ControlVectorExecution
{
    private const int VectorLimit = 4096;
    private const int LoopLimit = 64;
    [GeneratedRegex("^\u0022(?<prefix>[A-Za-z_0-9]+)\\{\\$(?<variable>[A-Za-z_][A-Za-z_0-9]*)\\}(?<suffix>[A-Za-z_0-9]*)\u0022$", RegexOptions.CultureInvariant)]
    private static partial Regex LoopTemplate();
    [GeneratedRegex(@"\bv\(\$(?<name>[A-Za-z_][A-Za-z_0-9]*)\)", RegexOptions.CultureInvariant|RegexOptions.IgnoreCase)]
    private static partial Regex DynamicVoltage();
    internal static bool IsLoopTemplate(string value)=>value.Length<=128&&LoopTemplate().IsMatch(value);
    [GeneratedRegex(@"\$&(?<name>[A-Za-z_][A-Za-z_0-9]*(?:\([A-Za-z_0-9]+\))?)(?![A-Za-z_0-9.\[(])", RegexOptions.CultureInvariant)]
    private static partial Regex Substitution();
    [GeneratedRegex(@"\$(?<name>[A-Za-z_][A-Za-z_0-9]*)(?![A-Za-z_0-9.\[(])", RegexOptions.CultureInvariant)]
    private static partial Regex ShellVariable();

    /// <summary>Executes a reporting region atomically, returning no output on failure.</summary>
    public static ControlVectorReport Execute(IEnumerable<ControlCommandSyntax> commands,
        IReadOnlyDictionary<string,double[]> inputs, bool activateAtAnalysis = false, IReadOnlyDictionary<string,double>? constants = null)
    {
        var program = commands.Take(257).ToArray();
        if (program.Length > 256 || inputs.Count + (constants?.Count??0) > 256)
            throw new InvalidDataException("control_execution_budget");
        var values = new Dictionary<string,double[]>(StringComparer.OrdinalIgnoreCase);
        var functions = new Dictionary<string,ControlPostprocessingSyntax>(StringComparer.OrdinalIgnoreCase);
        var variables = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var redirected = ImmutableDictionary.CreateBuilder<string,string>(StringComparer.Ordinal);
        var diagnostics = ImmutableArray.CreateBuilder<string>();
        // Const-plot scalars exist before the first dispatch and cannot be
        // overwritten by the bounded reporting program.
        var constantNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(constants!=null)
            foreach(var (name,value) in constants)
            {
                if(!double.IsFinite(value)||!constantNames.Add(name))
                    throw new InvalidDataException("invalid_control_constant");
                values.Add(name,[value]);
            }
        if (!activateAtAnalysis) BindInputs();
        void BindInputs()
        {
        foreach (var (name, vector) in inputs)
        {
            Validate(vector);
            if (!values.TryAdd(name,vector.ToArray()))
                throw new InvalidDataException("duplicate_control_input");
        }
        }
        var bound = !activateAtAnalysis;
        var stack = new Stack<(bool Parent, bool Condition, bool Else)>();
        var output = ImmutableArray.CreateBuilder<string>();
        var active = true;
        int? exit = null;
        var nodes = 0;
        ControlForeachSyntax? loop=null;
        double[] loopValues=[];
        var loopHeader=0;
        var loopIndex=0;
        var commandsExecuted=0;
        for(var cursor=0;cursor<program.Length;cursor++)
        {
            if(++commandsExecuted>4096)throw new InvalidDataException("control_execution_budget");
            var command=program[cursor];
            if (command.Diagnostic != null) throw new InvalidDataException(command.Diagnostic);
            var argument = command.Argument ?? "";
            switch (command.Kind)
            {
                case "comment": break;
                case "analysis" when !bound && stack.Count==0 && exit is null:
                    BindInputs(); bound=true; break;
                case "local_set":
                    if(active && exit is null) variables[command.Settings[0].Name!] = command.Settings[0].Value==null?"TRUE":"";
                    break;
                case "foreach":
                    if(loop!=null||stack.Count!=0||!bound||command.Foreach is not {} header||variables.ContainsKey(header.Variable))
                        throw new InvalidDataException("unsupported_reporting_loop_shape");
                    loop=header;
                    loopHeader=cursor;
                    loopIndex=0;
                    loopValues=exit==null?Resolve(header.Vector,null).ToArray():[];
                    if(loopValues.Length>LoopLimit||loopValues.Any(v=>!double.IsFinite(v)||v<0||v>int.MaxValue||v!=Math.Truncate(v)))
                        throw new InvalidDataException("control_loop_items");
                    active=loopValues.Length>0;
                    if(active)BindLoopValue();
                    break;
                case "loop_set":
                    if(loop==null)throw new InvalidDataException("control_loop_template_outside_loop");
                    if(active&&exit==null){
                        var setting=command.Settings.Single();
                        var match=LoopTemplate().Match(setting.Value!.Text);
                        if(!match.Success||!match.Groups["variable"].Value.Equals(loop.Variable,StringComparison.OrdinalIgnoreCase)
                            ||string.Equals(setting.Name,loop.Variable,StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("invalid_control_loop_template");
                        var node=match.Groups["prefix"].Value+LookupVariable(loop.Variable)+match.Groups["suffix"].Value;
                        if(node.Length>64)throw new InvalidDataException("control_loop_node_budget");
                        variables[setting.Name!]=node;
                    }
                    break;
                case "if":
                    if (stack.Count == 8) throw new InvalidDataException("conditional_depth_budget");
                    var condition = active && exit is null && Condition(argument);
                    stack.Push((active, condition, false));
                    active = condition;
                    break;
                case "else":
                    if (!stack.TryPop(out var branch) || branch.Else)
                        throw new InvalidDataException("invalid_conditional_else");
                    stack.Push((branch.Parent,branch.Condition,true));
                    active = branch.Parent && !branch.Condition;
                    break;
                case "end" when stack.Count==0&&loop!=null:
                    if(exit==null&&++loopIndex<loopValues.Length){BindLoopValue();cursor=loopHeader;break;}
                    variables.Remove(loop.Variable);
                    loop=null;
                    active=true;
                    break;
                case "end":
                    if (!stack.TryPop(out var ended)) throw new InvalidDataException("invalid_conditional_end");
                    active = ended.Parent;
                    break;
                case "postprocessing" when command.Postprocessing is {} declaration:
                    if (!active || exit is not null) break;
                    if (declaration.Kind == ControlPostprocessingKind.Unlet)
                    {
                        foreach (var name in declaration.Names)
                            if (constantNames.Contains(name) || inputs.Keys.Contains(name,StringComparer.OrdinalIgnoreCase) || !values.Remove(name))
                                throw new InvalidDataException("unknown_control_local");
                    }
                    else if (declaration.Kind == ControlPostprocessingKind.Define)
                        functions[declaration.Names[0]] = declaration;
                    else
                    {
                        var name = declaration.Names[0];
                        if (constantNames.Contains(name) || inputs.Keys.Contains(name,StringComparer.OrdinalIgnoreCase))
                            throw new InvalidDataException("control_input_assignment");
                        try { values[name] = Evaluate(declaration.Expression!.Text); }
                        catch (InvalidDataException error) when(error.Message.StartsWith("invalid_control_assignment_rhs:",StringComparison.Ordinal))
                        { diagnostics.Add(error.Message); }
                    }
                    break;
                case "report" or "report_substitution" when command.Name == "echo":
                    if (!active || exit is not null) break;
                    var expanded = Substitution().Replace(argument, match =>
                        Scalar(Resolve(match.Groups["name"].Value,null)).ToString("G",CultureInfo.InvariantCulture));
                    expanded = ShellVariable().Replace(expanded, match => LookupVariable(match.Groups["name"].Value));
                    if (expanded.Contains('$')) throw new InvalidDataException("unsupported_control_substitution");
                    output.Add(expanded.Replace("\"","",StringComparison.Ordinal));
                    break;
                case "report" when command.Name == "print":
                    if (active && exit is null)
                        output.Add(argument+" = "+string.Join(' ',Evaluate(argument).Select(v=>v.ToString("G",CultureInfo.InvariantCulture))));
                    break;
                case "report_redirect":
                    if(active && exit is null)
                    {
                        var target=command.RedirectTarget!;
                        if(target.Length is <1 or >64 || target.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'))
                            throw new InvalidDataException("invalid_control_redirect_target");
                        var value=Scalar(Evaluate(argument));
                        redirected[target]=argument+" = "+value.ToString("0.000000e+00",CultureInfo.InvariantCulture);
                    }
                    break;
                case "quit" or "quit_status":
                    if (active && exit is null) exit = argument == "1" ? 1 : 0;
                    break;
                default: throw new InvalidDataException("unsupported_control_execution:"+command.Name);
            }
        }
        if (!bound) throw new InvalidDataException("missing_control_analysis_boundary");
        if (stack.Count!=0||loop!=null) throw new InvalidDataException("unclosed_reporting_conditional");
        return new(output.ToImmutable(),values.Where(p=>!inputs.Keys.Contains(p.Key,StringComparer.OrdinalIgnoreCase))
            .ToImmutableDictionary(p=>p.Key,p=>p.Value.ToImmutableArray(),StringComparer.OrdinalIgnoreCase),exit) {Diagnostics=diagnostics.ToImmutable(),Redirected=redirected.ToImmutable()};

        void BindLoopValue()=>variables[loop!.Variable]=loopValues[loopIndex].ToString("0",CultureInfo.InvariantCulture);

        string LookupVariable(string name) => variables.TryGetValue(name,out var value)?value:
            throw new InvalidDataException("unknown_control_variable:"+name);

        bool Condition(string expression)
        {
            if(expression.Length==0){diagnostics.Add("missing_control_condition");return false;}
            try
            {
                if(expression.StartsWith('$')&&!expression.StartsWith("$&",StringComparison.Ordinal))
                    return LookupVariable(expression[1..]).Length!=0;
                return Evaluate(expression,condition:true).Any(v=>v!=0);
            }
            catch(InvalidDataException error) when(loop==null&&(error.Message.StartsWith("unknown_control_vector:",StringComparison.Ordinal)||
                error.Message.StartsWith("unknown_control_variable:",StringComparison.Ordinal)))
            { diagnostics.Add(error.Message);return false; }
        }

        double[] Resolve(string name,IReadOnlyDictionary<string,double[]>? locals)
        {
            if(locals?.TryGetValue(name,out var local)==true)return local;
            if(values.TryGetValue(name,out var value))return value;
            // Native reporting accepts v(name) for a named local vector.
            // Prefer an actual circuit voltage whenever both forms exist.
            if(name.StartsWith("v(",StringComparison.OrdinalIgnoreCase)&&name.EndsWith(')'))
            {
                var bare=name[2..^1];
                if(!constantNames.Contains(bare)&&!inputs.Keys.Contains(bare,StringComparer.OrdinalIgnoreCase)
                    &&values.TryGetValue(bare,out value))return value;
            }
            if(locals!=null&&name.Contains('-')&&TryFormalDifference(name,locals) is {} difference)
                return difference;
            throw new InvalidDataException("unknown_control_vector:"+name);
        }

        // BODY lexing preserves hyphenated names. Reuse the arithmetic lexer
        // for an otherwise unresolved token composed only of bound formals.
        double[]? TryFormalDifference(string name,IReadOnlyDictionary<string,double[]> formals)
        {
            var lexer=new SpiceLexer(new AntlrInputStream(name));
            lexer.Mode(SpiceLexer.EXPRESSION);
            lexer.RemoveErrorListeners();
            var tokens=new CommonTokenStream(lexer);
            tokens.Fill();
            var parts=tokens.GetTokens().Where(t=>t.Channel==TokenConstants.DefaultChannel&&t.Type!=TokenConstants.EOF).ToArray();
            if(parts.Length is <3 or >17||parts.Length%2==0||parts[^1].StopIndex!=name.Length-1)return null;
            for(var index=0;index<parts.Length;index++)
                if(index%2==0?(parts[index].Type!=SpiceLexer.WORD||!formals.ContainsKey(parts[index].Text)):
                    parts[index].Type!=SpiceLexer.MINUS)return null;
            var value=formals[parts[0].Text];
            for(var index=2;index<parts.Length;index+=2)value=Binary("-",value,formals[parts[index].Text]);
            return value;
        }

        double[] Evaluate(string expression,IReadOnlyDictionary<string,double[]>? locals=null,int depth=0,bool condition=false)
        {
            if (depth>16 || expression.Length>4096) throw new InvalidDataException("control_expression_budget");
            if (values.TryGetValue(expression,out var exact) && expression.StartsWith('@')) return exact;
            if(loop!=null)
                expression=DynamicVoltage().Replace(expression,match=>"v("+LookupVariable(match.Groups["name"].Value)+")");
            var lexer = new SpiceLexer(new AntlrInputStream(expression)){ControlSemantic=true};
            lexer.Mode(SpiceLexer.BODY);
            lexer.RemoveErrorListeners();
            var tokens = new CommonTokenStream(lexer);
            tokens.Fill();
            // In ngspice these standalone tokens can redirect let/define
            // commands. Only the conditional grammar gives them a safe meaning.
            if (!condition && tokens.GetTokens().Any(t=>t.Text is "<" or ">"))
            {
                var visible=tokens.GetTokens().Where(t=>t.Channel==TokenConstants.DefaultChannel).ToArray();
                if(visible.Length==4 && visible[1].Text==">" &&
                   (char.IsAsciiLetter(visible[2].Text[0])||visible[2].Text[0]=='_') &&
                   visible[2].Text.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_') &&
                   !values.ContainsKey(visible[2].Text))
                    throw new InvalidDataException("invalid_control_assignment_rhs:"+visible[2].Text);
                throw new InvalidDataException("ambiguous_control_redirection");
            }
            if (tokens.Size>256) throw new InvalidDataException("control_expression_budget");
            var parser = new SpiceParser(tokens){ErrorHandler=new BailErrorStrategy()};
            parser.RemoveErrorListeners();
            try
            {
                var tree = parser.expression();
                if (tokens.LA(1)!=TokenConstants.EOF) throw new InvalidDataException("invalid_control_expression");
                return Eval(tree,locals,depth,condition);
            }
            catch (ParseCanceledException error) { throw new InvalidDataException("invalid_control_expression",error); }
        }

        double[] Eval(SpiceParser.ExpressionContext node,IReadOnlyDictionary<string,double[]>? locals,int depth,bool condition)
        {
            if (++nodes>16384 || depth>64) throw new InvalidDataException("control_expression_budget");
            var children=node.expression();
            double[] Child(int i)=>Eval(children[i],locals,depth+1,condition);
            double[] result;
            if (node.voltageReference()!=null ||
                (node.identifier()?.GetText().ToLowerInvariant() is "i" or "v") && node.LPAREN()!=null)
                result=Resolve(node.GetText(),locals);
            else if (node.ChildCount==1)
                result=node.NUMBER()!=null ? [ParameterEvaluator.Number(node.GetText())] :
                    Resolve(node.GetText().Trim('"'),locals);
            else if (node.identifier()!=null && node.LPAREN()!=null)
            {
                var name=node.identifier().GetText().ToLowerInvariant();
                var args=children.Select(child=>Eval(child,locals,depth+1,condition)).ToArray();
                if (functions.TryGetValue(name,out var function) && function.Names.Length==args.Length+1)
                    result=Evaluate(function.Expression!.Text,function.Names.Skip(1).Zip(args)
                        .ToDictionary(p=>p.First,p=>p.Second,StringComparer.OrdinalIgnoreCase),depth+1,condition);
                else result=(name,args.Length) switch
                {
                    ("length",1)=>[args[0].Length],
                    ("abs",1)=>args[0].Select(Math.Abs).ToArray(),
                    ("unitvec",1)=>Ones(Scalar(args[0])),
                    ("vector",1)=>IndexVector(Scalar(args[0])),
                    _=>throw new InvalidDataException("unsupported_control_function:"+name)
                };
            }
            else if (node.QUESTION()!=null)
                result=Scalar(Child(0))!=0?Child(1):Child(2);
            else if (node.ChildCount==2)
                result=node.GetChild(0).GetText() switch
                {
                    "+"=>Child(0), "-"=>Child(0).Select(v=>-v).ToArray(),
                    "!" or "~"=>Child(0).Select(v=>v==0?1d:0d).ToArray(),
                    _=>throw new InvalidDataException("unsupported_control_unary")
                };
            else if (children.Length==1 && node.ChildCount==3)
                result=Child(0);
            else if (children.Length==2 && node.ChildCount==3)
                result=Binary(node.GetChild(1).GetText().ToLowerInvariant(),Child(0),Child(1));
            else throw new InvalidDataException("unsupported_control_expression");
            Validate(result);
            return result;
        }
    }

    private static double Scalar(double[] values) => values.Length==1 ? values[0] :
        throw new InvalidDataException("control_scalar_required");

    private static void Validate(double[] values)
    {
        if (values.Length is <1 or >VectorLimit || values.Any(v=>!double.IsFinite(v)))
            throw new InvalidDataException("invalid_control_vector");
    }

    private static double[] IndexVector(double count)=>count>=1&&count<=VectorLimit&&count==Math.Truncate(count)
        ?Enumerable.Range(0,(int)count).Select(i=>(double)i).ToArray()
        :throw new InvalidDataException("invalid_control_vector_length");

    private static double[] Ones(double count) => count>=1 && count<=VectorLimit && count==Math.Truncate(count)
        ? Enumerable.Repeat(1d,(int)count).ToArray() : throw new InvalidDataException("invalid_unitvec_length");

    private static double[] Binary(string op,double[] a,double[] b)
    {
        if (a.Length!=b.Length && a.Length!=1 && b.Length!=1)
            throw new InvalidDataException("control_vector_shape");
        var result=new double[Math.Max(a.Length,b.Length)];
        for(var i=0;i<result.Length;i++)
        {
            var x=a[a.Length==1?0:i];
            var y=b[b.Length==1?0:i];
            result[i]=op switch
            {
                "+"=>x+y, "-"=>x-y, "*"=>x*y, "/"=>x/y,
                "eq" or "=" or "=="=>x==y?1:0, "!=" or "<>" or "><"=>x!=y?1:0,
                "<"=>x<y?1:0, ">"=>x>y?1:0, "<="=>x<=y?1:0, ">="=>x>=y?1:0,
                "&" or "&&"=>x!=0&&y!=0?1:0, "|" or "||"=>x!=0||y!=0?1:0,
                _=>throw new InvalidDataException("unsupported_control_operator:"+op)
            };
        }
        return result;
    }
}
