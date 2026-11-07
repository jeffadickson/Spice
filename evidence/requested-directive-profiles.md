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
