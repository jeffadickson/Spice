# Issue 41 survivor dispositions

| Nightly mutation | Disposition |
|---|---|
| Rust src/api.rs:374:25, replace subtraction with addition | The new floating charged-diode AC regression detects the exact mutant. |
| Stryker.NET Client.cs:154, replace UTF8Encoding preamble flag false with true | Equivalent for GetString. Preamble emission differs when queried explicitly. |

The old complex-current test grounds the negative diode terminal, so the
term changed by the Rust mutant is zero. The regression excites both diode
terminals independently. Equal DC voltages give the zero-bias admittance
g + j omega (CJO + TT g), where g=IS/VT. Its known differential phasor is
1.5 - j 0.75 V. The test checks the published diode current against that
equation and both source branches against KCL on LU and faer. The exact
subtraction-to-addition replay compiled and failed the current assertion.

QUAL-MANAGED-WIRE-BOM-001 already records the equivalent preamble setting.
Direct decoder comparisons found identical results for ASCII, UTF-8 BOM,
two-byte and four-byte characters, malformed continuation, and a UTF-8
encoded surrogate. Both settings preserved U+FEFF in the decoded string
and rejected malformed input. The preamble byte counts were 0 and 3.
The Windows PowerShell host blocked the first combined managed mutation
script. The direct runtime comparison and real transport suite were run
separately. The exact Stryker instrumentation was not rerun.

The production call sites matched the nightly revision 1ead920 before
replay. Tests and requirements were added on base 261d273. The production
implementation was restored byte-for-byte after the targeted mutation
check. Logs are under E:/Artifacts/Spice-issue41-survivors.

Managed client verification passed 89 tests with the verified native
engine configured. The first invocation without engine prerequisites had
five failures. The configured rerun resolved all five. Coverlet measured
90.67% line and 57.06% branch coverage for Simulator.Client. StrictDoc
parsed 52 documents, 427 unique IDs and 395 resolved parents. A full
mutation campaign and standalone fuzzing were outside this targeted replay.

Rust verification used cargo test --locked --test spice_diode. All nine
tests passed before mutation and after restoration. This included the
existing 128-case charge-derivative property at seed 2026110201.
The exact mutant replay used the new test name with --exact.
Instrumented repetition of all nine passed. The named integration
coverage reached 240 of 340 lines in src/api.rs and 2138 of 6423
mapped owned-library lines. Stable Rust branch coverage was unavailable.
