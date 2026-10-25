use circuit_simulator::{
    backend::DenseLu, compile::compile, dc::operating_point, error::ErrorCode,
    netlist::{import_ngspice_request, parse_ngspice_basic},
    waveform::Waveform,
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

#[test]
fn ngspice_tran_directive_lowers_to_typed_request() {
    let request = import_ngspice_request(
        "title\nV1 out 0 1\nR1 out 0 1k\n.tran 1m 3m\n.end\n",
        "tran-import",
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert_eq!(options.max_step_s, 1e-3);
            assert_eq!(options.stop_s, 3e-3);
        }
        _ => panic!("expected configured transient"),
    }
}

#[test]
fn pulse_waveform_has_exact_plateaus_and_rejects_invalid_periods() {
    let pulse = Waveform::Pulse { low: 0., high: 2., delay_s: 1., rise_s: 1., fall_s: 1., width_s: 2., period_s: 6. };
    assert_eq!(pulse.at(0.).unwrap(), 0.);
    assert_eq!(pulse.at(2.).unwrap(), 2.);
    assert_eq!(pulse.at(4.).unwrap(), 2.);
    assert_eq!(pulse.at(5.).unwrap(), 0.);
    assert!(Waveform::Pulse {
        low: 0.,
        high: 2.,
        delay_s: 1.,
        rise_s: 1.,
        fall_s: 1.,
        width_s: 2.,
        period_s: 0.,
    }
    .validate()
    .is_err());
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
