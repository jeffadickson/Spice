using System.Text.Json.Serialization;
namespace Simulator.Client;

/// <summary>DC-only ideal voltage relation V(P)-V(N)=Expression(V(Cp)-V(Cn)); branch current is P to N.</summary>
public sealed record BehavioralVoltage(string Id,int P,int N,int Cp,int Cn,BoundedExpression Expression):Element(Id,P,N);

/// <summary>Finite bounded scalar model tree. Input is the SI numeric differential control voltage; this is not executable script text.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName="op")]
[JsonDerivedType(typeof(ExpressionConstant),"constant")]
[JsonDerivedType(typeof(ExpressionInput),"input")]
[JsonDerivedType(typeof(ExpressionAdd),"add")]
[JsonDerivedType(typeof(ExpressionMultiply),"multiply")]
[JsonDerivedType(typeof(ExpressionDivide),"divide")]
[JsonDerivedType(typeof(ExpressionExp),"exp")]
[JsonDerivedType(typeof(ExpressionLog),"log")]
public abstract record BoundedExpression;
/// <summary>One finite scalar constant.</summary>
public sealed record ExpressionConstant(double Value):BoundedExpression;
/// <summary>The source's differential control voltage.</summary>
public sealed record ExpressionInput:BoundedExpression;
/// <summary>Sum of two scalar subexpressions.</summary>
public sealed record ExpressionAdd(BoundedExpression Left,BoundedExpression Right):BoundedExpression;
/// <summary>Product of two scalar subexpressions.</summary>
public sealed record ExpressionMultiply(BoundedExpression Left,BoundedExpression Right):BoundedExpression;
/// <summary>Quotient; a zero denominator is an explicit model-domain failure.</summary>
public sealed record ExpressionDivide(BoundedExpression Left,BoundedExpression Right):BoundedExpression;
/// <summary>Natural exponential; nonfinite values or derivatives are refused.</summary>
public sealed record ExpressionExp(BoundedExpression Argument):BoundedExpression;
/// <summary>Natural logarithm with strictly positive real argument.</summary>
public sealed record ExpressionLog(BoundedExpression Argument):BoundedExpression;
