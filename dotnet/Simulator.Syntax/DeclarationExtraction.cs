using System.Collections.Immutable;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Simulator.Syntax.Generated;

namespace Simulator.Syntax;

// Uses generated contexts and original offsets, not another tokenizer or card
// scanner. Preorder traversal establishes an enclosing scope before its body.
internal sealed class DeclarationExtraction
{
    private readonly string text;
    private readonly Func<int,(int Line,int Column)> locate;
    private readonly List<DeclarationScope> scopes=[];
    private readonly List<NamedDeclaration> symbols=[];
    private readonly List<InstanceHeader> instances=[];
    private readonly List<PrimitiveSyntax> primitives=[];
    private readonly List<XspiceInstanceSyntax> xspice=[];
    private readonly List<BehavioralSourceSyntax> behavior=[];
    private readonly List<ScopedCard> cards=[];
    private readonly List<AnalysisSyntax> analyses=[];
    private readonly List<InitialConditionSyntax> initialConditions=[];
    private readonly List<ControlBlockSyntax> controlBlocks=[];
    private readonly List<LibrarySectionEnd> libraryEnds=[];
    private readonly List<ScopedParameter> parameters=[];
    private readonly List<DeclaredResource> resources=[];
    private readonly List<DeferredDeclarationContext> deferredContexts=[];
    private readonly Dictionary<SpiceParser.SubcircuitContext,int> bodies=[];

    internal DeclarationExtraction(string text,Func<int,(int Line,int Column)> locate)
    {
        this.text=text;this.locate=locate;scopes.Add(new(0,null,null,new(0,text.Length,1,1)));
    }

    private static bool IsReportingOption(SpiceParser.ParameterContext parameter)
    {
        if (parameter.value()?.identifier() != null)
            return parameter.GetText().ToLowerInvariant() is "acct" or "noacct" or "noinit";
        var assignment=parameter.assignment();
        var value=assignment?.value()?.GetText();
        return assignment?.referenceName()?.GetText().ToLowerInvariant() switch
        {
            "filetype" => value?.ToLowerInvariant() is "ascii" or "binary",
            // NUMDGT is a display setting. Bound literal precision to the
            // useful round-trip range for binary64 decimal output.
            "numdgt" => int.TryParse(value,System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,out var digits) && digits is >= 1 and <= 17,
            _ => false
        };
    }

    // A literal operating temperature equal to the current fixed Celsius
    // model temperature changes no equations. Expressions and other settings
    // require an engine/model contract and remain physical refusals.
    private static bool IsDefaultTemperature(SpiceParser.ValueContext? value) =>
        value?.NUMBER() != null
        && double.TryParse(value.GetText(),System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,out var celsius)
        && celsius == 27;

    private static bool IsDefaultTemperatureOption(SpiceParser.ParameterContext parameter) =>
        parameter.assignment() is {} assignment
        && assignment.referenceName()?.GetText().Equals("temp",StringComparison.OrdinalIgnoreCase) == true
        && IsDefaultTemperature(assignment.value());

    // ngspice order one is backward Euler for both Gear and trapezoidal.
    // Higher Gear orders need BDF history that the engine does not expose.
    private static string? IntegrationMethod(SpiceParser.ParameterContext[] parameters)
    {
        string? method=null,order=null;
        foreach(var parameter in parameters)
        {
            if(IsReportingOption(parameter))continue;
            var assignment=parameter.assignment();
            var value=assignment?.value()?.GetText().ToLowerInvariant();
            switch(assignment?.referenceName()?.GetText().ToLowerInvariant())
            {
                case "method" when method==null && value is "trap" or "gear":method=value;break;
                case "maxord" when order==null && value is "1" or "2":order=value;break;
                default:return null;
            }
        }
        return (method,order) switch
        {
            ("gear" or "trap","1")=>"backward_euler",
            ("trap",null or "2")=>"trapezoidal",
            _=>null
        };
    }

    // Legacy .WIDTH controls printed columns in ngspice. Only a bounded
    // literal IN/OUT pair can be classified as a reporting omission.
    private static bool IsReportingWidth(SpiceParser.ParameterContext[] parameters)
    {
        if (parameters.Length is < 1 or > 2) return false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters)
        {
            var assignment = parameter.assignment();
            var key = assignment?.referenceName()?.GetText();
            if (key?.ToLowerInvariant() is not ("in" or "out") || !seen.Add(key)
                || !int.TryParse(assignment!.value()?.GetText(),System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,out var width) || width <= 0)
                return false;
        }
        return true;
    }

    internal void Observe(IParseTree item)
    {
        if(item is not SpiceParser.StatementContext statement || statement.GetChild(0) is not ParserRuleContext card)return;
        var owner=Enclosing(statement.Parent);
        var directive = card as SpiceParser.ExtensionContext;
        var directiveName = directive?.Start.Text.ToLowerInvariant();
        var directiveParameters = directive?.parameter() ?? [];
        var optionCard = directiveName is ".option" or ".options" && directiveParameters.Length > 0;
        var reportingOnly = optionCard && directiveParameters.All(IsReportingOption);
        var exports = directiveName==".csparam" && owner==0 && directiveParameters.Length>0
            && directiveParameters.All(p=>p.assignment()?.referenceName()?.identifier()!=null)
            ? directiveParameters.Select(p=>Parameter(p.assignment())).ToImmutableArray()
            : ImmutableArray<ParameterSyntax>.Empty;
        cards.Add(new(owner,SpiceParser.ruleNames[card.RuleIndex],Range(card))
        {
            Name = card.Start.Text,
            GlobalNodes = GlobalNodeExtraction.Extract(card,Capture),
            ReportingOptionsOnly = reportingOnly,
            TransientMethodOption = optionCard ? IntegrationMethod(directiveParameters) : null,
            ControlConstants = exports,
            DefaultTemperature = (optionCard && !reportingOnly
                && directiveParameters.All(p => IsReportingOption(p) || IsDefaultTemperatureOption(p)))
                || (directiveName == ".temp" && directiveParameters.Length == 1 && IsDefaultTemperature(directiveParameters[0].value())),
            ReportingWidth = directiveName == ".width" && IsReportingWidth(directiveParameters)
        });
        if(card is SpiceParser.XspiceContext codeModel)
        {xspice.Add(XspiceExtraction.Extract(codeModel,owner,Capture));return;}
        if(card is SpiceParser.OpContext)analyses.Add(new(owner,"op",[],[],Range(card)));
        if(card is SpiceParser.AcContext ac)analyses.Add(new(owner,"ac",[ac.identifier().GetText()],ac.value().Select(Capture).ToImmutableArray(),Range(card)));
        if(card is SpiceParser.DcContext dc)analyses.Add(new(owner,"dc",dc.identifier().Select(i=>i.GetText()).ToImmutableArray(),dc.value().Select(Capture).ToImmutableArray(),Range(card)));
        if(card is SpiceParser.BehaviorContext behavioral)
        {behavior.Add(BehavioralSourceExtraction.Extract(behavioral,owner,Capture,Capture));return;}
        if(card is SpiceParser.TranContext tran){
            var values=tran.value();var names=tran.identifier()==null?ImmutableArray<string>.Empty:[tran.identifier().GetText()];
            // The optional generated value rule can consume the final UIC identifier.
            // Recognize the keyword from that context, never by splitting raw card text.
            if(names.IsEmpty&&values.Length>2&&values[^1].identifier()?.GetText().Equals("uic",StringComparison.OrdinalIgnoreCase)==true){names=["uic"];values=values[..^1];}
            analyses.Add(new(owner,"tran",names,values.Select(Capture).ToImmutableArray(),Range(card)));
        }
        if(card is SpiceParser.ExtensionContext ic&&ic.Start.Text.Equals(".ic",StringComparison.OrdinalIgnoreCase)){
            var nodes=ImmutableArray.CreateBuilder<NodeInitialConditionSyntax>();var supported=ic.parameter().Length>0;
            foreach(var argument in ic.parameter()){
                var assignment=argument.assignment();
                if(assignment?.GetChild(0) is not SpiceParser.CallContext target||!target.identifier().GetText().Equals("v",StringComparison.OrdinalIgnoreCase)||target.parameter().Length!=1||target.parameter(0).value() is not {} node){supported=false;continue;}
                if(node.identifier()==null&&node.NUMBER()==null){supported=false;continue;}
                nodes.Add(new(Capture(node),Capture((ParserRuleContext)assignment.GetChild(2)),Range(assignment)));
            }
            initialConditions.Add(new(owner,supported,nodes.ToImmutable(),Range(card)));
        }
        if(card is SpiceParser.ControlContext control)controlBlocks.Add(ControlExtraction.Extract(control,owner,Capture,Range));
        var primitive=PrimitiveExtraction.Extract(card,owner,Capture,Capture);
        if(primitive!=null){primitives.Add(primitive);return;}
        if(!exports.IsEmpty)return;
        if(card is SpiceParser.ExtensionContext extension && extension.Start.Text.ToLowerInvariant() is ".if" or ".elseif" or ".else" or ".endif" or ".csparam" or ".sndparam")
        {deferredContexts.Add(new(extension.Start.Text.ToLowerInvariant(),Range(extension)));return;}
        if(card is SpiceParser.ExtensionContext ending&&ending.Start.Text.Equals(".endl",StringComparison.OrdinalIgnoreCase))
        {
            var tail=InstanceExtraction.Tail(ending.parameter(),Capture,Parameter);
            libraryEnds.Add(new(tail.Positionals.IsEmpty?null:tail.Positionals[0],tail.Supported&&tail.Parameters.IsEmpty&&tail.Positionals.Length<=1,Range(ending)) { ScopeId=Enclosing(statement.Parent) });return;
        }
        var instance=InstanceExtraction.Extract(card,Enclosing(statement.Parent),Capture,Parameter);
        if(instance!=null){instances.Add(instance);return;}
        if(card is not (SpiceParser.SubcircuitContext or SpiceParser.ModelContext or SpiceParser.ParamCardContext or SpiceParser.IncludeCardContext or SpiceParser.LibraryCardContext))return;
        var scope=Enclosing(statement.Parent);
        switch(card)
        {
            case SpiceParser.SubcircuitContext sub:
                var name=sub.referenceName().GetText();var child=scopes.Count;
                scopes.Add(new(child,scope,name,Range(sub)));bodies.Add(sub,child);
                // Generated parameter() also contains .ends arguments. Only
                // records before the first default-channel NL are header data.
                var header=sub.parameter().Where(p=>p.Start.StartIndex<sub.NL(0).Symbol.StartIndex).ToArray();
                var typedHeader=InstanceExtraction.Tail(header,Capture,Parameter);
                var defaults=typedHeader.Parameters;
                symbols.Add(new(symbols.Count,DeclarationKind.Subcircuit,name,scope,child,null,header.Select(Capture).ToImmutableArray(),defaults,Range(sub)) { FormalPorts=typedHeader.Positionals,HasTypedPorts=typedHeader.Supported });
                parameters.AddRange(defaults.Select(p=>new ScopedParameter(child,"subcircuit_default",p)));
                break;
            case SpiceParser.ModelContext model:
                symbols.Add(new(symbols.Count,DeclarationKind.Model,model.referenceName().GetText(),scope,null,
                    model.identifier(0).GetText().ToLowerInvariant(),[],model.assignment().Select(Parameter).ToImmutableArray(),Range(model)){HasOpaqueModelTerms=model.value().Length>0||model.call().Length>0||model.identifier().Length>1});
                break;
            case SpiceParser.ParamCardContext param:
                parameters.AddRange(param.assignment().Select(p=>new ScopedParameter(scope,"param_card",Parameter(p))));
                break;
            case SpiceParser.IncludeCardContext:
            case SpiceParser.LibraryCardContext:
                var argument=(ITerminalNode)card.GetChild(1);
                var token=argument.Symbol;
                resources.Add(new(scope,card is SpiceParser.IncludeCardContext?"include":"library_card",
                    Capture(token.StartIndex,token.StopIndex),Range(card)) { Section=card is SpiceParser.LibraryCardContext library&&library.identifier()!=null?Capture(library.identifier()):null });
                break;
        }
    }

    internal DeclarationCatalog Build()=>new(scopes.ToImmutableArray(),symbols.ToImmutableArray(),parameters.ToImmutableArray(),resources.ToImmutableArray(),deferredContexts.ToImmutableArray(),instances.ToImmutableArray(),libraryEnds.ToImmutableArray(),primitives.ToImmutableArray(),cards.ToImmutableArray()) { XspiceInstances=xspice.ToImmutableArray(), Analyses=analyses.ToImmutableArray(), BehavioralSources=behavior.ToImmutableArray(), InitialConditions=initialConditions.ToImmutableArray(),ControlBlocks=controlBlocks.ToImmutableArray() };

    private int Enclosing(IParseTree? tree)
    {
        for(var parent=tree;parent!=null;parent=parent.Parent)
            if(parent is SpiceParser.SubcircuitContext sub&&bodies.TryGetValue(sub,out var scope))return scope;
        return 0;
    }
    private ParameterSyntax Parameter(SpiceParser.AssignmentContext assignment)
    {
        var target=(ParserRuleContext)assignment.GetChild(0);var value=(ParserRuleContext)assignment.GetChild(2);
        return new(assignment.referenceName()?.GetText(),Capture(target),Capture(value),Range(assignment))
        {ScalarVectorValues=value is SpiceParser.GroupContext group?ScalarVectorExtraction.Extract(group,Capture):null};
    }
    private SourceSyntax Capture(ParserRuleContext context)=>Capture(context.Start.StartIndex,context.Stop.StopIndex);
    private SourceSyntax Capture(int start,int stop)
    {
        start=Math.Clamp(start,0,text.Length);var end=Math.Clamp(stop+1,start,text.Length);var (line,column)=locate(start);
        return new(text[start..end],new(start,end-start,line,column));
    }
    private SourceRange Range(ParserRuleContext context)
    {
        var start=Math.Clamp(context.Start.StartIndex,0,text.Length);var end=Math.Clamp(context.Stop.StopIndex+1,start,text.Length);var (line,column)=locate(start);
        return new(start,end-start,line,column);
    }
}
