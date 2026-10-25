# Implementation iterations

Each entry corresponds to a local Git commit. Build checkpoints: 10, 20, 30, 40, 50. Packages remain incomplete unless independently evidenced.

1. Establish Rust reference-core package
2. Define structured error taxonomy
3. Introduce typed circuit identifiers
4. Specify validated structured circuit inputs
5. Separate dimensional input validation
6. Define circuit-independent bounded linear algebra
7. Reuse nalgebra LU through a bounded backend adapter
8. Compile passive DC circuits into modified nodal equations
9. Expose operating-point analysis over the solver contract
10. Add analytic DC and invalid-input regression tests
11. Add QR reference adapter without claiming provider independence
12. Expose truthful CPU reference backend capabilities
13. Verify physical residuals independently of backend success
