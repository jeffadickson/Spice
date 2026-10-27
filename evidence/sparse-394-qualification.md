# Sparse AC checkpoint 394

Campaign steps 51–60 (385–394), source 2daedab plus this checkpoint's
documentation/evidence. Sixty of the interpreted 100 implementation steps are
complete; the final review is pending. Full build, 297 Rust tests, 29 managed
checks, 61 xUnit cases, formatting, Clippy and analyzer/XMLDoc gates pass.
The intentionally ignored legacy endurance test remains separate.

The finite AC tranche now includes inclusive grid/source/retained-scalar limits,
independent malformed override fields and overflow rejection, both live-terminal
current signs, analytical RC generation, series RLC through resonance with signed
source/inductor currents, E/F/G/H complex transfer equations and forward/reverse
biased diode incremental admittance. The raw API's nominal source-as-AC-amplitude
convention remains. Nonlinear DC bias still uses bounded reference Newton;
incremental AC stamps and faer complex solves consume CSC directly. Prepared
complex factor reuse and arbitrary topology/conditioning remain unqualified.

Typed C# optional phasors retain omitted-field wire compatibility. Real-engine
LU/faer tests and examples/TypedAc.cs verify (1+2j)/(1+j)=1.5+0.5j, including
the large-nominal cancellation regression. The typed SDK supports fewer element
families than native JSON; the named oracle runner explicitly consumes the native
public JSON contract, preserving valid controlled-source cases.

Actual ngspice 47+ comparisons now select LU and direct faer explicitly:

| Named case/observable | Frequencies | Absolute component bound |
| --- | --- | --- |
| Upstream res_partition v(2) | 1 Hz | 1e-12 |
| Upstream lowpass v(2) | 1 kHz | 5e-8 |
| Original series RLC v(out) | 100/1000/10000 Hz | 5e-7 |
| Original VCCS load v(out) | 10/100/1000 Hz | 5e-6 |

Print rounding constrains the external bounds; attempted .options/CLI numdgt
settings did not improve observed precision and are not claimed as effective.
The runner disables user spiceinit, copies decks verbatim, checks named
columns/frequencies and records deck/request/executable/helper hashes.
Both original decks also pass importer-to-analytical-equation tests. They are
clearly labeled original Spice inputs, not copied upstream corpus growth.
The existing eight-entry upstream selection validates unchanged.

Eight parser contracts reject malformed, missing, duplicate, negative and
nonfinite reference data. An intentional zero-source/reference mismatch actually
returns exit 1 and saves a failed comparison report, retained as
named-ac-intentional-mismatch-394.json. It is a negative harness probe, not an
unexpected numerical failure. Positive comparisons were rerun afterwards.

Mutation evidence remains separate by snapshot:

* The earlier fa964ad 20-minute AC run ended partial at 1200.306 s:
  80 inventoried, 32 caught, 15 survivors, 24 unviable, nine unattempted.
  No per-mutant timeout was recorded. See sparse-384-mutation-final.json.
* Its boundary/current/field survivors drove new independent regressions.
  A 28-candidate selected follow-up remains active on an isolated snapshot:
  sparse-394-mutation-partial.json records three caught, seven unviable,
  zero survivors at the explicit ten-completed snapshot. This is not a final
  score; retain the eventual result at checkpoint 404.
* A division-to-multiplication residual-normalization survivor exposed a further
  oracle gap. The new scale-invariance test accepts near solutions and rejects
  inaccurate ones across 1e-120..1e120 equation scales. It passes; that specific
  generated mutant has not yet been rerun with the new test.
* Stryker was not rerun here. Existing Client.cs mutation evidence remains
  historical; the added Requests.cs DTO is not within that configured scope.

Fresh coverage:

| Scope | Lines | Branches |
| --- | --- | --- |
| Linux owned Rust on 2daedab | 4768/5288 (90.17%) | unavailable |
| Owned C# client, Coverlet | 162/170 (95.29%) | 109/112 (97.32%) |
| Named oracle helper | 126/128 (98.44%) | unavailable in this export |

All Rust src/test and managed source/test hashes match their measured scopes.
The 70 LLVM warnings resolve to two duplicated zero-hash tracing_core symbols;
no owned/unexpected symbols appear. Vendor internals, GPU, broad host/platform,
full parser fuzzing, oracle timeout/pipe-bound paths and PowerShell coverage remain
unmeasured. The helper's direct collector Cobertura is retained: ReportGenerator
conversion produced a different 127/128 line union and excluded generated-source
warnings, so that alternative count is not silently substituted.

StrictDoc 0.30.4 parses all 13 documents: 231 unique IDs, 217 resolving parent
links. SPARSE-001..008 remain Draft. Sphinx 9.1.0 builds with warnings as errors
and 225 generated XMLDoc entries.

Next: 395–414 direct sparse transient assembly, prepared-factor ownership/reuse,
rollback/adaptive/provider contracts and independent dynamic equations/oracles.
Then bounded models 415–434 and final deep review/fixes 435. The tight bridge
failure and earlier partial campaign evidence remain. Full SPICE, GPU,
large-circuit performance and production qualification are not established.
