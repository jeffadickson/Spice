# November 6 Rust survivor follow-up

Source under test branches from overnight main de5d84e with the managed
issue 38 changes. The numerical and assertion implementation is unchanged.

- AC diode terminal current: the new test checks complex KCL at 1 kHz and
  1 MHz on dense LU and CPU sparse LU. The retained mutant divides the
  conductance term by the real voltage instead of multiplying.
- Assertion bound validation: the new test rejects NaN and infinity in
  either bound independently. The retained OR-to-AND mutant fails it.
- LimitedFile flush: the inner writer is an unbuffered std::fs::File.
  File::flush is a no-op on Windows and Unix in the documented implementation,
  so Ok(()) is equivalent on those targets. File::sync_all still runs
  before publication. Rust standard-library reference:
  https://doc.rust-lang.org/src/std/fs.rs.html#1430-1445

Focused baseline: 28 Rust tests passed, including the existing randomized
junction derivative check. One separately invoked ngspice diode oracle
passed operating point, complex AC and transient on both CPU providers.
No new line or branch metrics were collected. Raw output is under
E:/Artifacts/Spice-issue39. No corpus census was rerun.
