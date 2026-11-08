# Resistor instance-default evidence

The original ngspice `tests/regression/model/instance-defaults.cir` (corpus
revision `e8285746c73a8229cf922487c8ccfcf6bea61bce`, input SHA-256
`fc8b1f4759e3efdc01b8b253352949ceba027b8fe6b60a5ef5a76f27328f8c5d`)
moved from `requested_preparation_refused / unsupported_resistor_model_parameter`
at `9ce8c809b35f27d473bfbb96c4dc7a765beb66d6` to
`all_individual_requested_analyses_agreed`. The single requested OP
point matched both node voltage and voltage-source current exactly.
The original unprojected ngspice 47+ batch exited 0 and printed
`INFO: ok` after its current assertion. The matched individual oracle
projects away the terminal conditional and its quit/report effects.

The focused managed resistor and option tests passed 54/54, including
128 generated resistance defaults with seed 20261107 and negative
duplicate/nonpositive cases. Cobertura covered 4,299/23,364 lines and
1,623/7,653 branches across instrumented assemblies in that focused
run. The native engine and ngspice are outside that instrumentation.
The raw reports are under `E:/Artifacts/Spice-passive-lowpass-9ce8c80`.

A targeted mutation redirected RESISTANCE to the sheet-width adjustment
slot. The example and property checks detected it. The property failed on
its first generated case and shrank to byte 0 (a 1-ohm default), with replay
pair `20261107,11400714819323198485`. The same disposable test run also
changed the independent DISTOF zero guard, which its separate option/source
test detected. StrictDoc validation parsed 53 documents and 438 IDs with
406 resolved parent references.
