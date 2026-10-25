# Evidence policy for the second slice

The historical checkpoint reports 10–50 and planning-validation.json describe earlier snapshots. They must not be used as current source verification. The current checkpoint 100, mutation source hashes, managed coverage source inventory and implementation-validation.json form the latest evidence set.

The required builds for the additional 50 passes are 60, 70, 80, 90 and 100. Validation repairs can amend the corresponding checkpoint commit; they do not create fictitious extra iteration numbers.

Mutation tests run in an ignored copied sandbox, classify killed/survived/invalid/timeout outcomes, restore the copied source and retain actual hashes. Bounded random/fuzz-smoke input tests are not a claim of coverage-guided fuzzing completeness.

Managed coverage includes all owned client .cs files, separately excludes the conformance executable and records line/branch counts where available. It does not measure Rust, faer internals, PowerShell scripts, native plugins or GPUs. The selected Windows GNU Rust toolchain lacks profiler_builtins; the supported-host Rust coverage script/CI configuration is not proof of a run on that host.

Local result artifact integrity is tested with round trips, collisions and corruption, not by claiming distributed transactions or power-loss durability. No performance speedup, stable production compatibility or GPU qualification follows from the additional iteration count.

The mutation baseline runs the full suite; each mutant runs its relevant integration target. Managed coverage filters exact owned source paths and unions source line locations; generated JSON metadata is excluded. Compiler branches mapped to owned source remain included. Schemas are exported from DTO metadata; no independent JSON Schema validator was run.
