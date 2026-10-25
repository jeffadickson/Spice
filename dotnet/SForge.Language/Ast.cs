namespace SForge.Language;

/// <summary>A zero-based UTF-16 source range shared by parser and diagnostics.</summary>
public readonly record struct SourceSpan(int Start, int Length);
public sealed record Diagnostic(SourceSpan Span, string Message);
public sealed record Document(string? Package, IReadOnlyList<Declaration> Declarations);
public abstract record Declaration(SourceSpan Span);
public sealed record Circuit(SourceSpan Span, string Name, IReadOnlyList<string> Nodes, IReadOnlyList<Device> Devices) : Declaration(Span);
public sealed record Run(SourceSpan Span, string Name, string Circuit, Analysis Analysis, IReadOnlyList<Observable> Observables, IReadOnlyList<Requirement> Requirements) : Declaration(Span);
public abstract record Device(SourceSpan Span, string Name, string Positive, string Negative);
public sealed record Resistor(SourceSpan Span, string Name, string Positive, string Negative, Quantity Resistance) : Device(Span, Name, Positive, Negative);
public sealed record Capacitor(SourceSpan Span, string Name, string Positive, string Negative, Quantity Capacitance) : Device(Span, Name, Positive, Negative);
public sealed record VoltageSource(SourceSpan Span, string Name, string Positive, string Negative, Quantity Dc) : Device(Span, Name, Positive, Negative);
public sealed record Diode(SourceSpan Span, string Name, string Positive, string Negative, string Model) : Device(Span, Name, Positive, Negative);
public sealed record Transient(SourceSpan Span, Quantity Stop, Quantity MaxStep) : Analysis(Span);
public abstract record Analysis(SourceSpan Span);
public sealed record Observable(SourceSpan Span, string Kind, string Target);
public sealed record Requirement(SourceSpan Span, string Expression);
public readonly record struct Quantity(double Value, string Unit);
public sealed record ParseResult(Document? Document, IReadOnlyList<Diagnostic> Diagnostics) { public bool Success => Document is not null && Diagnostics.Count == 0; }
