# SForge v0.1 user manual

SForge is a typed source language for circuit simulations. This is an implemented **v0.1 parser and lowering path**: it recognizes a package, circuits, nodes, resistor/capacitor/voltage-source/diode devices, one transient analysis, observations, and text assertions; validated programs lower into and execute through the Rust simulator.

The lexer is a first-class component. It recognizes identifiers, finite numeric prefixes, punctuation, whitespace and `#` comments; every emitted token carries an offset plus one-based line/column location. Invalid characters and incomplete exponents are structured lexical diagnostics. Parsing uses Parlot for the identifier contract, while the surrounding v0.1 grammar is a deliberately explicit recursive-descent implementation.

## Complete v0.1 grammar

```ebnf
file        = { whitespace | comment }, [ package ], { declaration }, EOF ;
package     = "package", identifier, ";" ;
declaration = circuit | run ;
circuit     = "circuit", identifier, "{", { node | device }, "}" ;
node        = "node", identifier, { ",", identifier }, ";" ;
device      = resistor | capacitor | source | diode ;
resistor    = "resistor", identifier, "(", identifier, ",", identifier, ")", "{", "resistance", ":", quantity, "}" ;
capacitor   = "capacitor", identifier, "(", identifier, ",", identifier, ")", "{", "capacitance", ":", quantity, "}" ;
source      = "source", identifier, "(", identifier, ",", identifier, ")", "{", "dc", ":", quantity, "}" ;
diode       = "diode", identifier, "(", identifier, ",", identifier, ")", "{", identifier, ":", identifier, "}" ;
run         = "run", identifier, "on", identifier, "{", transient, observe, { require }, "}" ;
transient   = "transient", "{", "stop", ":", quantity, ",", "max_step", ":", quantity, "}" ;
observe     = "observe", "{", observable, { ",", observable }, "}" ;
observable  = identifier, "(", identifier, ")" ;
require     = "require", expression, ";" ;
quantity    = number, unit ;
number      = [ "-" | "+" ], digit, { digit | "." | "e" | "E" | "+" | "-" } ;
unit        = identifier ;
identifier  = ( letter | "_" | "$" ), { letter | digit | "_" | "$" | "-" } ;
comment     = "#", { any-character-except-newline }, newline ;
whitespace  = { " " | "\t" | "\r" | "\n" } ;
```

`require` is intentionally retained as source text in v0.1. A later semantic expression parser will type-check it; do not assume expressions execute today.

## Example

```text
package clamp_demo;

circuit clamp {
  node gnd, vin, out;
  source supply(vin, gnd) { dc: 1V }
  resistor limit(vin, out) { resistance: 1kOhm }
  capacitor hold(out, gnd) { capacitance: 1uF }
  diode dclamp(out, gnd) { model: idealized }
}

run startup on clamp {
  transient { stop: 2ms, max_step: 1us }
  observe { v(out), i(dclamp) }
  require max(v(out)) < 0.9V;
}
```

## Using the parser from C#

```csharp
var result = SForge.Language.SForgeParser.Parse(File.ReadAllText("clamp.sforge"));
if (!result.Success)
    foreach (var diagnostic in result.Diagnostics) Console.Error.WriteLine(diagnostic);
else
    var circuit = (SForge.Language.Circuit)result.Document!.Declarations[0];
```

The parser returns a source-span-bearing AST and structured diagnostics. The included semantic validator checks declared nodes, circuit references, `gnd`, and the implemented unit families. `SForgeLowering.Lower(document, "startup")` now lowers v0.1 primitive circuits and transient runs into the existing typed Rust-simulator JSON request. It maps SForge `gnd` to the simulator's required node `0`; the conformance test executes the example through the Rust executable.

## Running a source file

```powershell
dotnet run --project dotnet/SForge.Runner -- examples/clamp.sforge target/debug/circuit-simulator.exe
```

The runner prints the native result manifest JSON and returns a nonzero exit code for lexical, semantic, lowering, or simulator failure.

`observe { v(out), i(d1) }` lowers to typed final-sample measurements. The currently supported assertion form is `require max(v(out)) < 0.9V;` (and the equivalent current form); unsupported expressions fail during lowering with the original source expression in the diagnostic. The v0.1 lowering uses configured backward-Euler transient execution so that the initial operating-point sample is available to bounded window measurements.
