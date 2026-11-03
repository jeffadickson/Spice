# Semiconductor evidence metadata correction

The initial diode evidence reproduction table recorded the intended checkout as
working directory for several manifest-path cargo/dotnet calls. Those calls
actually started from the host task directory
`USERPROFILE/Documents/Codex/2026-10-25/continue-the-spice-m1-qualification-work`,
with an absolute manifest/project path. The default compiler label was
`stable-x86_64-pc-windows-msvc`, version1.99.1(58cf8f9c3), rather than the checkout
label1.99.1 selected by the in-checkout mutation command. Both installed
rustc.exe files have SHA256
`ba03e3ba03a73d9fef19ab3659140f94b43615ec1d587fe9befc9aed3bd5aa72`.
There is no checkout .cargo configuration. Recorded source hashes and observed
compiler/SDK versions remain the actual evidence; the command cwd fields are a
reproduction instruction, not an accurate historical cwd for those calls.
New final BJT commands explicitly change into the checked worktree first.

The first BJT Stryker campaign used the existing native executable without first
freezing/hashing that binary separately; it was later rebuilt for the provider
fix. Its managed source/input fingerprints and exact mutation outcomes are
retained, but exact native-binary provenance is limited. Final original-case BJT
qualification used a separately copied SHA-verified executable and source hash
snapshot under the unique bjt-qualified artifact directory. No whole-product or
mutation closure is inferred.
