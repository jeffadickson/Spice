using System.Collections.Immutable;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Simulator.Syntax.Generated;

namespace Simulator.Syntax;

/// <summary>A source-located syntactic occurrence, not an elaborated device or numerical capability.</summary>
public sealed record SyntaxFeature(string Category,string Name,SourceRange Range);

// Observes the already recognized tree. No includes, quoted expression parsers,
// model evaluation, or independent card lexer are introduced here.
internal static class FeatureExtraction
{
    internal static void Observe(IParseTree item,ImmutableArray<SyntaxFeature>.Builder output,
        Func<int,(int Line,int Column)> locate,int sourceLength)
    {
        if(item is SpiceParser.StatementContext statement && statement.GetChild(0) is ParserRuleContext card)
        {
            var rule=SpiceParser.ruleNames[card.RuleIndex];
            if(rule is "passive" or "source" or "diode" or "bjt" or "mos" or "jfet" or "vcontrol" or
                "icontrol" or "switchCard" or "cswitch" or "coupling" or "subcall" or "behavior" or
                "lineCard" or "xspice" or "osdi" or "cpl" or "txl" or "urc") Add("device",card.Start.Text.TrimStart()[..1],card);
            else if(rule is "tran" or "ac" or "dc" or "op") Add("analysis",rule,card);
            else if(rule is "includeCard" or "libraryCard") Add("external_resource",rule=="includeCard"?"include":"library",card);
            else if(rule is "subcircuit" or "control" or "paramCard" or "function") Add("structure",rule,card);
            if(rule=="function")Add("directive",".func",card);
            else if(card.Start.Text.TrimStart().StartsWith('.')) Add("directive",card.Start.Text.Trim(),card);
        }
        if(item is SpiceParser.ModelContext model)
        {
            var family=model.identifier(0).GetText();
            Add("model",family,model);
            var levels=model.assignment().Where(a=>a.referenceName()?.GetText().Equals("level",StringComparison.OrdinalIgnoreCase)==true).ToArray();
            // Missing and non-literal LEVEL values are explicit profiles. No
            // ngspice defaults or parameter-expression values are inferred.
            Add("model_level",family+":"+(levels.Length==0?"unspecified":levels.Length>1?"multiple":
                levels[0].value()?.NUMBER()?.GetText() is { } n && levels[0].value()?.ChildCount==1?n:"expression"),model);
            foreach(var assignment in model.assignment())
                Add("model_parameter",family+":"+(assignment.referenceName()?.GetText()??"call_assignment"),assignment);
        }
        if(item is SpiceParser.AssignmentContext assignmentNode)
            Add("parameter",assignmentNode.referenceName()?.GetText()??"call_assignment",assignmentNode);
        if(item is SpiceParser.CallContext call)
            Add(InsideSource(call)?"source_form":"call",call.identifier().GetText(),call);
        if(item is SpiceParser.ExpressionContext expression)
        {
            // Each production is counted once; descendant expressions will be
            // visited independently. Function names are structural, not bound.
            if(expression.identifier() is { } name && expression.LPAREN()!=null)Add("call",name.GetText(),expression);
            if(expression.ChildCount>1)Add("expression",expression.QUESTION()!=null?"conditional":
                expression.BOOL()!=null?"boolean":expression.ChildCount==2?"unary":"compound",expression);
        }
        if(item is SpiceParser.XportContext port)
        {
            if(port.XDIFF()!=null)Add("xspice_port","differential",port);
            if(port.XSINGLE()!=null)Add("xspice_port","single",port);
            if(port.XTYPE()!=null)Add("xspice_port","custom",port);
        }
        if(item is SpiceParser.XvectorContext vector)Add("xspice_port","vector",vector);
        if(item is ITerminalNode terminal)
        {
            var token=terminal.Symbol;
            if(token.Type is SpiceLexer.STRING or SpiceLexer.QUOTED)AddToken("opaque","quoted_text",token);
            if(token.Type==SpiceLexer.COMMAND_LINE)
            {
                var text=token.Text.TrimStart();
                if(text.Length>0 && !text.StartsWith('*'))
                {
                    var end=0;while(end<text.Length&&!char.IsWhiteSpace(text[end]))end++;
                    AddToken("control_command",text[..end],token);
                }
            }
        }
        void Add(string category,string name,ParserRuleContext context) => AddRange(category,name,context.Start.StartIndex,context.Stop?.StopIndex??context.Start.StopIndex);
        void AddToken(string category,string name,IToken token)=>AddRange(category,name,token.StartIndex,token.StopIndex);
        void AddRange(string category,string name,int start,int stop)
        {
            start=Math.Clamp(start,0,sourceLength);var end=Math.Clamp(stop+1,start,sourceLength);
            var (line,column)=locate(start);
            output.Add(new(category,name.ToLowerInvariant(),new(start,end-start,line,column)));
        }
    }

    private static bool InsideSource(IParseTree tree)
    {
        for(var parent=tree.Parent;parent!=null;parent=parent.Parent)
        {
            // A nested function is not another waveform declaration. This
            // distinction is syntactic; source-form arguments remain unbound.
            if(parent is SpiceParser.CallContext)return false;
            if(parent is SpiceParser.SourceContext)return true;
        }
        return false;
    }
}
