# Requested directive profiles

The initial checkpoint qualifies fixed 27 C operating temperature, reporting
encoding and width, and the analysis-specific effect of a root node .IC.
Native OP fixture op-ic.cir retains .IC V(out)=99, runs independently, and
agrees with the unclamped divider. Existing transient clamp fixtures retain
their original analysis and initialization contracts.

## Original corpus replay

Artifact root E:/Artifacts/Spice-options-b192a35f. The 74-path inventory
includes initial .IC, .TEMP and .WIDTH refusals and decks with a candidate
literal TEMP=27 or FILETYPE option. The complete replay matches all 74 original
input identities and expanded SHA-256 hashes from the earlier 928-path census.
It used base 1ead920 plus this patch, ngspice-47+ from corpus e8285746c73a,
and the unchanged issue 40 engine
037406562cd2518633d4d05debceb3c5cf3252f16a52f4b68d9bbdde318b4cce.
The coordinator recorded matching source/runtime provenance. The separate
exploratory 272-path run was stopped after the focused replay completed. Its
last checkpoint had 73 refusals and 199 unattempted paths, without a final
provenance check.

| Focused scope | Baseline | After |
|---|---:|---:|
| Original requested executions | 0 | 0 |
| Native agreements | 0 | 0 |
| Decks reaching model diagnostics beyond the physical-directive gate | 0 | 12 |

Remaining first diagnostics were 62 physical directives, nine MOS profiles,
two diode parameters and one BJT profile. The diode fixtures explicitly carry
RS=0. Two owned linear benchmarks carry NUMDGT=15. These are separate follow-ups.
Nondefault operating temperature, device tolerances, continuation options, UIC,
.CSPARAM, netlist conditionals, RF .SP and audio remain open.

The focused replay took 64.35 seconds. Its summary SHA-256 is
6aca4aa61b0d40a86a63316f3b1960d9cb284bcd15568877a8a622a535707572 and
inventory SHA-256 is 581ad4b75d16a11542c05fa8faf3e5b77967034ceb209b9e396198281c03039.
Use the existing Run-RequestedCorpus.cs documented invocation with
affected-inventory.json and a fresh output directory.

## Verification

Frontend tests with the requested engine and native prerequisites configured
passed 569 with 24 opt-in skips. Syntax tests passed 471 with one filesystem
symlink skip. StrictDoc 0.30.4 parsed 52 documents with 419 unique IDs and 387
resolved parents. Negative cases cover unsupported mixed options and malformed
width. The FsCheck option property requested 120 cases with seed
2381,20261107. No separate input fuzzer was run.

Coverlet instrumented managed assemblies at 77.37% lines and 61.57% branches.
DeclarationExtraction measured 97.12% lines and 83.00% branches.
OperatingPointSelection measured 100% lines and branches. OriginalProjection
measured 100% lines and 64.54% branches. No Rust or native ngspice coverage
was measured. Stryker.NET 5.0.0 killed all four executed selected mutants in
OperatingPointSelection. Five candidates in the file had compile errors
and five were filtered. Other files and mutations are outside this selected
score.

## Follow-up checkpoint

Admit literal NUMDGT from 1 to 17 as output precision and explicit diode RS=0
as the existing zero-series-resistance topology. Positive, negative, duplicate
and expression-valued RS remain refused. The native diode projection preserves
the original model parameters.

The 77-path follow-up added the NUMDGT candidates to the original focused
inventory. All input and expanded hashes matched the historical census.
Requested execution and native agreement each improved from zero to three.
The matching decks are compat/benchmarks/divider.cir (OP),
compat/benchmarks/rc-ac.cir (AC), and
compat/ngspice/qualification-diode-constant-charge.cir (AC). The other 74
paths were refused in preparation. The diode charge transient now reaches an
explicit UIC refusal. The RC transient still needs its integration-method
option and UIC state contract. The RLC decay also needs the inductor IC=0
header. No 928-path total is inferred.

The final report has valid provenance and took 147.41 seconds. It tested
c23e438 plus the follow-up patch with the same native executables and corpus
revision. Artifact paths under the same E: root are
followup-inventory-v2.json and followup-final/summary.json, with SHA-256
4f2728761365f187803c70c8f2ff0e99a92eaf2653984f9afe481b98ba95b9da and
36a448afd662de5d4ea9415384ffa76147b2e7540d379f54d2610aad2c0b72d9.
Use the Run-RequestedCorpus.cs invocation in
docs/requested-corpus-qualification.md with this inventory.

Final frontend tests passed 580 with 24 opt-in skips. Syntax passed 471 with
one symlink skip. StrictDoc parsed 52 documents, 420 unique IDs and 388
resolved parents. The new focused tests detected both deliberate guard changes
in a disposable snapshot: allowing NUMDGT=0 and inverting the RS=0 predicate.
The combined mutation produced five expected assertion failures in eleven
tests. Coverlet measured 77.38% managed line and 61.47% managed branch
coverage, including 97.50%/89.36% for SemiconductorPreparation and
97.27%/82.52% for DeclarationExtraction. No new Rust coverage or standalone
input fuzzing was collected.

The remaining directive families need separate semantic contracts. .CSPARAM
exports constant vectors to control programs, netlist .IF selects physical
cards, .SP needs RF port excitation and S-parameter observables, and .SNDPARAM
belongs to the audio I/O path. The largest unresolved option groups still
require explicit device tolerance, continuation, temperature or integration
policy. The 23 ACCT BYPASS=1 cases additionally depend on CIDER device support.
