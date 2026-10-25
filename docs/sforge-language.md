<!-- Authoritative requirements: sforge-language.sdoc -->

# SForge language specification track

## Parser decision: use Parlot for the spike

**Yes.** Use stable Parlot 1.5.8 for the first C# parser spike, pinned by exact version. It is a maintained, BSD-3-Clause .NET parser-combinator library, supports .NET 10, and its documented fluent grammar/source-generation options fit a small language that needs high-quality C# diagnostics. The parser is deliberately not part of the language contract: its output is a SForge AST with spans and structured diagnostics.

Do not adopt the current Parlot previews for the v1 spike. Re-evaluate a generated parser only after the grammar, error recovery cases and performance corpus are real.

## Five design-review outcomes

| Lens | Non-negotiable language choice |
|---|---|
| Legacy-library maintainer | SPICE is an explicit import dialect with a translation/loss report. |
| Analog designer | Readable primitive devices, named model use, checked units, no positional mystery fields. |
| Verification engineer | `run`, `observe`, `require`, `sweep`, seed and failure behavior are source constructs. |
| Compact-model author | Models declare equations/derivatives/capabilities; implementation is selected separately. |
| IDE/API engineer | UTF-8 spans, multiple diagnostics, parser-independent AST, typed result schema. |

## Grammar slice 0

The first parser accepts only `package`, `circuit`, `node`, primitive devices, `run`, `transient`, `observe`, and `require`. It is intentionally non-SPICE-compatible source. Importing a SPICE netlist is a separate compiler front end.

```ebnf
file       = packageDecl? { circuitDecl | runDecl } EOF ;
circuitDecl = "circuit" identifier "{" { nodeDecl | deviceDecl } "}" ;
nodeDecl   = "node" identifier { "," identifier } ";" ;
runDecl    = "run" identifier "on" identifier "{" analysis observe require* "}" ;
quantity   = number unitIdentifier ;
```

## Parser architecture

`UTF-8 source -> tokens/spans -> Parlot grammar -> AST -> semantic validation -> shared IR`.

Parsing only recognizes syntax. Name resolution, unit dimension checking, model lookup, backend capability checks and solver validation are later phases, so diagnostics stay accurate and C#/F# builders can construct the same AST/IR.

Parlot’s current package supports .NET 8+ including .NET 10, and its project documents a fluent parser-combinator API plus optional compile-time source generation. [Parlot NuGet package](https://www.nuget.org/packages/Parlot), [Parlot source documentation](https://github.com/sebastienros/parlot)
