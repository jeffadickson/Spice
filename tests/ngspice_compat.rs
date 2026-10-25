use circuit_simulator::{
    backend::DenseLu,
    compile::compile,
    dc::operating_point,
    netlist::parse_ngspice_basic,
};

/// Source: ngspice tests/resistance/res_simple.cir, revision e8285746c.
/// This exercises title handling, a DC source marker, suffix parsing and ignored
/// output directives; it is not a broad ngspice compatibility claim.
#[test]
fn ngspice_res_simple_fixture_matches_dc_reference() {
    let circuit = parse_ngspice_basic(include_str!("../compat/ngspice/res_simple.cir")).unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[0] - 1.).abs() < 1e-12);
    assert!((values[1] + 1e-4).abs() < 1e-12);
}
