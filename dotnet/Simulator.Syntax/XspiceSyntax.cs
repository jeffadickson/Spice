using System.Collections.Immutable;
using Antlr4.Runtime;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;

/// <summary>One voltage port: potential is P minus N. Null terminals preserve an unsupported port rather than guessing its meaning.</summary>
public sealed record XspiceVoltagePortSyntax(SourceSyntax? P,SourceSyntax? N,SourceSyntax Original)
{
    /// <summary>Only noninverted scalar v/vd ports enter the bounded analog profile.</summary>
    public bool Supported => P != null && N != null;
}
/// <summary>Generated A-card metadata; lexical ownership and original spelling survive extraction.</summary>
public sealed record XspiceInstanceSyntax(string Name,int ScopeId,SourceSyntax Model,ImmutableArray<XspiceVoltagePortSyntax> Ports,SourceRange Range);

internal static class XspiceExtraction
{
    internal static XspiceInstanceSyntax Extract(SpiceParser.XspiceContext card,int scope,Func<ParserRuleContext,SourceSyntax> capture)
        =>new(card.Start.Text,scope,capture(card.referenceName()),card.xport().Select(Port).ToImmutableArray(),capture(card).Range);

    private static XspiceVoltagePortSyntax Port(SpiceParser.XportContext port)
    {
        // ANTLR physical slices retain comments/whitespace and differential order.
        SourceSyntax Capture(ParserRuleContext context)
        {
            var start=context.Start;var stop=context.Stop;
            return new(context.Start.InputStream.GetText(new Antlr4.Runtime.Misc.Interval(start.StartIndex,stop.StopIndex)),
                new(start.StartIndex,stop.StopIndex-start.StartIndex+1,start.Line,start.Column+1));
        }
        var original=Capture(port);
        if(port.TILDE()!=null||port.xvector()!=null||port.xport().Length!=0||port.XTYPE()!=null)return new(null,null,original);
        var nodes=port.xpair()?.scalarNode()??port.scalarNode();
        if(port.XDIFF()!=null)
            return port.XDIFF().GetText().Equals("%vd",StringComparison.OrdinalIgnoreCase)&&nodes.Length==2
                ?new(Capture(nodes[0]),Capture(nodes[1]),original):new(null,null,original);
        if(port.XSINGLE()!=null&&!port.XSINGLE().GetText().Equals("%v",StringComparison.OrdinalIgnoreCase))return new(null,null,original);
        // Unadorned scalar ports have the model's v default. Ground is synthetic
        // but carries the original port location for useful diagnostic attribution.
        return nodes.Length==1&&!nodes[0].GetText().Equals("null",StringComparison.OrdinalIgnoreCase)?new(Capture(nodes[0]),new("0",original.Range),original):new(null,null,original);
    }
}
