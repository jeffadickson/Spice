# Legacy linear-deck annotation evidence

Corpus revision `e8285746c73a8229cf922487c8ccfcf6bea61bce`, native ngspice 47+.
The identical `tests/filters/lowpass.cir` input (SHA-256
`a6a8be760d5787ca836990dac5bcfa3b909dd6ea8bf17c5b2f1be7583d3f2172`)
went from `requested_preparation_refused / unsupported_physical_directive`
at `9ce8c809b35f27d473bfbb96c4dc7a765beb66d6` to `requested_agreed`.
`Run-RequestedCorpus.cs` used the original input to prepare ordinary OP
and AC requests. All mapped voltages and independent source branch
current agreed over 1 OP point and 31 AC points. Native original deck
execution independently produced the same 1- and 31-point grids.

The focused `RequestedOptionConstantTests` run passed 19 tests with no
skips. Its managed Cobertura instrumentation covered 3,884/23,362 lines
and 1,351/7,647 branches across included assemblies. Those are
focused-test aggregate counts. The native engine and ngspice are outside
that instrumentation. Raw evidence is under
`E:/Artifacts/Spice-passive-lowpass-9ce8c80`.

Independent ablations used the identical corpus input. Keeping only the
option handling still refused with `missing_parameter` during source
preparation. Keeping only the distortion handling still refused with
`unsupported_physical_directive`. The two changes therefore address
distinct preparation blockers in the same original deck. Changing the
disabled-distortion guard from zero to one in a disposable copy was
detected by the focused low-pass test.
