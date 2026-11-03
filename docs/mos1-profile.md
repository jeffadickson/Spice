# Bounded MOS LEVEL=1

The qualified profile uses the Shichman-Hodges channel with analytic four-terminal derivatives. Model terms: LEVEL=1 (default), VTO, KP, GAMMA, PHI, LAMBDA, IS, CBD, CBS, PB, MJ, FC, CGSO, CGDO, CGBO, TNOM=27, and absent/zero TOX. Explicit positive instance L and W are required; all other instance/model terms refuse. Effective beta is KP*W/L. Nonzero GAMMA requires nonpositive body bias relative to the effective source, including reversed operation.

Defaults follow the pinned SpiceSharp 3.2.3 source: VTO=0, KP=2e-5 A/V², GAMMA=0, PHI=.6 V, LAMBDA=0, IS=1e-14 A, CBD/CBS=0 F, PB=.8 V, MJ=.5, FC=.5, overlap factors=0. The source-reference commit is `34484fa6c66d636f6983626a0a770f75c0484553`. The MIT license and attribution are retained under `third-party`.

The native `mos1_channel` law contributes only channel F/J, with `i_channel(id)` as an explicit observable. Generated M cards compose body junctions with the qualified nonlinear depletion Q and overlap capacitors CGSO*W, CGDO*W, CGBO*L. An outer zero-volt drain sense supplies signed total `i(M)` including displacement. Accepted-step BE history and rollback belong to the existing primitives; no private mutable MOS charge state is introduced.

Nonzero TOX requires intrinsic Meyer charge and is explicitly unsupported. Both ngspice and the reference source use zero oxide capacitance when TOX is absent/zero; genuine body and overlap storage remain active in that profile. Overlap C is not intrinsic gate-charge parity. No full LEVEL=1 compatibility, higher MOS levels, arbitrary temperature, series resistance, sidewall/area junction geometry, mobility/oxide-derived KP or forward-body GAMMA approximation is claimed.

Verification source: `tests/mos1_channel.rs`, `Mos1Tests.cs`, `Mos1NativeOracleIntegration.cs`; requirements `SEMI-MOS1-*` in `mos1-profile.sdoc`. Native comparisons use nonzero body/overlap charge on NMOS/PMOS and LU/Faer, against installed ngspice, and retain exact requests/results and a frozen engine hash. Numerical tolerances and finite tested scope are recorded with the evidence.
