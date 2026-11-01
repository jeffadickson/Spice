# Corpus capability inventory

State: **complete_inventory**. Accepted observations: **916/916** required decks; 8 explicitly adjudicated exclusions from 924 inputs.

Presence is not simulation support. Counts overlap; declarations may be unused. Includes are not expanded, LEVEL expressions are not evaluated, and command headings/quoted text remain opaque. No circuits are claimed newly runnable.


## Device and model families

| Construct | Decks | Occurrences | Assessment | Limits |
|---|---:|---:|---|---|
| device:v | 789 | 5484 | partial | Bounded legacy importer subset only; parameter restrictions and analysis limits apply. ANTLR-to-core elaboration is absent. |
| device:r | 463 | 293020 | partial | Bounded legacy importer subset only; parameter restrictions and analysis limits apply. ANTLR-to-core elaboration is absent. |
| device:c | 315 | 320677 | partial | Bounded legacy importer subset only; parameter restrictions and analysis limits apply. ANTLR-to-core elaboration is absent. |
| device:x | 280 | 73474 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| device:a | 239 | 876 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| device:m | 223 | 2008 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| device:q | 117 | 566 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| model:nmos | 92 | 111 | unsupported | The bounded importer accepts only restricted D/R model families. No numerical implementation is qualified for this family. |
| device:l | 86 | 298 | partial | Bounded legacy importer subset only; parameter restrictions and analysis limits apply. ANTLR-to-core elaboration is absent. |
| model:pmos | 83 | 93 | unsupported | The bounded importer accepts only restricted D/R model families. No numerical implementation is qualified for this family. |
| model:npn | 77 | 122 | unsupported | The bounded importer accepts only restricted D/R model families. No numerical implementation is qualified for this family. |
| device:i | 76 | 157 | partial | Bounded legacy importer subset only; parameter restrictions and analysis limits apply. ANTLR-to-core elaboration is absent. |
| device:d | 72 | 535 | partial | Bounded legacy importer subset only; parameter restrictions and analysis limits apply. ANTLR-to-core elaboration is absent. |
| model:d | 59 | 150 | partial | Restricted diode or positive constant resistor contract; geometry, transport, temperature and other parameters are not generally supported. |
| model:adc_bridge | 57 | 70 | unassessed | Observation only; semantic and numerical support not established |
| model:vdmos | 50 | 82 | unsupported | The bounded importer accepts only restricted D/R model families. No numerical implementation is qualified for this family. |
| device:b | 45 | 227 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| model:gain | 44 | 45 | unassessed | Observation only; semantic and numerical support not established |
| model:dac_bridge | 39 | 39 | unassessed | Observation only; semantic and numerical support not established |
| device:n | 37 | 330 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| device:e | 33 | 144 | partial | Bounded legacy importer subset only; parameter restrictions and analysis limits apply. ANTLR-to-core elaboration is absent. |
| model:d_nand | 23 | 23 | unassessed | Observation only; semantic and numerical support not established |
| model:d_source | 21 | 32 | unassessed | Observation only; semantic and numerical support not established |
| model:nbjt | 21 | 21 | unassessed | Observation only; semantic and numerical support not established |
| model:numos | 21 | 25 | unassessed | Observation only; semantic and numerical support not established |
| device:o | 18 | 65 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| device:u | 18 | 118 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| model:ltra | 18 | 50 | unassessed | Observation only; semantic and numerical support not established |
| model:pnp | 18 | 30 | unsupported | The bounded importer accepts only restricted D/R model families. No numerical implementation is qualified for this family. |
| device:f | 17 | 120 | partial | Bounded legacy importer subset only; parameter restrictions and analysis limits apply. ANTLR-to-core elaboration is absent. |
| device:p | 17 | 19 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| model:cpl | 17 | 19 | unassessed | Observation only; semantic and numerical support not established |
| model:d_inverter | 17 | 17 | unassessed | Observation only; semantic and numerical support not established |
| device:z | 16 | 20 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| model:d_dff | 15 | 15 | unassessed | Observation only; semantic and numerical support not established |
| device:g | 14 | 81 | partial | Bounded legacy importer subset only; parameter restrictions and analysis limits apply. ANTLR-to-core elaboration is absent. |
| device:y | 14 | 46 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| model:d_osc | 14 | 14 | unassessed | Observation only; semantic and numerical support not established |
| model:nmf | 14 | 16 | unassessed | Observation only; semantic and numerical support not established |
| model:table2d | 14 | 18 | unassessed | Observation only; semantic and numerical support not established |
| model:txl | 14 | 28 | unassessed | Observation only; semantic and numerical support not established |
| device:k | 13 | 36 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| model:limit | 13 | 13 | unassessed | Observation only; semantic and numerical support not established |
| model:d_and | 12 | 12 | unassessed | Observation only; semantic and numerical support not established |
| model:d_cosim | 12 | 12 | unassessed | Observation only; semantic and numerical support not established |
| model:summer | 12 | 12 | unassessed | Observation only; semantic and numerical support not established |
| model:d_fdiv | 10 | 10 | unassessed | Observation only; semantic and numerical support not established |
| model:divide | 10 | 10 | unassessed | Observation only; semantic and numerical support not established |
| model:mult | 10 | 10 | unassessed | Observation only; semantic and numerical support not established |
| model:numd | 10 | 10 | unassessed | Observation only; semantic and numerical support not established |
| model:pwl | 10 | 10 | unassessed | Observation only; semantic and numerical support not established |
| model:table3d | 10 | 16 | unassessed | Observation only; semantic and numerical support not established |
| model:aswitch | 9 | 10 | unassessed | Observation only; semantic and numerical support not established |
| model:d_to_real | 9 | 9 | unassessed | Observation only; semantic and numerical support not established |
| model:int | 9 | 9 | unassessed | Observation only; semantic and numerical support not established |
| model:lcouple | 9 | 10 | unassessed | Observation only; semantic and numerical support not established |
| model:r | 9 | 14 | partial | Restricted diode or positive constant resistor contract; geometry, transport, temperature and other parameters are not generally supported. |
| model:real_gain | 9 | 44 | unassessed | Observation only; semantic and numerical support not established |
| model:real_to_v | 9 | 9 | unassessed | Observation only; semantic and numerical support not established |
| model:s_xfer | 9 | 11 | unassessed | Observation only; semantic and numerical support not established |
| model:climit | 8 | 8 | unassessed | Observation only; semantic and numerical support not established |
| model:cmeter | 8 | 8 | unassessed | Observation only; semantic and numerical support not established |
| model:core | 8 | 8 | unassessed | Observation only; semantic and numerical support not established |
| model:d_dt | 8 | 8 | unassessed | Observation only; semantic and numerical support not established |
| model:hyst | 8 | 8 | unassessed | Observation only; semantic and numerical support not established |
| model:ilimit | 8 | 8 | unassessed | Observation only; semantic and numerical support not established |
| model:lmeter | 8 | 8 | unassessed | Observation only; semantic and numerical support not established |
| model:oneshot | 8 | 8 | unassessed | Observation only; semantic and numerical support not established |
| model:sine | 8 | 8 | unassessed | Observation only; semantic and numerical support not established |
| model:triangle | 8 | 8 | unassessed | Observation only; semantic and numerical support not established |
| model:zener | 8 | 8 | unassessed | Observation only; semantic and numerical support not established |
| model:seegen | 7 | 7 | unassessed | Observation only; semantic and numerical support not established |
| device:s | 6 | 16 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| model:d_buffer | 6 | 6 | unassessed | Observation only; semantic and numerical support not established |
| model:filesource | 6 | 13 | unassessed | Observation only; semantic and numerical support not established |
| model:print_param_types | 6 | 6 | unassessed | Observation only; semantic and numerical support not established |
| model:sw | 6 | 10 | unassessed | Observation only; semantic and numerical support not established |
| model:d_jkff | 5 | 5 | unassessed | Observation only; semantic and numerical support not established |
| model:real_delay | 5 | 5 | unassessed | Observation only; semantic and numerical support not established |
| device:j | 4 | 7 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| model:ueff | 4 | 5 | unassessed | Observation only; semantic and numerical support not established |
| model:ugate | 4 | 47 | unassessed | Observation only; semantic and numerical support not established |
| model:capacitor | 3 | 3 | unassessed | Observation only; semantic and numerical support not established |
| model:d_process | 3 | 14 | unassessed | Observation only; semantic and numerical support not established |
| model:d_pulldown | 3 | 3 | unassessed | Observation only; semantic and numerical support not established |
| model:d_pullup | 3 | 3 | unassessed | Observation only; semantic and numerical support not established |
| model:d_ram | 3 | 3 | unassessed | Observation only; semantic and numerical support not established |
| model:d_state | 3 | 3 | unassessed | Observation only; semantic and numerical support not established |
| model:d_xor | 3 | 3 | unassessed | Observation only; semantic and numerical support not established |
| model:inductor | 3 | 3 | unassessed | Observation only; semantic and numerical support not established |
| model:pswitch | 3 | 10 | unassessed | Observation only; semantic and numerical support not established |
| model:astate | 2 | 3 | unassessed | Observation only; semantic and numerical support not established |
| model:cpline | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:d_dlatch | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:d_nor | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:d_or | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:d_srff | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:d_srlatch | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:d_tff | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:d_xnor | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:memristor | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:mlin | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:nhfet | 2 | 3 | unassessed | Observation only; semantic and numerical support not established |
| model:nport | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:square | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:this_model_doesnt_exist | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| device:h | 1 | 1 | partial | Bounded legacy importer subset only; parameter restrictions and analysis limits apply. ANTLR-to-core elaboration is absent. |
| device:w | 1 | 1 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| model:absdelay_dly | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |
| model:capacitoric | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |
| model:cpmlin | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |
| model:csw | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |
| model:d_genlut | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |
| model:d_lut | 1 | 3 | unassessed | Observation only; semantic and numerical support not established |
| model:d_pwm | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |
| model:d_tristate | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |
| model:delay | 1 | 5 | unassessed | Observation only; semantic and numerical support not established |
| model:inductoric | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |
| model:njf | 1 | 1 | unsupported | The bounded importer accepts only restricted D/R model families. No numerical implementation is qualified for this family. |
| model:ota | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |
| model:pwlts | 1 | 2 | unassessed | Observation only; semantic and numerical support not established |
| model:r2_cmc | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |
| model:res | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |
| model:uio | 1 | 41 | unassessed | Observation only; semantic and numerical support not established |
| model:urc | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |
| model:utgate | 1 | 3 | unassessed | Observation only; semantic and numerical support not established |
| model:xfer | 1 | 4 | unassessed | Observation only; semantic and numerical support not established |

## Most widespread planning gaps

| Construct | Decks | Occurrences | Assessment | Limits |
|---|---:|---:|---|---|
| directive:.control | 601 | 601 | unassessed | Observation only; semantic and numerical support not established |
| structure:control | 601 | 601 | unassessed | Observation only; semantic and numerical support not established |
| control_command:plot | 453 | 1011 | unassessed | Observation only; semantic and numerical support not established |
| device:x | 280 | 73474 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| directive:.subckt | 244 | 29154 | unassessed | Observation only; semantic and numerical support not established |
| structure:subcircuit | 244 | 29154 | unassessed | Observation only; semantic and numerical support not established |
| control_command:set | 241 | 798 | unassessed | Observation only; semantic and numerical support not established |
| device:a | 239 | 876 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| device:m | 223 | 2008 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| external_resource:include | 197 | 275 | unassessed | Observation only; semantic and numerical support not established |
| directive:.include | 196 | 274 | unassessed | Observation only; semantic and numerical support not established |
| control_command:quit | 191 | 236 | unassessed | Observation only; semantic and numerical support not established |
| control_command:tran | 181 | 202 | unassessed | Observation only; semantic and numerical support not established |
| control_command:let | 171 | 1662 | unassessed | Observation only; semantic and numerical support not established |
| control_command:run | 169 | 177 | unassessed | Observation only; semantic and numerical support not established |
| directive:.options | 154 | 163 | unassessed | Observation only; semantic and numerical support not established |
| directive:.print | 154 | 163 | unassessed | Observation only; semantic and numerical support not established |
| control_command:end | 147 | 544 | unassessed | Observation only; semantic and numerical support not established |
| control_command:dc | 129 | 222 | unassessed | Observation only; semantic and numerical support not established |
| control_command:echo | 126 | 809 | unassessed | Observation only; semantic and numerical support not established |
| control_command:rusage | 124 | 149 | unassessed | Observation only; semantic and numerical support not established |
| control_command:if | 121 | 385 | unassessed | Observation only; semantic and numerical support not established |
| device:q | 117 | 566 | unsupported | No general bounded ngspice-import execution contract for this family; syntax acceptance does not establish numerical support. |
| directive:.param | 110 | 291 | unassessed | Observation only; semantic and numerical support not established |
| structure:paramcard | 110 | 291 | unassessed | Observation only; semantic and numerical support not established |
| directive:.option | 109 | 115 | unassessed | Observation only; semantic and numerical support not established |
| control_command:else | 104 | 202 | unassessed | Observation only; semantic and numerical support not established |
| model:nmos | 92 | 111 | unsupported | The bounded importer accepts only restricted D/R model families. No numerical implementation is qualified for this family. |
| model:pmos | 83 | 93 | unsupported | The bounded importer accepts only restricted D/R model families. No numerical implementation is qualified for this family. |
| model:npn | 77 | 122 | unsupported | The bounded importer accepts only restricted D/R model families. No numerical implementation is qualified for this family. |

## Model LEVEL profiles (literal spelling; unspecified is not an inferred default)

| Construct | Decks | Occurrences | Assessment | Limits |
|---|---:|---:|---|---|
| model_level:d:unspecified | 59 | 150 | unassessed | Observation only; semantic and numerical support not established |
| model_level:adc_bridge:unspecified | 57 | 70 | unassessed | Observation only; semantic and numerical support not established |
| model_level:vdmos:unspecified | 50 | 82 | unassessed | Observation only; semantic and numerical support not established |
| model_level:npn:unspecified | 47 | 82 | unassessed | Observation only; semantic and numerical support not established |
| model_level:gain:unspecified | 44 | 45 | unassessed | Observation only; semantic and numerical support not established |
| model_level:dac_bridge:unspecified | 39 | 39 | unassessed | Observation only; semantic and numerical support not established |
| model_level:d_nand:unspecified | 23 | 23 | unassessed | Observation only; semantic and numerical support not established |
| model_level:nmos:49 | 22 | 22 | unassessed | Observation only; semantic and numerical support not established |
| model_level:pmos:49 | 22 | 22 | unassessed | Observation only; semantic and numerical support not established |
| model_level:d_source:unspecified | 21 | 32 | unassessed | Observation only; semantic and numerical support not established |
| model_level:nmos:8 | 21 | 21 | unassessed | Observation only; semantic and numerical support not established |
| model_level:numos:unspecified | 21 | 25 | unassessed | Observation only; semantic and numerical support not established |
| model_level:pmos:8 | 21 | 21 | unassessed | Observation only; semantic and numerical support not established |
| model_level:pmos:unspecified | 19 | 19 | unassessed | Observation only; semantic and numerical support not established |
| model_level:ltra:unspecified | 18 | 50 | unassessed | Observation only; semantic and numerical support not established |
| model_level:cpl:unspecified | 17 | 19 | unassessed | Observation only; semantic and numerical support not established |
| model_level:d_inverter:unspecified | 17 | 17 | unassessed | Observation only; semantic and numerical support not established |
| model_level:nmos:54 | 17 | 31 | unassessed | Observation only; semantic and numerical support not established |
| model_level:nmos:unspecified | 16 | 16 | unassessed | Observation only; semantic and numerical support not established |
| model_level:npn:1 | 16 | 26 | unassessed | Observation only; semantic and numerical support not established |
| model_level:d_dff:unspecified | 15 | 15 | unassessed | Observation only; semantic and numerical support not established |
| model_level:d_osc:unspecified | 14 | 14 | unassessed | Observation only; semantic and numerical support not established |
| model_level:pnp:unspecified | 14 | 22 | unassessed | Observation only; semantic and numerical support not established |
| model_level:table2d:unspecified | 14 | 18 | unassessed | Observation only; semantic and numerical support not established |
| model_level:txl:unspecified | 14 | 28 | unassessed | Observation only; semantic and numerical support not established |
| model_level:limit:unspecified | 13 | 13 | unassessed | Observation only; semantic and numerical support not established |
| model_level:nbjt:1 | 13 | 13 | unassessed | Observation only; semantic and numerical support not established |
| model_level:pmos:54 | 13 | 21 | unassessed | Observation only; semantic and numerical support not established |
| model_level:d_and:unspecified | 12 | 12 | unassessed | Observation only; semantic and numerical support not established |
| model_level:d_cosim:unspecified | 12 | 12 | unassessed | Observation only; semantic and numerical support not established |

## Source forms

| Construct | Decks | Occurrences | Assessment | Limits |
|---|---:|---:|---|---|
| source_form:pulse | 192 | 2828 | unassessed | Observation only; semantic and numerical support not established |
| source_form:sin | 86 | 101 | unassessed | Observation only; semantic and numerical support not established |
| source_form:pwl | 47 | 83 | unassessed | Observation only; semantic and numerical support not established |
| source_form:trnoise | 10 | 41 | unassessed | Observation only; semantic and numerical support not established |
| source_form:trrandom | 5 | 16 | unassessed | Observation only; semantic and numerical support not established |
| source_form:exp | 4 | 8 | unassessed | Observation only; semantic and numerical support not established |
| source_form:sine | 4 | 4 | unassessed | Observation only; semantic and numerical support not established |
| source_form:snd | 2 | 2 | unassessed | Observation only; semantic and numerical support not established |
| source_form:file | 1 | 1 | unassessed | Observation only; semantic and numerical support not established |

## Next work

1. Build source-located semantic binding and parameter/subcircuit elaboration; keep unavailable external resources explicit.
2. Minimize representative model/LEVEL/parameter profiles from the JSON inventory before selecting numerical implementations.
3. Connect already-supported numerical subsets through the typed boundary, then compare actual results against ngspice.

The JSON contains every observed feature, counts, support evidence and exact campaign hashes. The SVG is a baseline of assessed feature kinds, not a simulation-pass burndown. Prior numerical corpus results remain a separate series.
