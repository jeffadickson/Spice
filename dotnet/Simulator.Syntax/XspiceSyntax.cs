using System.Collections.Immutable;
using Antlr4.Runtime;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;

/// <summary>One voltage port: potential is P minus N. Null terminals preserve unsupported scalar semantics rather than guessing.</summary>
public sealed record XspiceVoltagePortSyntax(SourceSyntax? P,SourceSyntax? N,SourceSyntax Original)
{
    /// <summary>Only noninverted scalar v/vd ports enter the bounded scalar analog profile.</summary>
    public bool Supported => P != null && N != null && !IsCurrent;
    /// <summary>True for a noninverted scalar %i or %id port, whose positive current flows P to N.</summary>
    public bool IsCurrent {get;init;}
    /// <summary>True for a generated vector, including unsupported vector types.</summary>
    public bool IsVector {get;init;}
    /// <summary>True only for a noninverted default-v or explicitly %v vector container. Each child still requires its own supported scalar semantics.</summary>
    public bool IsVoltageVector {get;init;}
    /// <summary>Ordered generated vector children; nested or unsupported children remain explicit and are never silently dropped.</summary>
    public ImmutableArray<XspiceVoltagePortSyntax> Elements {get;init;}=[];
}
/// <summary>Generated A-card metadata; lexical ownership and original spelling survive extraction.</summary>
public sealed record XspiceInstanceSyntax(string Name,int ScopeId,SourceSyntax Model,ImmutableArray<XspiceVoltagePortSyntax> Ports,SourceRange Range);

internal static class XspiceExtraction
{
    internal static XspiceInstanceSyntax Extract(SpiceParser.XspiceContext card,int scope,Func<ParserRuleContext,SourceSyntax> capture)
        =>new(card.Start.Text,scope,capture(card.referenceName()),card.xport().Select(p=>Port(p,capture)).ToImmutableArray(),capture(card).Range);

    private static XspiceVoltagePortSyntax Port(SpiceParser.XportContext port,Func<ParserRuleContext,SourceSyntax> capture)
    {
        // Reuse the deck's physical offset map, including bare CR source lines.
        var original=capture(port);
        if(port.xvector() is {} vector)
            return new(null,null,original){IsVector=true,
                IsVoltageVector=port.TILDE()==null&&port.XDIFF()==null&&port.XTYPE()==null
                    &&(port.XSINGLE()==null||port.XSINGLE().GetText().Equals("%v",StringComparison.OrdinalIgnoreCase)),
                Elements=vector.xport().Select(p=>Port(p,capture)).ToImmutableArray()};
        if(port.TILDE()!=null||port.xport().Length!=0||port.XTYPE()!=null)return new(null,null,original);
        var nodes=port.xpair()?.scalarNode()??port.scalarNode();
        if(nodes.Any(n=>n.GetText().Equals("null",StringComparison.OrdinalIgnoreCase)))return new(null,null,original);
        if(port.XDIFF()!=null)
        {
            var type=port.XDIFF().GetText().ToLowerInvariant();
            return type is "%vd" or "%id"&&nodes.Length==2
                ?new(capture(nodes[0]),capture(nodes[1]),original){IsCurrent=type=="%id"}:new(null,null,original);
        }
        var single=port.XSINGLE()?.GetText().ToLowerInvariant();
        return single is null or "%v" or "%i"&&nodes.Length==1
            ?new(capture(nodes[0]),new("0",original.Range),original){IsCurrent=single=="%i"}:new(null,null,original);
    }
}
