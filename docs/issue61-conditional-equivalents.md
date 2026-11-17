# Issue 61: conditional equivalent mutations

These dispositions bind to the exact cargo-mutants native diffs in
`evidence/issue61-conditional-equivalents.json`; they do not change raw survivor
outcomes into kills. This is a source-based argument, not a formal solver proof.

* `netlist.rs:362:30`, subtraction to addition in the PWL token parity guard:
  the preceding short-circuit condition requires at least six tokens. Thus
  subtracting four cannot underflow. A `Vec<&str>` cannot contain enough
  nonzero-sized references to approach `usize::MAX - 4`, so adding four cannot
  overflow on the qualified Windows x64 representation. The two counts differ
  by eight and have identical parity. Both take exactly the same branch. This
  concerns the token parity predicate, not whether all PWL forms execute.
* `newton.rs:112:45`, the **first** multiplication in
  `sign * polarity * gm`: the nested loops supply only literal +1 and -1 for
  both factors. For all four pairs, multiplication and division of the first
  two operands produce the same exactly representable signed unit. The final
  multiplication by gm and matrix accumulation are unchanged. The current
  counterpart resides in `stamp_four_terminal`. The second multiplication
  (division by gm) is a different, non-equivalent candidate.

Assumptions: ordinary Rust integer/IEEE floating-point operations, the admitted
container representation and unchanged literal sign loops. Reopen these
reviews if those assumptions or the expressions change. No performance,
full-machine, GPU, or universal SPICE qualification is inferred. The stop-time
audit connective is **not** in this list: its earlier neighboring-predicate
prototype was rejected, and the actual mutant now has an executing regression.

The later full replay found a third conditional equivalent:
`waveform.rs:359:36` changes the exponential **fall-delay** guard from `<` to
`<=`. The only newly selected case is time exactly equal to the finite fall
delay. Validated fall_tau_s is positive finite. Thus time-minus-delay is signed
zero, division stays signed zero, exp of either signed zero is exactly1, and
1-minus-1 is the same positive zero as the other branch. All downstream rise
and amplitude expressions remain identical, including their existing failure
behavior. This argument concerns the actual fall guard, not the rise guard,
phase, pulse fall endpoint, or general exponential accuracy. It assumes the
qualified ordinary IEEE operations/exp(0)=1; changes to evaluation or domains
reopen the disposition. It remains a raw survivor and is not a test kill.
