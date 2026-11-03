using System.Collections.Immutable;
namespace Simulator.Syntax;
/// <summary>Generated reporting-only setting candidate; validation decides the exact supported names and literal values.</summary>
public sealed record ControlSettingSyntax(string? Name,SourceSyntax? Value,string? LiteralIdentifier,bool LiteralNumber,SourceRange Range);
/// <summary>One generated control command; unknown effects remain explicit diagnostics.</summary>
public sealed record ControlCommandSyntax(string Name,string Kind,AnalysisSyntax? Analysis,ImmutableArray<ControlSettingSyntax> Settings,SourceRange Range,string? Diagnostic=null);
/// <summary>One original control envelope and ordered physical command records.</summary>
public sealed record ControlBlockSyntax(int ScopeId,SourceRange Range,SourceRange OpenRange,SourceRange CloseRange,ImmutableArray<ControlCommandSyntax> Commands,string? Diagnostic=null);
