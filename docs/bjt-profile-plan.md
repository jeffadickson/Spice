# Bounded Ebers–Moll BJT implementation plan

Goal: native and generated NPN/PNP DC, AC and backward-Euler execution with genuine CJE/CJC depletion and TF/TR transport storage. This is the default GP parameter subset where Early/high-injection/leakage/modulated-base/excess-phase effects are absent, not a general Gummel–Poon implementation.

Reuse: existing Shockley junctions, existing CCCS and zero-voltage senses, and the qualified nonlinear junction Q kernel. Add an explicit charge-only `junction_storage` element with no F contribution. Keep the current senses in conduction-only paths; charge companions bypass those senses so CCCS transport never amplifies displacement.

For NPN, Dbe has IS/BF and Dbc IS/BR; collector-emitter transport is BF*Ibe-BR*Ibc. Parallel BE storage uses effective IS, NF*kT/q, CJE/VJE/MJE/FC and TF; BC storage uses effective IS, NR*kT/q, CJC/VJC/MJC/FC and TR. PNP mirrors all junction/transport polarities. An outer zero-voltage collector sense provides signed total collector current, including BC storage. Internal nodes bypass public source-name mappings.

Qualified first model names: IS,BF,BR,NF,NR,CJE,VJE,MJE,CJC,VJC,MJC,FC,TF,TR,TNOM=27 and LEVEL=1 only. Three-terminal default profiles only. Refuse unknown, duplicate, opaque or instance parameters and unsupported substrate/fourth-terminal, VAF/VAR, IKF/IKR, RB/RC/RE, leakage, temperature or excess-phase terms. Broader profiles remain open.

- [x] Observe charge-only DC/AC/BE tests fail on the old unknown wire tag.
- [x] Add immutable typed charge-only profile, shared Q/history use, explicit audit/observable semantics and domains.
- [x] Verify NPN/PNP composed currents against independent Ebers–Moll equations; sensor current must exclude parallel Q.
- [x] Add generated model binder, private node/element construction with budgets, and collector source mapping; strict refusals and lexical model scope.
- [x] Compare generated typed payloads on LU/faer against real ngspice DC, complex AC and nonzero-charge transient V/I, with refined-grid/negative/property checks as appropriate.
- [x] Capture scoped coverage, mutation dispositions, StrictDoc and documentation/source fingerprints, then commit separately. MOS level1 remains next open scope.
