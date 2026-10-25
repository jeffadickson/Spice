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
fn ngspice_ac_linear_directive_lowers_to_typed_request() {
    let request = import_ngspice_request("title\nV1 out 0 1\nR1 out 0 1k\n.ac lin 3 10 30\n.end\n", "ac-import").unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::Ac { frequencies_hz } => assert_eq!(frequencies_hz, vec![10., 20., 30.]),
        _ => panic!("expected AC analysis"),
    }
}

#[test]
fn ngspice_ac_source_amplitude_is_used_for_bounded_ac_import() {
    let request = import_ngspice_request("title\nV1 out 0 DC 5 AC 2\nR1 out 0 1k\n.ac lin 1 10 10\n.end\n", "ac-source").unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!((result.ac_samples[0].values[0].real - 2.).abs() < 1e-12);
}

#[test]
fn ngspice_ac_qualified_resistor_is_used_for_bounded_ac_import() {
    let request = import_ngspice_request("title\nV1 in 0 DC 1 AC 1\nR1 in out 5k\nR2 out 0 5k ac=15k\n.ac lin 1 10 10\n.end\n", "ac-resistor").unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!((result.ac_samples[0].values[1].real - 0.75).abs() < 1e-12);
}

#[test]
fn ngspice_res_partition_fixture_matches_analytic_ac_reference() {
    let request = import_ngspice_request(
        include_str!("../compat/ngspice/res_partition.cir"),
        "ngspice-res-partition",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert_eq!(result.ac_samples.len(), 41);
    assert!(result.ac_samples.iter().all(|sample| (sample.values[1].real - 0.75).abs() < 1e-12));
}

#[test]
fn ngspice_decade_ac_sweeps_lower_to_logarithmic_grids() {
    let request = import_ngspice_request(
        "title\nV1 out 0 DC 0 AC 1\nR1 out 0 1k\n.ac dec 10 1 10\n.end\n",
        "ac-dec",
    ).unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::Ac { frequencies_hz } => {
            assert_eq!(frequencies_hz.len(), 11);
            assert!((frequencies_hz[10] - 10.).abs() < 1e-12);
        }
        _ => panic!("expected AC analysis"),
    }
}

#[test]
fn ngspice_decade_grid_ends_exactly_at_non_integral_stop() {
    let request = import_ngspice_request(
        "title\nV1 out 0 DC 0 AC 1\nR1 out 0 1k\n.ac dec 10 1 15\n.end\n",
        "ac-dec-stop",
    ).unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::Ac { frequencies_hz } => {
            assert_eq!(frequencies_hz.last().copied(), Some(15.));
            assert!(frequencies_hz.iter().all(|frequency| *frequency <= 15.));
        }
        _ => panic!("expected AC analysis"),
    }
}

#[test]
fn ngspice_octave_ac_sweeps_lower_to_logarithmic_grids() {
    let request = import_ngspice_request(
        "title\nV1 out 0 DC 0 AC 1\nR1 out 0 1k\n.ac oct 2 1 2\n.end\n",
        "ac-oct",
    ).unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::Ac { frequencies_hz } => {
            assert_eq!(frequencies_hz.len(), 3);
            assert!((frequencies_hz[1] - 2_f64.sqrt()).abs() < 1e-12);
        }
        _ => panic!("expected AC analysis"),
    }
}

#[test]
fn ngspice_g_card_imports_with_controlled_current_polarity() {
    let circuit = parse_ngspice_basic("title\nV1 in 0 2\nG1 out 0 in 0 0.5\nR1 out 0 1\n.end\n").unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[1] + 1.).abs() < 1e-12);
}

#[test]
fn ngspice_g_card_rejects_missing_control_terminal() {
    let error = parse_ngspice_basic("title\nG1 out 0 in 1m\n.end\n").unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}

#[test]
fn ngspice_e_card_imports_with_controlled_voltage_polarity() {
    let circuit = parse_ngspice_basic("title\nV1 in 0 2\nE1 out 0 in 0 -3\nR1 out 0 1\n.end\n").unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[1] + 6.).abs() < 1e-12);
}

#[test]
fn ngspice_pulse_source_lowers_to_transient_override() {
    let request = import_ngspice_request("title\nV1 out 0 pulse 0 2 0 1m 1m 2m 6m\nR1 out 0 1k\n.tran 1m 6m\n.end\n", "pulse-import").unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert_eq!(options.sources.len(), 1);
            assert_eq!(options.sources[0].id, "V1");
        }
        _ => panic!("expected configured transient"),
    }
}

#[test]
fn ngspice_pulse_rejects_phase_duration_beyond_period_when_executed() {
    let request = import_ngspice_request("title\nV1 out 0 pulse 0 1 0 1m 1m 2m 3m\nR1 out 0 1k\n.tran 1m 3m\n.end\n", "pulse-invalid").unwrap();
    let error = circuit_simulator::api::run(&request).unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}

#[test]
fn ngspice_compact_sine_source_lowers_to_transient_override() {
    let request = import_ngspice_request("title\nV1 out 0 SIN(1,2,1k)\nR1 out 0 1k\n.tran 1m 2m\n.end\n", "sine-import").unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert_eq!(options.sources.len(), 1);
            assert!((options.sources[0].waveform.at(0.).unwrap() - 1.).abs() < 1e-12);
        }
        _ => panic!("expected configured transient"),
    }
}

#[test]
fn ngspice_compact_sine_rejects_nonpositive_frequency_when_executed() {
    let request = import_ngspice_request("title\nV1 out 0 SIN(0,1,0)\nR1 out 0 1k\n.tran 1m 2m\n.end\n", "sine-invalid").unwrap();
    let error = circuit_simulator::api::run(&request).unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}

#[test]
fn ngspice_compact_pwl_source_lowers_to_transient_override() {
    let request = import_ngspice_request("title\nV1 out 0 PWL(0,0,1m,2)\nR1 out 0 1k\n.tran 1m 2m\n.end\n", "pwl-import").unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert!((options.sources[0].waveform.at(0.5e-3).unwrap() - 1.).abs() < 1e-12);
        }
        _ => panic!("expected configured transient"),
    }
}

#[test]
fn ngspice_compact_pwl_rejects_nonmonotonic_times_when_executed() {
    let request = import_ngspice_request("title\nV1 out 0 PWL(1m,0,0,2)\nR1 out 0 1k\n.tran 1m 2m\n.end\n", "pwl-invalid").unwrap();
    let error = circuit_simulator::api::run(&request).unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
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
fn ngspice_explicit_dc_source_units_are_normalized() {
    let circuit = parse_ngspice_basic("title\nV1 out 0 DC 2V\nR1 out 0 4k\n.end\n").unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[0] - 2.).abs() < 1e-12);
}

#[test]
fn ngspice_waveform_tail_is_not_silently_imported() {
    let error = parse_ngspice_basic("title\nV1 out 0 DC=2 SIN(0 1 1k)\nR1 out 0 4k\n.end\n")
        .unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}
