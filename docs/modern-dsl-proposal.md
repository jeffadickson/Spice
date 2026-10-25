<!-- Authoritative requirements: modern-dsl-proposal.sdoc -->

# Proposal: a modern DSL for Spice

## Decision

Build **SForge**, a source-language and typed intermediate representation (IR), not a new spelling of a SPICE netlist. Keep a read-only/import compatibility front end for selected SPICE dialects, but make SForge the canonical authoring, validation, simulation and API model.

The goal is a simulation description that a circuit engineer can review as quickly as a netlist and that a program can create, type-check, run, query and reproduce without scraping console text.

## Five expert-user design reviews

1. **The legacy-library maintainer:** retain predictable flat text and stable naming, but reject silent dialect guessing. Every import declares a dialect and returns structured diagnostics plus a loss report.
2. **The analog designer:** make node connectivity, units, models, parameters and analysis intent visible. Preserve familiar primitives (`resistor`, `diode`, `source`) and physical units, rather than forcing users to write raw solver equations.
3. **The verification engineer:** make corners, sweeps, assertions, seeds, tolerances and failure policy first-class. A run should be reviewable and replayable from its source and manifest.
4. **The model author:** separate a portable declarative device contract from a sandboxed implementation. A model declares terminals, parameters, states, equations, derivatives and supported analyses; a native/GPU plugin is optional, versioned acceleration.
5. **The automation/API engineer:** results must be tables, not stdout or a simulator-private binary. Select signals explicitly, preserve units and provenance, and offer Arrow/Parquet output plus an API that streams accepted samples.

## Proposed shape

```text
package buck_demo @ 0.1

units { voltage: V, time: s, current: A }

model diode idealized {
  parameter is: current = 1pA [positive]
  parameter vt: voltage = 25.85mV [positive]
}

circuit clamp {
  node gnd, vin, out
  source v1(vin, gnd) { dc: 1V }
  resistor r1(vin, out) { resistance: 1kOhm }
  diode d1(out, gnd) uses idealized
  capacitor c1(out, gnd) { capacitance: 1uF }
}

run startup on clamp {
  transient { stop: 2ms, max_step: 1us, method: backward_euler }
  observe { v(out), i(d1), power(d1) }
  require max(v(out)) < 0.9V
  output parquet "results/startup.parquet"
}
```

## Semantics that must be non-negotiable

- Units are parsed and checked; dimensionless values need an explicit `unitless` escape.
- Names are lexical, qualified and case-sensitive; no implicit global parameter collisions.
- Analyses are blocks with schemas, not dot-directives whose meanings vary by dialect.
- `observe` selects result columns; no unbounded “save everything” default.
- Solver policy is separate from circuit behavior. A run can name a policy, while the manifest records the actual backend, tolerances, damping and convergence path.
- Randomness requires a seed; external files/plugins declare hashes and capabilities.
- Every source node has spans and stable IDs so IDEs, C# and F# builders can point to the same diagnostic.

## Architecture

`SForge source -> parser -> typed AST -> validated IR -> compiled circuit plan -> selected CPU/GPU solver -> Arrow result stream -> Parquet/artifact manifest`.

The IR is the contract boundary. C#, F#, JSON and future Starlark bindings should construct the same IR, not generate text. The text DSL is therefore an ergonomic front end, while automation remains structured.

## Deliberate exclusions for v1

- No claim of universal ngspice/PSpice/HSPICE/Spectre source compatibility.
- No unrestricted embedded scripting inside solver execution.
- No hidden solver defaults or silent value suffix interpretation.
- No generic GPU promise: backends advertise supported matrix/device/analysis capabilities.

## Adoption plan

1. Define SForge AST/IR and diagnostics while retaining the current JSON boundary.
2. Implement a strict v1 parser for primitive devices, parameters, `.op`/transient/AC-equivalent blocks and observables.
3. Add SPICE importer as a separate compatibility package with a dialect flag and loss report.
4. Emit Arrow result batches and Parquet artifacts; bind C# and F# directly to IR/result schemas.
5. Add behavioral/device extensions only with derivative, determinism, sandbox and verification contracts.

## Evidence informing the proposal

ngspice documents a complex staged flow and describes its manual as evolving. Xyce explicitly documents cross-dialect incompatibilities and ships an XDM translator; its reference material treats models, subcircuits and behavioral sources as substantial language features. Those are reasons to preserve import compatibility at the edge, not to make it the internal language. [ngspice documentation](https://ngspice.sourceforge.io/docs.html), [Xyce FAQ](https://xyce.sandia.gov/documentation-tutorials/frequently-asked-questions/), [Xyce Reference Guide](https://xyce.sandia.gov/files/xyce/Xyce_Reference_Guide_7.7.pdf)
