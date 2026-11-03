# Refreshed original corpus: static simulation and known blockers

Tested source1b9451a, original928-row inventory, Windows managed coordinator,
existing verified debug Rust executable, LU/Faer and native ngspice.

| Outcome | Files |
|---|---:|
| Complete selected DC solves |38|
| Native DC voltage agreement |37|
| Solved with no exposed root observable |1|
| Lowering refused |852|
| Resource resolution refused |30|
| Hash-pinned exclusions |8|

There are33 distinct root-byte hashes among the38 solved paths, not33 proven
independent expanded circuits.76 provider requests and196 root voltage checks
completed.10 files also match selected1/100/1000Hz AC probes:20 provider requests,
60 frequency points and210 complex voltage checks. No numerical mismatch was
observed. OP orchestration took324.73s and AC9.39s; these are campaign wall times,
not solver performance comparisons.38 replaces35 as the measured solve count;
the gain comes from the previously implemented E/G path. The new owned bug883
fixtures are outside this frozen inventory and are counted separately.

The new audit records multiple known blockers. At least444 roots contain an
unsupported V-source profile,228 XSPICE,217 MOS,127 BJT,86 diode and29 F/H current
control blockers. These overlap; they are not additive or guaranteed unlock counts.
Reporting97 `.options` and83 `.option` blockers does not justify ignoring their
physical effects. Of890 readiness reports,89 have incomplete active graphs and22
truncate bounded observations. Numerical validation remains nonexhaustive.

Prioritize the source-profile family first. Existing Rust PULSE, sinusoid, PWL
and exponential models can be reused through generated source metadata and
explicit typed transient operations. No new lexer/parser or waveform engine is
needed. Preserve all unsupported defaults/forms as explicit capability limits,
and verify DC initial values independently before counting simulation growth.

Requirements SEM-READINESS-001, SEM-EXEC-001, SEM-ORACLE-001 and SEM-ACPROBE-001
remain Draft. `evidence/corpus-static-readiness.json` retains exact source/binary,
root and report hashes and the complete bounded category tables. Raw reports
stay on E:. This report-only checkpoint does not claim new code coverage; the
unchanged implementation's scoped coverage is in the preceding switch evidence.
Selected probes do not execute the original input programs or qualify full SPICE.
