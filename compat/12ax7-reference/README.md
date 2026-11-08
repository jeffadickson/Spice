# 12AX7 reference decks

These two decks expect a local `12ax7.inc` containing only the 12AX7
subcircuit from [Norman Koren's Appendix A3](https://www.normankoren.com/Audio/Tubemodspice_article_2.html).
Its terminals are plate, grid and cathode in that order. Keep the source
model alongside copies of the decks in an artifact directory. The
published model is acquired separately from these fixtures.

Run each deck in ngspice with PSpice compatibility and ASCII RAW output.
The plate fixture sweeps 0–400 V at nine grid biases from 0 to −4 V.
Plate current flowing into the tube is `-i(Vplate)`. The second fixture
measures a 250 V common-cathode stage with a 100 kΩ plate resistor, a
1.5 kΩ cathode resistor, a 1 MΩ grid resistor and a 1 MΩ output load.
Compare `v(out)/v(in)` on the 10 Hz–100 kHz AC grid.

The current frontend resolves the include and parses the original cards.
Original PSpice VALUE source preparation and normalized nonlinear
B-source evaluation remain open under SEMI-TUBE-12AX7-REF-001.
See [the verification record](../../evidence/grammar-cohort-checklist.md)
for the native results and the separate fixed-bias comparison.
