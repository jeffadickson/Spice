using System.Text.Json.Serialization;
namespace Simulator.Client;

/// <summary>Ideal voltage relation V(P)-V(N)=Expression(V(Cp)-V(Cn)); branch current is P to N. Analysis admission is checked separately.</summary>
public sealed record BehavioralVoltage(string Id,int P,int N,int Cp,int Cn,BoundedExpression Expression):Element(Id,P,N);
/// <summary>Memoryless I(P to N)=(V(P)-V(N))/Expression(V(Cp)-V(Cn)); resistance is in ohms, control is high impedance.</summary>
public sealed record BehavioralResistor(string Id,int P,int N,int Cp,int Cn,BoundedExpression Expression):Element(Id,P,N);

/// <summary>Finite bounded scalar model tree. Input is the SI numeric differential control voltage; this is not executable script text.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName="op")]
[JsonDerivedType(typeof(ExpressionConstant),"constant")]
[JsonDerivedType(typeof(ExpressionInput),"input")]
[JsonDerivedType(typeof(ExpressionAdd),"add")]
[JsonDerivedType(typeof(ExpressionMultiply),"multiply")]
[JsonDerivedType(typeof(ExpressionDivide),"divide")]
[JsonDerivedType(typeof(ExpressionExp),"exp")]
[JsonDerivedType(typeof(ExpressionLog),"log")]
[JsonDerivedType(typeof(ExpressionAbs),"abs")]
[JsonDerivedType(typeof(ExpressionAsin),"asin")]
[JsonDerivedType(typeof(ExpressionAcos),"acos")]
[JsonDerivedType(typeof(ExpressionSin),"sin")]
[JsonDerivedType(typeof(ExpressionCos),"cos")]
[JsonDerivedType(typeof(ExpressionTan),"tan")]
[JsonDerivedType(typeof(ExpressionAtan),"atan")]
[JsonDerivedType(typeof(ExpressionSqrt),"sqrt")]
[JsonDerivedType(typeof(ExpressionFloor),"floor")]
[JsonDerivedType(typeof(ExpressionCeil),"ceil")]
[JsonDerivedType(typeof(ExpressionTruncate),"truncate")]
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

/// <summary>Absolute value; the selected derivative at zero is zero.</summary>
public sealed record ExpressionAbs(BoundedExpression Argument):BoundedExpression;
/// <summary>Real inverse sine; the native derivative profile requires an argument strictly inside (-1,1).</summary>
public sealed record ExpressionAsin(BoundedExpression Argument):BoundedExpression;
/// <summary>Real inverse cosine; the native derivative profile requires an argument strictly inside (-1,1).</summary>
public sealed record ExpressionAcos(BoundedExpression Argument):BoundedExpression;
/// <summary>Sine of a radian argument.</summary>
public sealed record ExpressionSin(BoundedExpression Argument):BoundedExpression;

/// <summary>Cosine of a radian argument.</summary>
public sealed record ExpressionCos(BoundedExpression Argument):BoundedExpression;

/// <summary>Tangent of a radian argument; nonfinite value or slope is refused.</summary>
public sealed record ExpressionTan(BoundedExpression Argument):BoundedExpression;

/// <summary>Inverse tangent in radians.</summary>
public sealed record ExpressionAtan(BoundedExpression Argument):BoundedExpression;

/// <summary>Square root; runtime finite-derivative profile requires a strictly positive argument.</summary>
public sealed record ExpressionSqrt(BoundedExpression Argument):BoundedExpression;

/// <summary>Round toward negative infinity; selected piecewise slope is zero.</summary>
public sealed record ExpressionFloor(BoundedExpression Argument):BoundedExpression;

/// <summary>Round toward positive infinity; selected piecewise slope is zero.</summary>
public sealed record ExpressionCeil(BoundedExpression Argument):BoundedExpression;

/// <summary>Round toward zero; selected piecewise slope is zero.</summary>
public sealed record ExpressionTruncate(BoundedExpression Argument):BoundedExpression;
