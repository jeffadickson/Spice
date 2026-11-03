# Third-party source attribution

| Repository path | Author/source | License and revision | Modifications/conditions |
| --- | --- | --- | --- |
| src/spice_diode.rs depletion/transport charge equations | Sven Boulanger / SpiceSharp Components/Semiconductors/Diodes/Dynamic.cs | MIT, SpiceSharp3.2.3 commit34484fa6c66d636f6983626a0a770f75c0484553 | Native Rust adaptation into immutable SI evaluation; existing Shockley conduction reused, stable expm1 integral, strict finite-domain validation, no simulator runtime. Retain third-party/SPICESHARP-LICENSE.txt in redistribution. |

Upstream source: https://github.com/SpiceSharp/SpiceSharp/blob/34484fa6c66d636f6983626a0a770f75c0484553/SpiceSharp/Components/Semiconductors/Diodes/Dynamic.cs
Thevenin and Spice21 were inspected only; no source from either is redistributed here. Local downloaded experiments are qualification artifacts outside Git.

BJT profile/default source reference: SpiceSharp3.2.3 ModelParameters.cs and Dynamic.cs under Components/Semiconductors/Bipolars at the same MIT commit above. BjtPreparation.cs independently composes existing native primitives for the absent-GP-coupling Ebers-Moll subset; default values and charge conventions were checked against those files and the actual pinned ngspice oracle. No full Gummel-Poon implementation or clean-room claim is made.

## Bounded MOS1 channel and composition

- Files: `src/mos1.rs`, `dotnet/Simulator.Frontend/Mos1Preparation.cs` and additive compiler/stamping/client integration.
- Reference author/source: SpiceSharp contributors, SpiceSharp 3.2.3 commit `34484fa6c66d636f6983626a0a770f75c0484553`, `SpiceSharp/Components/Semiconductors/Mosfets/Level1/Biasing.cs`, Level1/Common model parameters and dynamics.
- License: MIT; full retained upstream notice in `SPICESHARP-LICENSE.txt`.
- Modifications: effective SI current-only Rust kernel with analytic terminal partials and explicit forward-body domain refusal; generated composition reuses native junction Q and overlap C. No upstream solver/runtime is embedded. Source-derived equations/defaults are attributed; no clean-room or complete model compatibility claim.
- Independent oracle/source check: installed ngspice MOS1 implementation, especially `mos1temp.c` TOX absent/zero oxide-capacitance semantics. ngspice source is inspected/reference-executed, not redistributed as implementation source.
