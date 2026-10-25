# SForge v0.1 user manual

SForge is a typed source language for circuit simulations. This is an implemented **v0.1 parser**: it recognizes a package, circuits, nodes, resistor/capacitor/voltage-source/diode devices, one transient analysis, observations, and text assertions. Semantic lowering into the Rust simulator is the next step.

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
  observe { v(out), i(dclamp), power(dclamp) }
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

The parser returns a source-span-bearing AST and structured diagnostics. It does not run simulation yet. The compiler must next validate names/units, lower to the shared IR, and select a Rust/ngspice backend.
