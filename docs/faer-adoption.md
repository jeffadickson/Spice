# faer adapter adoption experiment

Pinned faer 0.24.4 (MIT), default features disabled, std and sparse-linalg enabled. The exact dependency inventory is in Cargo.lock. This is a local numerical adapter experiment, not a completed licensing/distribution/platform audit.

The adapter constructs canonical CSC and uses faer's sparse LU for real/complex systems. nalgebra 0.34.1 remains an independent dense reference. Common tests cover nonsymmetric indefinite matrices, RC AC, RHS reuse and invalid sparse indices.

The circuit compiler still stores bounded dense matrices (256 unknowns maximum). Sparse factorization is real, but no large-circuit memory-scaling or speedup claim is made. No GPU execution is present. Preparation handles own numeric matrices; changing caller values requires a new factorization.

Sources: https://faer.veganb.tw/docs/sparse-linalg/linsolve/ and https://faer.veganb.tw/docs/sparse-linalg/creating-matrix/ . Exact APIs were checked against the pinned crate.
