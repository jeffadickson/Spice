# Original UIC transient replay

All three original decks completed and agreed with ngspice-47+ on every
positive engine sample and every mapped node voltage and branch current.
The previous replay of the same three input and expanded-deck hashes had
zero requested executions and agreements. The measured scope is these three
paths.

| Original deck | Engine method | Compared positive samples | Largest voltage error (V) | Largest current error (A) |
|---|---|---:|---:|---:|
| compat/benchmarks/rc-transient.cir | trapezoidal | 500 | 2.2274424e-6 | 2.2274424e-9 |
| compat/ngspice/qualification-rlc-decay.cir | trapezoidal | 1024 | 5.4526078e-8 | 4.0953201e-8 |
| compat/ngspice/qualification-diode-charge-transient.cir | adaptive backward Euler | 6771 | 4.2785196e-6 | 4.2785196e-9 |

The comparisons use the existing absolute bounds of 1e-6 V and 1e-9 A
plus 1e-4 times the native magnitude. Each report records exactly one
excluded sample at engine time zero. Original native UIC output begins at
positive time. The retained startup states were also checked against the
source and storage constraints. In wire order, diode startup was
(0.1 V, 0 V, -0.1 mA), RLC was (1 V, 1 V, 0 A), and RC was
(1 V, 0 V, -1 mA).

The RLC initializes its original inductor branch at zero current and solves
the algebraic midpoint. A separate test puts a conflicting .IC hint on
that midpoint and checks that KCL determines its initial voltage. The
constant-charge diode reuses the existing equivalent Shockley plus Q=CV
wire profile. The earlier fixed-step backward Euler trajectories disagreed
for the RLC and diode. The initial reference attempt also exposed the
existing public-wire restriction on explicit initial states for the general
charged junction output profile. The final replay used the existing
public wire and comparison tolerance contracts.

Provenance was valid for the 34.99-second replay on Windows. It tested base
6e8aef0 plus this patch using the existing engine SHA-256
037406562cd2518633d4d05debceb3c5cf3252f16a52f4b68d9bbdde318b4cce.
The corpus revision was e8285746c73a8229cf922487c8ccfcf6bea61bce.

Artifacts are in E:/Artifacts/Spice-uic-9682fa1f. Inventory SHA-256 is
7606025744e200f479034edbec11f6ead4610df7ab9d7669f7e492d3990d8cb6 and
final-replay/summary.json SHA-256 is
ddf95b903a1af096e484445af153e31c9d564298d7cb7375d692a766e38e1480.
Reproduce using the documented Run-RequestedCorpus.cs invocation with this
inventory, the pinned ANTLR jar, the existing engine and native executable,
and a fresh E: output directory.

## Verification

Windows .NET SDK 10.0.403 and StrictDoc 0.30.4. The syntax suite passed 471
with one symlink prerequisite skip. StrictDoc parsed 52 documents, 423
unique IDs and 391 resolved parents. The broad frontend run reported 590
passes, 24 skips and one corpus-progress failure after it exhausted the
120-second admission budget. A subsequent isolated run passed all 12 UIC and
progress tests. Starting that retry before broad Coverlet teardown caused a
shared DLL file lock and invalidated the broad coverage collection.

The FsCheck UIC hint property requested 100 cases with seed 731,20261107.
Two deliberate changes in a disposable snapshot were detected. Replacing
the storage hint with zero falsified the property after one case and
one shrink, from NonNegativeInt 1 to 0. Omitting the UIC flag guard caused
the non-UIC comparison with a missing native startup interval to report
agreement. The replay and these checks used the existing Rust engine
binary. Standalone fuzzing and Rust coverage were outside this checkpoint.

The isolated Coverlet run passed all 65 scoped tests. Coverage was
65.58% line and 30.66% branch across the managed assemblies instrumented
by that filter. RequestedUicStartup.cs measured 93.93% line and 92.10%
branch. RequestedComparison measured 81.53% line and 49.04% branch.
See scoped-coverage/scoped.trx and its Cobertura attachment.
