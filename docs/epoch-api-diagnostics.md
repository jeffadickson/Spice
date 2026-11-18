# Second epoch checkpoint: API admission, overflow and structured diagnostics

This checkpoint changes tests, not production numerical expressions. The frozen
4,411-candidate source and ledger remain untouched. Five of seven selected
counterparts are caught by current regressions: the finite-computation overflow
guard, three adaptive rejection-event thresholds, and the inclusive API case-ID
boundary. Two raw survivors have distinct reviewed scopes, described below.

The API tests accept and preserve both ASCII and UTF-8 identifiers at256bytes,
reject257bytes, and exercise independent supported requests. A linear RC with an
explicit0.25V initial state follows the analytic backward-Euler sequence
V(n)=0.25/(1.1^n). A charged Shockley diode supports DC with current
1e-9*expm1(2); only its explicit-state dynamic combination remains refused.
These charge-admission tests are useful additional contracts, but they do not
kill the memoryless-profile predicate mutation at api128.

Finite trial inputs can still overflow their subtraction or tolerance scale.
With opposite signed f64::MAX values, the discrepancy is nonfinite; with a
finite f64::MAX absolute tolerance plus a positive finite relative term, the
scale overflows and division could falsely yield zero. The new private unit
requires InvalidInput for both independent cases. Nonfinite-input tests alone
could not detect this mutation because the earlier operand guard rejected them.

A thread-local tracing_subscriber layer captures actual structured DEBUG events.
Analytic scaled errors0,1,4/3 must yield exactly one
adaptive_local_error_rejected event, with scaled_error=4/3. In particular an
accepted exactly-one boundary must not be logged as rejected. The precise event
contract is explicitly inferred/Draft rather than a public stable log-schema
promise. Three logging-only guard mutations are now caught; do not describe
these as three production numerical bugs.

## Reviewed survivors and limitations

Changing the maximum-selection comparison from>to>= preserves the numerical
norm for admitted integration orders. A tied diagnostic may identify another
equally worst unknown. No first-tie identity guarantee is asserted; this is
numerical equivalence with a diagnostic difference, not byte identity.

The api128 mutation is the memoryless behavior preflight A&&B to A||B, not the
charged-junction predicate. For valid analysis parameters, AC, basic transient
and configured transient controllers independently refuse the same unqualified
behavior profiles. A separate replay with supported DC/BE controls and valid
unsupported AC/basic transient/trapezoidal/initial-state/storage cases still
survives. This is scoped duplicate admission, not full typed-error equivalence:
for multiply-invalid configured requests the preflight can return Unsupported
before numeric validation, whereas a bypass can return InvalidInput first.
The intended error-priority policy remains an explicit open question in issue63.
No production gate was removed or weakened to improve a mutation score.

## Verification

Native stages: first7=4caught3survived; overflow follow-up1=1caught; strengthened
profile follow-up1=1survived. Unique selected findings:5caught,1reviewed numerical
tie equivalence,1scoped profile-redundancy/error-priority limitation. Historical
frozen outcomes remain unchanged. The separately observed invalid AC-arm deletion
has Rust E0282: removing the sole ac_bias producer leaves its type uninferred.
It is a compile-invalid generated program, not a product failure or test kill.

Full default-feature regression:51unit+656integration passed,6explicit skips.
New focused API contracts:3passed; new private unit contracts:2passed. Four
aggregation checks, real StrictDoc parsing,15actual Intel routine tests and the
separate SpMV probe passed. Scoped LLVM API line coverage and source hashes are
in the evidence JSON. Stable Rust branch coverage is unavailable. The adaptive
cfg(test) logger/overflow code is behavior-tested but not measured by that
integration-library coverage scope. Existing properties ran in the full suite;
no separate new fuzz engine or Stryker campaign ran. No new original source
deck, GPU speedup or full SPICE/production qualification is claimed.

All raw reports and failed setup attempts remain on E:. The first final StrictDoc
invocation had an invalid caller path after Pester and was rerun independently
with an existing explicit repository directory; only the successful parse is
qualification evidence. Read-only agent review found no important test or
numerical findings; this was not participation by external experts.
