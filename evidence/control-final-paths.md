# Prefix control and safe redirect verification

The four remaining paths from the b2c50e4 control cohort all execute their
original requested analyses and agree with ngspice 47+. The two original
decks each have a byte-identical parallel mirror.

- if-test-1 runs OP. The managed report matches the native conditional output
  and v(1) = 42 V. Three intentional missing condition/name cases choose
  false and retain diagnostics.
- new-check-4 runs a three-point DC sweep. Its ten checks produce INFO: ok.
  The logical capture for unwanted_output_file_3 contains the same scalar
  line as the native output file. Native execution was limited to the
  dedicated E: witness directory. Managed execution opened no deck paths.

The focused replay summary SHA-256 is
fcb2dfe306a809fd07c5dd6f4651abe92fe1ba098c40e5dbbb37761d8a7e286f.
Raw artifacts are under E:/Artifacts/Spice-control-final. The projection
compares circuit values independently of terminal and prefix reporting.

## Full 928-path census

The same frozen path/hash inventory completed in 1010.95 seconds with matching
source and binary provenance. Tested source was b2c50e4 plus the immutable
prefix patch recorded by the raw source hashes. Circuit preparation remains
85 paths. Requested execution increased from 25 to 43 paths, and native
agreement increased from 12 to 29. No previously agreeing path regressed.
All 14 originally if-blocked paths now execute and agree. Four additional
repository substitution fixtures execute, with three agreements and one
observable gap in semantic-zero-port.cir. There are 14 executed paths without
complete agreement and four separate requested-execution failures.

Verification: 471 syntax tests passed with one platform skip, 535 frontend
tests passed with 36 opt-in skips, and StrictDoc parsed 51 documents. A
reverse-inequality mutation was detected. The main evaluator mapped
81.73% lines and 48.24% branches; RequestedControlPreparation mapped
81.94% lines and 73.03% branches in the scoped runs. Earlier build-output
locking failures and the stopped preliminary full run remain in raw logs.
The final reruns use isolated build directories. No full Stryker run occurred.

Bounded stage counts, transitions and burndown are in control-prefix-full.json
and control-prefix-full.svg. Full summary SHA-256:
bd818e28d131a3c42eef30f804f7778659346ea987d893f8ec50689f2b5c0826.
