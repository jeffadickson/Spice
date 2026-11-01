using System.Collections.Immutable;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;

// Adapts generated contexts only. No card splitting or second tokenizer.
internal static class InstanceExtraction
{
    internal static (ImmutableArray<SourceSyntax> Positionals,ImmutableArray<ParameterSyntax> Parameters,bool Supported) Tail(
        IEnumerable<SpiceParser.ParameterContext> parameters,Func<ParserRuleContext,SourceSyntax> capture,Func<SpiceParser.AssignmentContext,ParameterSyntax> assignment)
    {
        var positional=ImmutableArray.CreateBuilder<SourceSyntax>();var named=ImmutableArray.CreateBuilder<ParameterSyntax>();
        var assignments=false;var supported=true;
        foreach(var p in parameters)
        {
            if(p.assignment() is {} a){assignments=true;named.Add(assignment(a));continue;}
            if(p.GetText().Equals("params:",StringComparison.OrdinalIgnoreCase)){assignments=true;continue;}
            if(assignments||p.value() is not {} v||v.ChildCount!=1||(v.identifier()==null&&v.NUMBER()==null)){supported=false;continue;}
            positional.Add(capture(v));
        }
        return(positional.ToImmutable(),named.ToImmutable(),supported);
    }
    internal static InstanceHeader? Extract(ParserRuleContext card,int scope,Func<ParserRuleContext,SourceSyntax> capture,
        Func<SpiceParser.AssignmentContext,ParameterSyntax> assignment)
    {
        var family=card switch{SpiceParser.SubcallContext=>"X",SpiceParser.DiodeContext=>"D",SpiceParser.BjtContext=>"Q",SpiceParser.MosContext=>"M",SpiceParser.JfetContext=>"J",SpiceParser.SwitchCardContext=>"S",SpiceParser.CswitchContext=>"W",_=>null};
        if(family==null)return null;
        var positions=ImmutableArray.CreateBuilder<SourceSyntax>();var tails=new List<SpiceParser.ParameterContext>();
        for(var i=1;i<card.ChildCount;i++)
        {
            if(card.GetChild(i) is SpiceParser.ParameterContext p){tails.Add(p);continue;}
            if(card.GetChild(i) is not ParserRuleContext context)continue;
            if(context is SpiceParser.NodeContext or SpiceParser.ReferenceNameContext)positions.Add(capture(context));
            else for(var n=0;n<context.ChildCount;n++)if(context.GetChild(n) is SpiceParser.NodeContext node)positions.Add(capture(node));
        }
        var tail=Tail(tails,capture,assignment);positions.AddRange(tail.Positionals);
        return new(card.Start.Text,family,scope,positions.ToImmutable(),tail.Parameters,tail.Supported,capture(card).Range);
    }
}
