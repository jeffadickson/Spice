# Third-party source attribution

| Repository path | Author/source | License and revision | Modifications/conditions |
| --- | --- | --- | --- |
| src/spice_diode.rs depletion/transport charge equations | Sven Boulanger / SpiceSharp Components/Semiconductors/Diodes/Dynamic.cs | MIT, SpiceSharp3.2.3 commit34484fa6c66d636f6983626a0a770f75c0484553 | Native Rust adaptation into immutable SI evaluation; existing Shockley conduction reused, stable expm1 integral, strict finite-domain validation, no simulator runtime. Retain third-party/SPICESHARP-LICENSE.txt in redistribution. |

Upstream source: https://github.com/SpiceSharp/SpiceSharp/blob/34484fa6c66d636f6983626a0a770f75c0484553/SpiceSharp/Components/Semiconductors/Diodes/Dynamic.cs
Thevenin and Spice21 were inspected only; no source from either is redistributed here. Local downloaded experiments are qualification artifacts outside Git.

BJT profile/default source reference: SpiceSharp3.2.3 ModelParameters.cs and Dynamic.cs under Components/Semiconductors/Bipolars at the same MIT commit above. BjtPreparation.cs independently composes existing native primitives for the absent-GP-coupling Ebers-Moll subset; default values and charge conventions were checked against those files and the actual pinned ngspice oracle. No full Gummel-Poon implementation or clean-room claim is made.
