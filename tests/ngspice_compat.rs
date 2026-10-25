use circuit_simulator::{
    backend::DenseLu, compile::compile, dc::operating_point, error::ErrorCode,
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

/// Compact DC assignment and inline comments are common deck conveniences.
/// Waveform tails deliberately remain outside this importer rather than being
/// accepted with incorrect constant-source semantics.
#[test]
fn ngspice_compact_dc_and_inline_comment_import() {
    let circuit = parse_ngspice_basic(
        "title\nV1 out 0 DC=2 ; source bias\nR1 out 0 4k ; load\n.op\n.end\n",
    )
    .unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[0] - 2.).abs() < 1e-12);
    assert!((values[1] + 5e-4).abs() < 1e-12);
}

#[test]
fn ngspice_waveform_tail_is_not_silently_imported() {
    let error = parse_ngspice_basic("title\nV1 out 0 DC=2 SIN(0 1 1k)\nR1 out 0 4k\n.end\n")
        .unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}
