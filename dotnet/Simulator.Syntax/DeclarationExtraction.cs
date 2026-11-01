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
    private readonly List<ScopedCard> cards=[];
    private readonly List<LibrarySectionEnd> libraryEnds=[];
    private readonly List<ScopedParameter> parameters=[];
    private readonly List<DeclaredResource> resources=[];
    private readonly List<DeferredDeclarationContext> deferredContexts=[];
    private readonly Dictionary<SpiceParser.SubcircuitContext,int> bodies=[];

    internal DeclarationExtraction(string text,Func<int,(int Line,int Column)> locate)
    {
        this.text=text;this.locate=locate;scopes.Add(new(0,null,null,new(0,text.Length,1,1)));
    }

    internal void Observe(IParseTree item)
    {
        if(item is not SpiceParser.StatementContext statement || statement.GetChild(0) is not ParserRuleContext card)return;
        var owner=Enclosing(statement.Parent);
        cards.Add(new(owner,SpiceParser.ruleNames[card.RuleIndex],Range(card)));
        var primitive=PrimitiveExtraction.Extract(card,owner,Capture,Capture);
        if(primitive!=null){primitives.Add(primitive);return;}
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
                    model.identifier(0).GetText().ToLowerInvariant(),[],model.assignment().Select(Parameter).ToImmutableArray(),Range(model)));
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

    internal DeclarationCatalog Build()=>new(scopes.ToImmutableArray(),symbols.ToImmutableArray(),parameters.ToImmutableArray(),resources.ToImmutableArray(),deferredContexts.ToImmutableArray(),instances.ToImmutableArray(),libraryEnds.ToImmutableArray(),primitives.ToImmutableArray(),cards.ToImmutableArray());

    private int Enclosing(IParseTree? tree)
    {
        for(var parent=tree;parent!=null;parent=parent.Parent)
            if(parent is SpiceParser.SubcircuitContext sub&&bodies.TryGetValue(sub,out var scope))return scope;
        return 0;
    }
    private ParameterSyntax Parameter(SpiceParser.AssignmentContext assignment)
    {
        var target=(ParserRuleContext)assignment.GetChild(0);var value=(ParserRuleContext)assignment.GetChild(2);
        return new(assignment.referenceName()?.GetText(),Capture(target),Capture(value),Range(assignment));
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
