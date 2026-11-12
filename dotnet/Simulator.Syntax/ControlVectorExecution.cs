using System.Numerics;
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

/// <summary>Finite complex reporting output; the borrowed physical result arrays are never modified.</summary>
public sealed record ComplexControlVectorReport(ImmutableArray<string> Output,
    ImmutableDictionary<string,ImmutableArray<ControlComplexValue>> Locals,int? ExitCode)
{
    /// <summary>Recoverable condition and assignment diagnostics.</summary>
    public ImmutableArray<string> Diagnostics { get; init; }=[];
    /// <summary>Captured logical output; evaluation does not access the filesystem.</summary>
    public ImmutableDictionary<string,string> Redirected { get; init; }=ImmutableDictionary<string,string>.Empty;
}

/// <summary>Finite report components without derived magnitude/phase properties that could overflow during serialization.</summary>
public readonly record struct ControlComplexValue(double Real,double Imag);

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

    /// <summary>Preserves the real-only reporting contract using the shared bounded evaluator.</summary>
    public static ControlVectorReport Execute(IEnumerable<ControlCommandSyntax> commands,
        IReadOnlyDictionary<string,double[]> inputs,bool activateAtAnalysis=false,IReadOnlyDictionary<string,double>? constants=null)
    {
        if(inputs.Count>256)throw new InvalidDataException("control_execution_budget");
        var converted=new Dictionary<string,Complex[]>(StringComparer.OrdinalIgnoreCase);
        foreach(var (name,vector) in inputs)
        {
            if(vector is null||vector.Length is <1 or >VectorLimit||vector.Any(v=>!double.IsFinite(v)))
                throw new InvalidDataException("invalid_control_vector");
            if(!converted.TryAdd(name,vector.Select(v=>new Complex(v,0)).ToArray()))
                throw new InvalidDataException("duplicate_control_input");
        }
        var result=ExecuteCore(commands,converted,activateAtAnalysis,constants,false,null);
        return new(result.Output,result.Locals.ToImmutableDictionary(p=>p.Key,p=>p.Value.Select(v=>v.Real).ToImmutableArray(),StringComparer.OrdinalIgnoreCase),result.ExitCode)
            {Diagnostics=result.Diagnostics,Redirected=result.Redirected};
    }

    /// <summary>Executes reporting against copied finite complex vectors without an analysis boundary.</summary>
    public static ComplexControlVectorReport ExecuteComplex(IEnumerable<ControlCommandSyntax> commands,
        IReadOnlyDictionary<string,Complex[]> inputs,IReadOnlyDictionary<string,double>? constants=null)
        =>ExecuteCore(commands,CopyInputs(inputs),false,constants,true,null);

    /// <summary>Activates the matching immutable plot at each original analysis source offset. At most 128 plots and 1048576 complex cells are admitted.</summary>
    public static ComplexControlVectorReport ExecutePlots(IEnumerable<ControlCommandSyntax> commands,
        IReadOnlyDictionary<int,IReadOnlyDictionary<string,Complex[]>> plots,IReadOnlyDictionary<string,double>? constants=null)
    {
        ArgumentNullException.ThrowIfNull(plots);
        if(plots.Count is <1 or >128)throw new InvalidDataException("control_plot_budget");
        long cells=0;
        var snapshots=new Dictionary<int,IReadOnlyDictionary<string,Complex[]>>();
        foreach(var (offset,plot) in plots)
        {
            if(offset<0||plot is null||plot.Count>256)throw new InvalidDataException("control_plot_budget");
            foreach(var vector in plot.Values){Validate(vector,true);cells+=vector.Length;}
            if(cells>1_048_576)throw new InvalidDataException("control_plot_budget");
            snapshots.Add(offset,CopyInputs(plot));
        }
        return ExecuteCore(commands,new Dictionary<string,Complex[]>(),true,constants,true,snapshots);
    }

    private static Dictionary<string,Complex[]> CopyInputs(IReadOnlyDictionary<string,Complex[]> inputs)
    {
        if(inputs.Count>256)throw new InvalidDataException("control_execution_budget");
        var result=new Dictionary<string,Complex[]>(StringComparer.OrdinalIgnoreCase);
        foreach(var (name,vector) in inputs)
        {
            Validate(vector,true);
            if(!result.TryAdd(name,vector.ToArray()))throw new InvalidDataException("duplicate_control_input");
        }
        return result;
    }

    /// <summary>Executes a reporting region atomically, returning no output on failure.</summary>
    private static ComplexControlVectorReport ExecuteCore(IEnumerable<ControlCommandSyntax> commands,
        IReadOnlyDictionary<string,Complex[]> initialInputs, bool activateAtAnalysis, IReadOnlyDictionary<string,double>? constants, bool allowComplex, IReadOnlyDictionary<int,IReadOnlyDictionary<string,Complex[]>>? plots)
    {
        var inputs=initialInputs;
        var program = commands.Take(257).ToArray();
        if (program.Length > 256 || inputs.Count + (constants?.Count??0) > 256)
            throw new InvalidDataException("control_execution_budget");
        var values = new Dictionary<string,Complex[]>(StringComparer.OrdinalIgnoreCase);
        var globalValues=new Dictionary<string,Complex[]>(StringComparer.OrdinalIgnoreCase);
        var constantOwners=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plotLocals=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
        void BindInputs(IReadOnlyDictionary<string,Complex[]>? next=null)
        {
        if(next!=null)
        {
            foreach(var name in inputs.Keys)values.Remove(name);
            // Scalar session values and const-plot bindings persist. A vector
            // created in the preceding analysis must not become a new plot's
            // data merely because it has the same unqualified spelling.
            foreach(var name in plotLocals)
                if(globalValues.TryGetValue(name,out var scalar))values[name]=scalar;
                else values.Remove(name);
            plotLocals.Clear();inputs=next;
        }
        foreach (var (name, vector) in inputs)
        {
            Validate(vector,allowComplex);
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
                case "analysis" when plots!=null:
                    if(stack.Count!=0||loop!=null||!active||exit!=null)
                        throw new InvalidDataException("unsupported_conditional_analysis");
                    if(!plots.TryGetValue(command.Range.Start,out var plot))
                        throw new InvalidDataException("missing_control_analysis_plot");
                    BindInputs(plot);bound=true;break;
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
                    loopValues=exit==null?Resolve(header.Vector,null).Select(Real).ToArray():[];
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
                            else {globalValues.Remove(name);constantOwners.Remove(name);plotLocals.Remove(name);}
                    }
                    else if (declaration.Kind == ControlPostprocessingKind.Define)
                        functions[declaration.Names[0]] = declaration;
                    else
                    {
                        var name = declaration.Names[0];
                        if (constantNames.Contains(name) || inputs.Keys.Contains(name,StringComparer.OrdinalIgnoreCase))
                            throw new InvalidDataException("control_input_assignment");
                        try
                        {
                            if(declaration.Kind==ControlPostprocessingKind.Compose&&
                                (declaration.Values.IsEmpty||declaration.Values.Length>VectorLimit))
                                throw new InvalidDataException("control_compose_value_budget");
                            // Evaluate every scalar before replacing the local. The
                            // borrowed input arrays and any previous local stay intact
                            // when a value is missing, nonfinite or nonscalar.
                            var replacement=declaration.Kind==ControlPostprocessingKind.Compose
                                ? declaration.Values.Select(item=>ScalarValue(Evaluate(item.Text))).ToArray()
                                : Evaluate(declaration.Expression!.Text);
                            Validate(replacement,allowComplex);
                            if(!bound)constantOwners.Add(name);
                            if(constantOwners.Contains(name)||replacement.Length==1)
                            {
                                globalValues[name]=replacement;plotLocals.Remove(name);
                            }
                            else plotLocals.Add(name);
                            values[name]=replacement;
                        }
                        catch (InvalidDataException error) when(error.Message.StartsWith("invalid_control_assignment_rhs:",StringComparison.Ordinal))
                        { diagnostics.Add(error.Message); }
                    }
                    break;
                case "report" or "report_substitution" when command.Name == "echo":
                    if (!active || exit is not null) break;
                    var expanded = Substitution().Replace(argument, match =>
                        allowComplex?string.Join(" ",Resolve(match.Groups["name"].Value,null).Select(Format)):Format(ScalarValue(Resolve(match.Groups["name"].Value,null))));
                    expanded = ShellVariable().Replace(expanded, match => LookupVariable(match.Groups["name"].Value));
                    if (expanded.Contains('$')) throw new InvalidDataException("unsupported_control_substitution");
                    output.Add(expanded.Replace("\"","",StringComparison.Ordinal));
                    break;
                case "report" when command.Name == "print":
                    if (active && exit is null)
                        output.Add(argument+" = "+string.Join(' ',Evaluate(argument).Select(v=>Format(v))));
                    break;
                case "report_redirect":
                    if(active && exit is null)
                    {
                        var target=command.RedirectTarget!;
                        if(!ControlReportTarget.IsSafe(target))
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
            .ToImmutableDictionary(p=>p.Key,p=>p.Value.Select(v=>new ControlComplexValue(v.Real,v.Imaginary)).ToImmutableArray(),StringComparer.OrdinalIgnoreCase),exit) {Diagnostics=diagnostics.ToImmutable(),Redirected=redirected.ToImmutable()};

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
                return Evaluate(expression,condition:true).Any(v=>Real(v)!=0);
            }
            catch(InvalidDataException error) when(loop==null&&(error.Message.StartsWith("unknown_control_vector:",StringComparison.Ordinal)||
                error.Message.StartsWith("unknown_control_variable:",StringComparison.Ordinal)))
            { diagnostics.Add(error.Message);return false; }
        }

        Complex[] Resolve(string name,IReadOnlyDictionary<string,Complex[]>? locals)
        {
            if(locals?.TryGetValue(name,out var local)==true)return local;
            if(values.TryGetValue(name,out var value))return value;
            if(allowComplex&&name.Equals("pi",StringComparison.OrdinalIgnoreCase))return [Math.PI];
            if(allowComplex&&name.Equals("i",StringComparison.OrdinalIgnoreCase))return [Complex.ImaginaryOne];
            // Native reporting accepts v(name) for a named local vector.
            // Prefer an actual circuit voltage whenever both forms exist.
            if(name.StartsWith("v(",StringComparison.OrdinalIgnoreCase)&&name.EndsWith(')'))
            {
                var bare=name[2..^1];
                if(!constantNames.Contains(bare)&&!inputs.Keys.Contains(bare,StringComparer.OrdinalIgnoreCase)
                    &&values.TryGetValue(bare,out value))return value;
            }
            throw new InvalidDataException("unknown_control_vector:"+name);
        }

        // BODY lexing preserves '+' and '-' in names. Reuse the arithmetic lexer
        // for an otherwise unresolved token composed only of bound names and
        // numeric constants. Splice tokens before parsing, rather than evaluating
        // a difference as an atom, which would change division/power precedence.
        IToken[]? DifferenceTokens(string name,IReadOnlyDictionary<string,Complex[]>? locals)
        {
            var lexer=new SpiceLexer(new AntlrInputStream(name));
            lexer.Mode(SpiceLexer.EXPRESSION);
            lexer.RemoveErrorListeners();
            var tokens=new CommonTokenStream(lexer);
            tokens.Fill();
            var parts=tokens.GetTokens().Where(t=>t.Channel==TokenConstants.DefaultChannel&&t.Type!=TokenConstants.EOF).ToArray();
            if(parts.Length is <3 or >17||parts.Length%2==0||parts[^1].StopIndex!=name.Length-1)return null;
            for(var index=0;index<parts.Length;index++)
            {
                if(index%2!=0){if(parts[index].Type is not (SpiceLexer.MINUS or SpiceLexer.PLUS))return null;continue;}
                if(parts[index].Type!=SpiceLexer.NUMBER&&
                    (parts[index].Type!=SpiceLexer.WORD||
                     !(locals?.ContainsKey(parts[index].Text)==true||values.ContainsKey(parts[index].Text)||
                       allowComplex&&parts[index].Text.ToLowerInvariant() is "pi" or "i")))return null;
            }
            return parts;
        }

        Complex[] Evaluate(string expression,IReadOnlyDictionary<string,Complex[]>? locals=null,int depth=0,bool condition=false)
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
            var expanded=new List<IToken>();
            var probeParents=new Stack<bool>();
            var inProbe=false;
            IToken? previous=null;
            foreach(var token in tokens.GetTokens())
            {
                if(token.Channel!=TokenConstants.DefaultChannel){expanded.Add(token);continue;}
                if(token.Type==SpiceLexer.LPAREN)
                {
                    probeParents.Push(inProbe);
                    inProbe|=previous?.Text.Equals("v",StringComparison.OrdinalIgnoreCase)==true||
                        previous?.Text.Equals("i",StringComparison.OrdinalIgnoreCase)==true;
                }
                else if(token.Type==SpiceLexer.RPAREN&&probeParents.TryPop(out var parentProbe))inProbe=parentProbe;
                // Circuit terminal names are not arithmetic, and exact local or
                // input names must win even when their spelling contains '-'.
                // BODY identifiers include device-family token kinds (for
                // example a name beginning with 'g'), not just WORD.
                if(!inProbe&&token.Type!=SpiceLexer.STRING&&token.Type!=SpiceLexer.QUOTED&&(token.Text.Contains('-')||token.Text.Contains('+'))&&
                    locals?.ContainsKey(token.Text)!=true&&!values.ContainsKey(token.Text)&&
                    DifferenceTokens(token.Text,locals) is {} difference)expanded.AddRange(difference);
                else expanded.Add(token);
                previous=token;
            }
            tokens=new CommonTokenStream(new ListTokenSource(expanded));
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
            parser.AddParseListener(new DeckParser.RuleDepthGuard());
            try
            {
                var tree = parser.expression();
                if (tokens.LA(1)!=TokenConstants.EOF) throw new InvalidDataException("invalid_control_expression");
                return Eval(tree,locals,depth,condition);
            }
            catch (ParseCanceledException error) { throw new InvalidDataException("invalid_control_expression",error); }
            catch (DeckParser.SyntaxDepthException error) { throw new InvalidDataException("control_expression_budget",error); }
        }

        Complex[] Eval(SpiceParser.ExpressionContext node,IReadOnlyDictionary<string,Complex[]>? locals,int depth,bool condition)
        {
            if (++nodes>16384 || depth>64) throw new InvalidDataException("control_expression_budget");
            var children=node.expression();
            Complex[] Child(int i)=>Eval(children[i],locals,depth+1,condition);
            Complex[] result;
            if (node.voltageReference()!=null ||
                (node.identifier()?.GetText().ToLowerInvariant() is "i" or "v" or "pole" or "zero") && node.LPAREN()!=null)
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
                    ("vecmax",1)=>[args[0].Select(Real).Max()],
                    ("abs",1)=>args[0].Select(v=>new Complex(v.Magnitude,0)).ToArray(),
                    ("ln" or "log",1)=>args[0].Select(v=>allowComplex?Complex.Log(v):new Complex(Math.Log(v.Real),0)).ToArray(),
                    ("log10",1)=>args[0].Select(v=>allowComplex?Complex.Log10(v):new Complex(Math.Log10(v.Real),0)).ToArray(),
                    ("unitvec",1)=>Ones(Scalar(args[0])),
                    ("vector",1)=>IndexVector(Scalar(args[0])),
                    _=>throw new InvalidDataException("unsupported_control_function:"+name)
                };
            }
            else if (node.LBRACKET()!=null)
            {
                var vector=Child(0);
                var index=Scalar(Child(1));
                // Reporting vectors use zero-based integer indexing. Refuse
                // truncation, wrapping and scalar broadcasting at this boundary.
                if(index<0||index>=vector.Length||index!=Math.Truncate(index))
                    throw new InvalidDataException("invalid_control_vector_index");
                result=[vector[(int)index]];
            }
            else if (node.QUESTION()!=null)
                result=Scalar(Child(0))!=0?Child(1):Child(2);
            else if (node.ChildCount==2)
                result=node.GetChild(0).GetText() switch
                {
                    "+"=>Child(0), "-"=>Child(0).Select(v=>-v).ToArray(),
                    "!" or "~"=>Child(0).Select(v=>Real(v)==0?new Complex(1,0):Complex.Zero).ToArray(),
                    _=>throw new InvalidDataException("unsupported_control_unary")
                };
            else if (children.Length==1 && node.ChildCount==3)
                result=Child(0);
            else if (children.Length==2 && node.ChildCount==3)
                result=Binary(node.GetChild(1).GetText().ToLowerInvariant(),Child(0),Child(1));
            else throw new InvalidDataException("unsupported_control_expression");
            Validate(result,allowComplex);
            return result;
        }
    }

    private static double Scalar(Complex[] values)=>Real(ScalarValue(values));
    private static double Real(Complex value)=>value.Imaginary==0?value.Real:throw new InvalidDataException("control_real_required");
    private static string Format(Complex value)=>value.Imaginary==0?value.Real.ToString("G",CultureInfo.InvariantCulture):"("+value.Real.ToString("G",CultureInfo.InvariantCulture)+","+value.Imaginary.ToString("G",CultureInfo.InvariantCulture)+")";
    private static Complex ScalarValue(Complex[] values) => values.Length==1 ? values[0] :
        throw new InvalidDataException("control_scalar_required");

    private static void Validate(Complex[] values,bool allowComplex)
    {
        if (values is null||values.Length is <1 or >VectorLimit || values.Any(v=>!double.IsFinite(v.Real)||!double.IsFinite(v.Imaginary)||!allowComplex&&v.Imaginary!=0))
            throw new InvalidDataException("invalid_control_vector");
    }

    private static Complex[] IndexVector(double count)=>count>=1&&count<=VectorLimit&&count==Math.Truncate(count)
        ?Enumerable.Range(0,(int)count).Select(i=>new Complex(i,0)).ToArray()
        :throw new InvalidDataException("invalid_control_vector_length");

    private static Complex[] Ones(double count) => count>=1 && count<=VectorLimit && count==Math.Truncate(count)
        ? Enumerable.Repeat(Complex.One,(int)count).ToArray() : throw new InvalidDataException("invalid_unitvec_length");

    private static Complex[] Binary(string op,Complex[] a,Complex[] b)
    {
        if (a.Length!=b.Length && a.Length!=1 && b.Length!=1)
            throw new InvalidDataException("control_vector_shape");
        var result=new Complex[Math.Max(a.Length,b.Length)];
        for(var i=0;i<result.Length;i++)
        {
            var x=a[a.Length==1?0:i];
            var y=b[b.Length==1?0:i];
            result[i]=op switch
            {
                "+"=>x+y, "-"=>x-y, "*"=>x*y, "/"=>x/y,
                "eq" or "=" or "=="=>x==y?1:0, "ne" or "!=" or "<>" or "><"=>x!=y?1:0,
                "<"=>Real(x)<Real(y)?1:0, ">"=>Real(x)>Real(y)?1:0, "<="=>Real(x)<=Real(y)?1:0, ">="=>Real(x)>=Real(y)?1:0,
                "&" or "&&"=>Real(x)!=0&&Real(y)!=0?1:0, "|" or "||"=>Real(x)!=0||Real(y)!=0?1:0,
                _=>throw new InvalidDataException("unsupported_control_operator:"+op)
            };
        }
        return result;
    }
}
