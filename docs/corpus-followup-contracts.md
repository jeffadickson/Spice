# Corpus follow-up contracts

The observation-preflight mutation rollback was reverified against 41b7655;
three selected mutations were killed. 105 other CompileError statuses are not
kills. See rollback-current-verification.json. No numerical rollback behavior
was changed by that instrumentation checkpoint.

## Current comparisons

Policy v4 adds inductor and E/H controlled-voltage-source MNA currents to V
currents and non-ground node voltages. All currents use the existing p-to-n
sign convention, quantity=current and unit=A; ngspice aliases bind exact device
names. RL complex admittance and E/H current polarity are checked independently
on both CPU providers. Wider terminal-current coverage is still unavailable.
Historical v3 points are a separate burndown series.

## Transient syntax

The bounded form is `.tran step stop [zero-start [max-step]]`. All numbers are
finite; step, stop and max-step must be positive. A zero output start retains
simulation from t=0. Explicit max-step controls the configured integration bound;
the print increment is not a promise of a resampled ngspice output grid. This
remains the existing BE profile. Nonzero output starts, UIC, repeated cards and
AC+TRAN are rejected. OP alongside one selected analysis remains partial corpus
coverage; no multi-analysis result contract is implied.

## Passive model family

The new named family is `.model name R (R=positive-ohms)` (parentheses optional)
with `Rdevice p n name`. Case-insensitive forward references lower to the same
validated primitive resistor, without changing numerical equations. Model names
are unique across families. No default missing resistance is invented.

Geometry, temperature coefficients, noise, overrides and multiplicity remain
unsupported. BJT/MOS/JFET and other semiconductor families are still unsupported;
model cards are never dropped to make their decks pass. This deliberately small
R family does not resolve the dominant semiconductor model barrier.

Primary references: [ngspice manual](https://ngspice.sourceforge.io/docs/ngspice-manual.pdf),
resistor model R default resistance; upstream `src/spicelib/devices/res/restemp.c`
at e8285746c73a8229cf922487c8ccfcf6bea61bce uses RESres for a missing instance
resistance. Existing standard-library collections and the shared scalar parser
are sufficient; no new parser or numerical library is required by these shapes.
