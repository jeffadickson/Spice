# MOS1 audit survivor assessment

Basedccb863. Production MOS1 solver/model and audit source are unchanged by this
checkpoint. Requirements: `audit-mos-polarity.sdoc`. Results and immutable
original/current mappings: `evidence/audit-mos-polarity-results.json`.

The rebuilt final seven-minute stage tested5 distinct candidates:4caught,
1rawMissed retained as the reviewed zero-body boundary equivalent. Both original
polarity/domain survivor counterparts are caught. Including the earlier four
candidate attempts gives9attempts on5names; the failed follow-up baseline adds
zero candidate outcomes. No final invalids/timeouts or frozen-score rewrite.

## Two genuine missing-test cases

Frozen audit693 removing PMOS polarity inversion maps to current703. Independent
manufactured states use the closed-form memoryless triode current57.75uA:
beta2e-4, normalized overdrive0.8V, span0.5V, lambda0.1/V. Both channel polarities,
drain/source reversal and common-mode translations must conserve terminal
current and power. Four ideal clamps fix supplying currents analytically;
corrupted source currents fail. No production current/Jacobian/solver is the
expected-result oracle. Stationary BE checks use two non-unit timesteps.

Frozen audit701 weakening the finite-differential OR to AND maps to current711.
Finite absolute potentials can overflow body bias while channel span stayszero,
or overflow span while body staysfinite. Bare-MOS audit fixtures avoid incidental
ideal-clamp overflows. The body case becomes a **passing cutoff audit** under the
mutation, despite unrepresentable body bias. The span case instead reaches a
later NumericalFailure; requiring the existing InvalidInput admission category
detects that weaker validation. Those two failure mechanisms are distinct.
The corresponding raw failed tests retain both actual outcomes.

96 Proptest cases with seed2026111908 transform a fixed independently known
current across polarity/orientation/common mode. Nonzero body coefficients at
zero body bias additionally retain the same threshold/current. This is a bounded
memoryless audit fixture, not full MOS derivative or dynamic-charge qualification.

## Additional zero-body boundary survivor

Frozen audit708 `body>0` to `body>=0` maps to current718. Exact-diff review finds
both body-shift branches zero at body+/-0 for validated finite positive phi and
finite nonnegative gamma. Only private signed-zero bits can differ. Current
public KCL/power/energy accumulation preserves physical report results under
default nontrapping f64 behavior. This is bounded source review, not formal or
bitwise proof, and excludes production body-root derivatives, charge, future
private-value logging and neighboring `==`/`<` mutations. No guard/filter or
physics change was introduced merely to make this candidate fail.

## Replay-cache failure and correction

The initial four-candidate stage caught both original counterparts and two
additional negated finite guards. After adding the zero-body coefficient test,
the separate boundary follow-up failed its **unmutated baseline** and tested
zero candidates. Ordinary qualification remained passing in its separate cache.
Its errors matched the preceding last guard mutant; the build recorded a Fresh
owned library while recompiling the integration target, consistent with reuse
of the prior mutant-linked artifact. Cargo's internal cache mechanism was not
independently traced. Restoring source was not cache qualification.

The failed baseline is preserved as unavailable source-correspondence evidence,
not a product defect or compile-invalid candidate. Cargo's owned-package clean
removed51files/133.1MiB from only the replay target. The final combined stage
requires a fresh owned-library build and positive/negative unmutated contracts
against the final five-test source. AGENTS.md and the native-lane reproduction
guide now require this clean before each separate targeted invocation. The
active4411 campaign uses another cache and its frozen helper/config were unchanged.

Keep initial, failed follow-up and final native outcomes separate; do not sum
duplicate mutant attempts as distinct candidates. A rebuilt passing baseline
does not itself establish completed candidate outcomes. The machine evidence
records terminal final outcomes only after the replay finishes.

## Independent oracle and verification scope

Fresh ngspice47+ DC fixtures cover12 combinations (NMOS/PMOS, both orientations,
three common modes). At17 printed digits their maximum supplying-current error
against the analytical fixture is5.100000215435378e-13A, within the unchanged
2e-11A bound. Initial lower-precision outputs remain separately preserved;
`ngspice-precision-summary.json` is the authoritative high-precision reference.
This is an oracle check of manufactured current states, not new original corpus
decks or a per-engine speed comparison. No transient/AC oracle scope is implied.

Final baseline:51unit +689integration pass,7ordinary skips; five focused tests,
96Proptestcases seed2026111908. Scoped owned audit coverage290/586lines; stable
Rust branch, test/vendor/native-model/main coverage unmeasured. Do not compare
different named suites as an overall trend. Existing source's Intel routine and
UHD630 FP64 probe passed. Real StrictDoc parsed117documents/637unique IDs.

Read-only review found no important sign/oracle defect and explicitly distinguished
body false-pass from span error-category failure. No fresh standalone fuzz engine,
Stryker, solver/model implementation, formal or full SPICE/GPU claim. Unmutated
core source remains at the previous verified hash. API128 multiply-invalid error
precedence and the separate epoch Windows executable-removal cause remain open.

Raw root: `E:/Artifacts/Spice-mos-audit-20261119T072340Z`; final test source is
retained there. Build/TEMP/TMP/oracle/mutation/coverage output stayed on E:, with
ordinary, coverage, replay and active-epoch targets separate. No generic memory
caps/free-RAM gates, unrelated jobs stopped, CI or main merge.
