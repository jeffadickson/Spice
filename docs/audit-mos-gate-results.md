# Finite-threshold, overflowing-overdrive admission

Baseb761893. The newly completed frozen survivor audit716:31 changes the
threshold/overdrive finite-guard OR to AND; its current counterpart is726:31.
Production source remains unchanged. Requirement:
`AUDIT-MOS1-OVERDRIVE-ADMISSION-001` in `audit-mos-polarity.sdoc`.

The final four-minute native replay caught3/3 candidates: this frozen-survivor
counterpart and two additional negated finite guards. No missed, invalid or
timed-out candidates. The owned library was freshly rebuilt after Cargo clean
and all six linked tests passed in its unmutated baseline.

The previous span/body tests kept gate overdrive finite. The new test instead
keeps normalized drain=source=bulk, so channel span/body are zero and normalized
threshold is+0.7V. Only gate-minus-effective-source overflows. Both MOS polarities
and signs of overflow are checked through DC and stationary BE public audits.
Declared model thresholds are+/-0.7V; no model evaluation or solver supplies the
expected error. Bare-MOS states avoid unrelated ideal-source bound failures.

Under the weakened guard, negative-infinite overdrive can be clipped into zero
current and a passing cutoff audit. That is the executed mutation counterexample.
Source review also predicts positive-infinite overdrive can reach a later
NumericalFailure via0*Inf rather than the required InvalidInput. The unmutated
test exercises both signs; its negative-case panic may stop the mutant before
the positive case. This is admission of malformed finite-potential arithmetic
states, not qualification of physically extreme devices or solved decks.

The replay invalidated only its owned package with Cargo before starting, per
the prior cache finding. The native baseline must explicitly rebuild the owned
library and pass six linked positive/negative tests before candidate outcomes
are accepted. Original and current names/source/diff hashes remain distinct.
The live campaign, ordinary tests and coverage each use separate target caches.
No frozen source/configuration/helper, physics or tolerance changes were made.

Full regression:51unit+690integration passed,7ordinary skips. The named target
has six tests, including the retained96-case property with fixedseed2026111908;
the full-suite environment seed is2026111909. Scoped library audit coverage is
293/586lines. Stable Rust branch, test/vendor/native-model/main coverage is
unmeasured; a named-suite metric is not an overall coverage trend. Real StrictDoc
parsed117documents/638unique IDs. Intel routine and separate probe results, final
native mutation counts and exact source hashes are in the machine evidence.

No fresh standalone fuzz engine, Stryker or ngspice run is claimed for this
test-only checkpoint. Prior12-case ngspice evidence remains historical for the
unchanged analytical fixture/source. No new original deck, solver/model change,
speedup, full SPICE/GPU qualification or formal proof is claimed. Read-only review
confirmed independent overdrive isolation and exact old/current guard mapping.

Raw root: `E:/Artifacts/Spice-mos-gate-audit-20261119T080057Z`.
All new build/TEMP/TMP/snapshot/log/coverage output stays on E:. No general memory
caps/free-RAM gates, unrelated jobs stopped, CI or main merge. The ongoing4411
campaign remains a frozen historical snapshot; these retests do not rewrite it.
API128 error precedence and the separate Windows executable-removal cause remain
open. Final native outcomes are recorded in `evidence/audit-mos-gate-results.json`.
