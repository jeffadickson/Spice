# Project instructions

- Before substantial additional parser/solver/model implementation, apply the canonical reuse-before-build gate. Evaluate complete existing solutions, including SpiceSharp/SpiceSharpParser and ngspice integration, rather than limiting the search to Rust crates. Record concrete gaps and executable evidence; do not treat the current language/architecture choice as proof that reuse is unsuitable. An evaluation does not itself authorize a production migration.

- Read `docs/implementation-plan.sdoc`, `docs/architecture.sdoc` and `docs/development-standards.sdoc` before implementation.
- Use C# or F# for scripts/automation by default and PowerShell cmdlets for shell/text/file operations. A required tool runtime, such as StrictDoc's Python runtime, is an explicit exception rather than a default scripting choice.
- Keep authoritative requirements in StrictDoc with stable IDs and honest implementation/test evidence links. Update `backlog.json` from the plan when work-package metadata changes.
- Comment public interfaces, ownership, units, numerical assumptions, invariants and non-obvious logic meaningfully; update comments alongside code.
- Collect actual line/branch coverage where supported, report exclusions and unavailable metrics, and apply relevant mutation, malformed-input, fuzz and randomized/property tests with reproducible seeds and minimized failures.
- Keep parsing, immutable circuit/plan structures, model evaluation, numerical controllers, backend adapters, runtime and results separated as specified. No legacy simulator runtime dependency in the product core.
- C#/F# and GUI code consume the public API, not internal solver/model state. Definition DSL evaluation does not run in the Newton/device hot loop.
- Do not promise arbitrary solver combinations, universal GPU acceleration, native-code sandboxing from Rust alone, universal convergence or compatibility without qualification evidence.
- Deliver the next evidence-backed vertical slice from the plan rather than creating all proposed crates or adapters at once. Do not infer completed work from planning documents.
- When resuming an active qualification goal, inspect the current worktree, requirements, evidence, and handoff prompt before acting. Preserve the goal's full scope, make concrete verified progress, and leave it active unless completion or the documented blocked audit is proven.
- Maintain the repository as a careful long-term steward: prefer small reversible commits, keep the handoff material accurate, and thank contributors respectfully for their guidance and trust.
- Never commit build outputs, runtime environments, credentials, downloaded native binaries or large fuzz/benchmark corpora. Record reproducible acquisition instructions and immutable hashes when those assets become necessary.
