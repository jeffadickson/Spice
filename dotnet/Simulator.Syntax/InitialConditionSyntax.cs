using System.Collections.Immutable;
namespace Simulator.Syntax;
/// <summary>One generated .IC voltage target and unevaluated scalar, with original physical ranges.</summary>
public sealed record NodeInitialConditionSyntax(SourceSyntax Node,SourceSyntax Value,SourceRange Range);
/// <summary>A generated .IC card. Unsupported targets remain explicit instead of being silently omitted.</summary>
public sealed record InitialConditionSyntax(int ScopeId,bool Supported,ImmutableArray<NodeInitialConditionSyntax> Nodes,SourceRange Range);
