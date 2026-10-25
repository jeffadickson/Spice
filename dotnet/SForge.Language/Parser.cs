using System.Globalization;
using Parlot;
using Parlot.Fluent;
using static Parlot.Fluent.Parsers;

namespace SForge.Language;

/// <summary>Strict v0.1 recursive-descent front end. Parlot validates identifier lexical rules; parsing keeps every AST range.</summary>
public static class SForgeParser
{
    // Kept as a Parlot primitive so the lexer contract tracks the chosen parser package.
    private static readonly Parser<TextSpan> Identifier = Terms.Identifier();

    public static ParseResult Parse(string source)
    {
        var p = new Reader(source ?? string.Empty);
        var declarations = new List<Declaration>(); string? package = null;
        if (p.Word("package")) { package = p.Name(); p.Semicolon(); }
        while (!p.End) { Declaration? declaration = p.Word("circuit") ? p.Circuit() : p.Word("run") ? p.Run() : null; if (declaration is null) p.Error("expected circuit or run declaration"); else declarations.Add(declaration); }
        return new ParseResult(p.Errors.Count == 0 ? new Document(package, declarations) : null, p.Errors);
    }
    private sealed class Reader(string text)
    {
        internal readonly List<Diagnostic> Errors = []; private int _at;
        internal bool End { get { Skip(); return _at >= text.Length; } }
        internal void Skip() { while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++; if (_at < text.Length && text[_at] == '#') { while (_at < text.Length && text[_at] != '\n') _at++; Skip(); } }
        internal bool Word(string value) { Skip(); if (text.AsSpan(_at).StartsWith(value, StringComparison.Ordinal) && (_at + value.Length == text.Length || !char.IsLetterOrDigit(text[_at + value.Length]) && text[_at + value.Length] != '_')) { _at += value.Length; return true; } return false; }
        internal string Name() { Skip(); var start = _at; while (_at < text.Length && (char.IsLetterOrDigit(text[_at]) || text[_at] is '_' or '-')) _at++; var name = text[start.._at]; if (name.Length == 0 || Identifier.Parse(name).Length != name.Length) Error("expected identifier"); return name; }
        internal void Need(char c) { Skip(); if (_at < text.Length && text[_at] == c) _at++; else Error($"expected '{c}'"); }
        internal void Semicolon() => Need(';');
        internal Quantity Quantity() { Skip(); var start = _at; while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] is '.' or '-' or '+' or 'e' or 'E')) _at++; var number = double.TryParse(text[start.._at], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN; var unit = Name(); if (!double.IsFinite(number) || unit.Length == 0) Error("expected finite quantity with unit"); return new Quantity(number, unit); }
        internal Circuit Circuit() { var start = _at - "circuit".Length; var name = Name(); Need('{'); var nodes = new List<string>(); var devices = new List<Device>(); while (!End && !Word("}")) { if (Word("node")) { nodes.Add(Name()); while (Word(",")) nodes.Add(Name()); Semicolon(); } else devices.Add(Device()); } return new Circuit(new(start, _at - start), name, nodes, devices); }
        internal Device Device() { Skip(); var start = _at; var kind = Name(); var name = Name(); Need('('); var p = Name(); Need(','); var n = Name(); Need(')'); Need('{'); var field = Name(); Need(':'); var quantity = kind == "diode" ? default : Quantity(); if (kind == "diode") { var model = Name(); Need('}'); return new Diode(new(start, _at - start), name, p, n, model); } Need('}'); return kind switch { "resistor" when field == "resistance" => new Resistor(new(start, _at - start), name, p, n, quantity), "capacitor" when field == "capacitance" => new Capacitor(new(start, _at - start), name, p, n, quantity), "source" when field == "dc" => new VoltageSource(new(start, _at - start), name, p, n, quantity), _ => BadDevice(start, kind, name, p, n) }; }
        private Device BadDevice(int start, string kind, string name, string p, string n) { Error($"unsupported device or property '{kind}'"); return new Resistor(new(start, Math.Max(0, _at - start)), name, p, n, default); }
        internal Run Run() { var start = _at - 3; var name = Name(); if (!Word("on")) Error("expected 'on'"); var circuit = Name(); Need('{'); Transient? analysis = null; var observables = new List<Observable>(); var requirements = new List<Requirement>(); while (!End && !Word("}")) { if (Word("transient")) { var at = _at; Need('{'); if (!Word("stop")) Error("expected stop"); Need(':'); var stop = Quantity(); Need(','); if (!Word("max_step")) Error("expected max_step"); Need(':'); var step = Quantity(); Need('}'); analysis = new Transient(new(at, _at - at), stop, step); } else if (Word("observe")) { Need('{'); do { var at = _at; var kind = Name(); Need('('); var target = Name(); Need(')'); observables.Add(new(new(at, _at - at), kind, target)); } while (Word(",")); Need('}'); } else if (Word("require")) { var at = _at; var end = text.IndexOf(';', _at); if (end < 0) { Error("expected ';' after require"); end = text.Length; } var expr = text[_at..end].Trim(); _at = Math.Min(end + 1, text.Length); requirements.Add(new(new(at, _at - at), expr)); } else Error("expected transient, observe or require"); } return new Run(new(start, _at - start), name, circuit, analysis ?? new Transient(new(start,0),default,default), observables, requirements); }
        internal void Error(string message) { Skip(); Errors.Add(new(new(_at, 0), message)); if (_at < text.Length) _at++; }
    }
}
