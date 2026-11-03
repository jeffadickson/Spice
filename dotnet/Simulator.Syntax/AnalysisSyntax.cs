using System.Collections.Immutable;
namespace Simulator.Syntax;
/// <summary>One analysis directive from generated contexts; names and SI expressions remain unevaluated.</summary>
public sealed record AnalysisSyntax(int ScopeId,string Kind,ImmutableArray<string> Names,ImmutableArray<SourceSyntax> Values,SourceRange Range);
