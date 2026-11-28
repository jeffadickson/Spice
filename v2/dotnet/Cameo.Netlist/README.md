# Cameo.Netlist & Cameo.Netlist.Scripting

Standalone, `.NET 11` libraries implementing the **Cameo Flagship Netlist** ecosystem:

- **`Cameo.Netlist`** *(zero dependencies — pure .NET 11 BCL)*
  - Topology-first, Git-diffable hierarchical `.spicenet` (`spice.netlist.v3`) parser, canonical formatter, and elaborator (`module`, `use`, `$param`, `chain`, `model`, `rtl`, `bridge`, `layout`, `pcb`, `verify`).
  - Independent dual SHA-256 provenance hashes: `electrical_sha256` (strictly invariant across schematic/PCB layout edits) and `layout_sha256` (schematic placements + PCB geometry).
  - Deterministic left-to-right signal-flow auto-placer + polyglot `.spice.svg` codec.
  - Mixed-signal **`velosim.netlist.v1` + `velosim.rtl.v1`** export & fail-closed importer (4 MiB / JSON depth 64 / pinned model SHA-256 verification).
  - Derived **`tscircuit` / `FieldworkPCB`**-aligned **Circuit JSON** emitter (`pcb_board`, `pcb_component`, `pcb_smtpad`, `pcb_plated_hole`, `pcb_trace`, `pcb_via`, `pcb_ratsnest`) + deterministic `FlagshipNetlist.CheckPcbDrc(...)`.
- **`Cameo.Netlist.Scripting`** *(zero `Spice.*` dependencies)*
  - Hermetic [`Cameo-script`](https://github.com/jeffadickson/Cameo-script) (`CameoEngine.CreateHermetic`) + ISO 80000 **`SharpUnits`** (`Cameo.Units`) scripting host (`net.*`) with dimensional safety (`units.qty(2.2, "kOhm")` accepted on `net.resistor`, while `units.qty(5.0, "V")` fails closed).

## Cross-Repo Consumption (`VeloSim`, `FieldworkPCB`, etc.)

### Option A: Pack Local NuGet Packages (`.nupkg`)

```bash
dotnet pack v2/dotnet/Cameo.Netlist/Cameo.Netlist.csproj -c Release -o ./nupkgs
dotnet pack v2/dotnet/Cameo.Netlist.Scripting/Cameo.Netlist.Scripting.csproj -c Release -o ./nupkgs
```

Then reference `Cameo.Netlist` (and optionally `Cameo.Netlist.Scripting`) in `VeloSim` or any `.NET 11` host:

```xml
<PackageReference Include="Cameo.Netlist" Version="0.1.0" />
<PackageReference Include="Cameo.Netlist.Scripting" Version="0.1.0" />
```

### Option B: Direct Project Reference via Git Submodule

```xml
<ProjectReference Include="../external/Spice/v2/dotnet/Cameo.Netlist/Cameo.Netlist.csproj" />
<ProjectReference Include="../external/Spice/v2/dotnet/Cameo.Netlist.Scripting/Cameo.Netlist.Scripting.csproj" />
```

### Option C: Browser / TypeScript (`FieldworkPCB`)

Export derived `Circuit JSON` via `VeloSimIngestion.ExportCircuitJson(doc)` or `spice-app cameo design.cameo --circuit-json board.circuit.json` to consume `pcb_board`, `pcb_component`, `pcb_smtpad`, `pcb_plated_hole`, `pcb_trace`, `pcb_via`, and `pcb_ratsnest` directly in TypeScript/Web tooling.
