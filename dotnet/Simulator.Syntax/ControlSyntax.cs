using System.Collections.Immutable;
namespace Simulator.Syntax;
/// <summary>Generated reporting-only setting candidate; validation decides the exact supported names and literal values.</summary>
public sealed record ControlSettingSyntax(string? Name,SourceSyntax? Value,string? LiteralIdentifier,bool LiteralNumber,SourceRange Range);
/// <summary>One generated control command; unknown effects remain explicit diagnostics.</summary>
public sealed record ControlCommandSyntax(string Name,string Kind,AnalysisSyntax? Analysis,ImmutableArray<ControlSettingSyntax> Settings,SourceRange Range,string? Diagnostic=null)
{
    /// <summary>Exact generated semantic production, or unclassified when lexical admission fails.</summary>
    public string GrammarRule { get; init; } = "unclassified";
    /// <summary>Original condition or reporting argument, preserved for bounded evaluation.</summary>
    public string? Argument { get; init; }
    /// <summary>Validated logical destination for an isolated reporting capture.</summary>
    public string? RedirectTarget { get; init; }
    /// <summary>Unevaluated local declaration candidate; null for other commands.</summary>
    public ControlPostprocessingSyntax? Postprocessing { get; init; }
}
/// <summary>Recognized local result declarations, independent of any evaluation support.</summary>
public enum ControlPostprocessingKind
{
    /// <summary>Assign a result expression to a simple vector name.</summary>
    Let,
    /// <summary>Declare a named function with simple formal arguments.</summary>
    Define,
    /// <summary>Remove explicitly named local result vectors.</summary>
    Unlet
}
/// <summary>Names contains the let target, the function name followed by formals,
/// or unlet targets. Expression preserves physical spelling; it is not evaluated.</summary>
public sealed record ControlPostprocessingSyntax(ControlPostprocessingKind Kind,ImmutableArray<string> Names,SourceSyntax? Expression);
/// <summary>One original control envelope and ordered physical command records.</summary>
public sealed record ControlBlockSyntax(int ScopeId,SourceRange Range,SourceRange OpenRange,SourceRange CloseRange,ImmutableArray<ControlCommandSyntax> Commands,string? Diagnostic=null);
