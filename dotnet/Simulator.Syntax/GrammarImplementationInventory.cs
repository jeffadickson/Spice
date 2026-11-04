using System.Collections.Immutable;
using Simulator.Syntax.Generated;

namespace Simulator.Syntax;

/// <summary>Assessment of one stage, never a claim of complete SPICE conformance.</summary>
public enum GrammarStageStatus
{
    /// <summary>No reviewed association is available.</summary>
    Unassessed,
    /// <summary>Structure is preserved without the feature's semantic implementation.</summary>
    Structural,
    /// <summary>A named subset is implemented within the recorded limits.</summary>
    Bounded,
    /// <summary>The operation is explicitly retained as unexecuted.</summary>
    NotExecuted,
    /// <summary>The construct is explicitly refused at this stage.</summary>
    Unsupported,
    /// <summary>This infrastructure rule has no independent operation at this stage.</summary>
    NotApplicable
}

/// <summary>Reviewed association between a generated production, implementation,
/// stable requirements and verification source/evidence. Paths are repository relative.</summary>
public sealed record GrammarRuleAssessment(string Rule,GrammarStageStatus Extraction,
    GrammarStageStatus Preparation,GrammarStageStatus Execution,ImmutableArray<string> Implementation,
    ImmutableArray<string> Requirements,ImmutableArray<string> Evidence,string Limitation)
{
    /// <summary>Concrete control headings associated with this production; empty
    /// for non-command rules. These are reviewed, not inferred from wildcard syntax.</summary>
    public ImmutableArray<string> Commands { get; init; } = [];
}

/// <summary>Every generated production is visible, including absent assessments.</summary>
public sealed record GrammarImplementationRow(string Rule,bool Assessed,GrammarStageStatus Extraction,
    GrammarStageStatus Preparation,GrammarStageStatus Execution,ImmutableArray<string> Implementation,
    ImmutableArray<string> Requirements,ImmutableArray<string> Evidence,string Limitation)
{
    /// <summary>Reviewed concrete control headings, when applicable.</summary>
    public ImmutableArray<string> Commands { get; init; } = [];
}

/// <summary>Joins the generated parser's actual rule identities with conservative,
/// reviewed associations. It does not infer execution support from syntax or names.</summary>
public static class GrammarImplementationInventory
{
    /// <summary>Rejects stale/duplicate/invalid claims; missing associations remain
    /// explicitly unassessed so new grammar productions cannot silently disappear.</summary>
    public static ImmutableArray<GrammarImplementationRow> Build(IEnumerable<GrammarRuleAssessment> assessments)
    {
        ArgumentNullException.ThrowIfNull(assessments);
        var rules=SpiceParser.ruleNames.ToHashSet(StringComparer.Ordinal);
        var registered=new Dictionary<string,GrammarRuleAssessment>(StringComparer.Ordinal);
        var commands=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var assessment in assessments)
        {
            if(assessment==null||!rules.Contains(assessment.Rule)||!registered.TryAdd(assessment.Rule,assessment))
                throw new InvalidDataException("Stale or duplicate grammar assessment.");
            if(!Enum.IsDefined(assessment.Extraction)||!Enum.IsDefined(assessment.Preparation)||!Enum.IsDefined(assessment.Execution)||
               assessment.Implementation.IsDefaultOrEmpty||assessment.Requirements.IsDefaultOrEmpty||assessment.Evidence.IsDefaultOrEmpty||
               assessment.Implementation.Any(string.IsNullOrWhiteSpace)||assessment.Requirements.Any(string.IsNullOrWhiteSpace)||
               assessment.Evidence.Any(string.IsNullOrWhiteSpace)||string.IsNullOrWhiteSpace(assessment.Limitation))
                throw new InvalidDataException("Assessment requires stage identities, implementation, requirements, evidence and limits.");
            if(assessment.Commands.IsDefault||assessment.Commands.Any(c=>string.IsNullOrWhiteSpace(c)||c.Any(char.IsWhiteSpace)||!commands.Add(c)))
                throw new InvalidDataException("Invalid or ambiguous concrete command association.");
        }
        return rules.Order(StringComparer.Ordinal).Select(rule=>registered.TryGetValue(rule,out var a)
            ? new GrammarImplementationRow(rule,true,a.Extraction,a.Preparation,a.Execution,a.Implementation,a.Requirements,a.Evidence,a.Limitation){Commands=a.Commands}
            : new GrammarImplementationRow(rule,false,GrammarStageStatus.Unassessed,GrammarStageStatus.Unassessed,GrammarStageStatus.Unassessed,
                [],[],[],"No reviewed association; syntax recognition establishes no execution capability.")).ToImmutableArray();
    }
}
