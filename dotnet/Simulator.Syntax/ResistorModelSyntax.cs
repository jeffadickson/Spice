using System.Collections.Immutable;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Simulator.Syntax.Generated;
namespace Simulator.Syntax;

/// <summary>A generated scalar or model candidate and ordered named assignments. This metadata does not override ordinary scalar resistor syntax.</summary>
public sealed record ResistorModelSyntax(SourceSyntax Reference,ImmutableArray<ParameterSyntax> Parameters);

// Existing generated parameter tokens and the semantic-entry adapter retain
// original offsets; no netlist relexing, splitting or independent parser.
internal static class ResistorModelExtraction
{
    internal static ResistorModelSyntax? Extract(SpiceParser.ParameterContext[] parameters,Func<ParserRuleContext,SourceSyntax> capture)
    {
        // The general card already exposes a scalar followed by assignments.
        // Retain those generated ranges for both numeric and model candidates.
        if(parameters.Length>1&&parameters[0].value()!=null&&parameters.Skip(1).All(p=>p.assignment()!=null))
            return new(capture(parameters[0].value()),parameters.Skip(1).Select(p=>Assignment(p.assignment())).ToImmutableArray());
        var parser=SourceProfileExtraction.CreateParser(parameters);if(parser==null)return null;
        try
        {
            var profile=parser.resistorModelProfile();
            return new(capture(profile.identifier()),profile.assignment().Select(Assignment).ToImmutableArray());
        }
        catch(ParseCanceledException){return null;}
        ParameterSyntax Assignment(SpiceParser.AssignmentContext a)=>new(a.referenceName()?.GetText(),
            capture((ParserRuleContext)a.GetChild(0)),capture((ParserRuleContext)a.GetChild(2)),capture(a).Range);
    }
}