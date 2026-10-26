use circuit_simulator::{
    backend::DenseLu,
    compile::compile,
    dc::operating_point,
    error::ErrorCode,
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
fn ngspice_duplicate_transient_directives_are_rejected() {
    let error = import_ngspice_request(
        "title\nV1 out 0 1\nR1 out 0 1k\n.tran 1m 2m\n.tran 1m 3m\n.end\n",
        "tran-duplicate",
    )
    .unwrap_err();
    assert_eq!(error.code, ErrorCode::Unsupported);
}

#[test]
fn ngspice_ac_linear_directive_lowers_to_typed_request() {
    let request = import_ngspice_request(
        "title\nV1 out 0 1\nR1 out 0 1k\n.ac lin 3 10 30\n.end\n",
        "ac-import",
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::Ac { frequencies_hz, .. } => {
            assert_eq!(frequencies_hz, vec![10., 20., 30.])
        }
        _ => panic!("expected AC analysis"),
    }
}

#[test]
fn ngspice_duplicate_ac_directives_are_rejected() {
    let error = import_ngspice_request(
        "title\nV1 out 0 DC 0 AC 1\nR1 out 0 1k\n.ac lin 1 1 1\n.ac lin 1 2 2\n.end\n",
        "ac-duplicate",
    )
    .unwrap_err();
    assert_eq!(error.code, ErrorCode::Unsupported);
}

#[test]
fn ngspice_mixed_transient_and_ac_directives_are_rejected() {
    let error = import_ngspice_request(
        "title\nV1 out 0 DC 0 AC 1\nR1 out 0 1k\n.tran 1m 2m\n.ac lin 1 1 1\n.end\n",
        "mixed-analysis",
    )
    .unwrap_err();
    assert_eq!(error.code, ErrorCode::Unsupported);
}

#[test]
fn ngspice_ac_grid_rejects_nonpositive_start_frequency() {
    let error = import_ngspice_request(
        "title\nV1 out 0 DC 0 AC 1\nR1 out 0 1k\n.ac lin 2 0 1\n.end\n",
        "ac-invalid",
    )
    .unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}

#[test]
fn ngspice_ac_source_amplitude_is_used_for_bounded_ac_import() {
    let request = import_ngspice_request(
        "title\nV1 out 0 DC 5 AC 2\nR1 out 0 1k\n.ac lin 1 10 10\n.end\n",
        "ac-source",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!((result.ac_samples[0].values[0].real - 2.).abs() < 1e-12);
}

#[test]
fn ngspice_implicit_dc_ac_source_form_lowers_to_complex_excitation() {
    let request = import_ngspice_request(
        "title\nV1 out 0 0 AC 2 90\nR1 out 0 1k\n.ac lin 1 10 10\n.end\n",
        "ac-implicit-dc",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!(result.ac_samples[0].values[0].real.abs() < 1e-12);
    assert!((result.ac_samples[0].values[0].imag - 2.).abs() < 1e-12);
}

#[test]
fn ngspice_ac_current_source_amplitude_uses_declared_polarity() {
    let request = import_ngspice_request(
        "title\nI1 out 0 DC 0 AC 2mA\nR1 out 0 1k\n.ac lin 1 10 10\n.end\n",
        "ac-current",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!((result.ac_samples[0].values[0].real + 2.).abs() < 1e-12);
}

#[test]
fn ngspice_ac_source_phase_lowers_to_a_complex_phasor() {
    let request = import_ngspice_request(
        "title\nV1 out 0 DC 0 AC 1 90\nR1 out 0 1k\n.ac lin 1 10 10\n.end\n",
        "ac-phase",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!(result.ac_samples[0].values[0].real.abs() < 1e-12);
    assert!((result.ac_samples[0].values[0].imag - 1.).abs() < 1e-12);
}

#[test]
fn ngspice_ac_current_source_phase_preserves_p_to_n_polarity() {
    let request = import_ngspice_request(
        "title\nI1 out 0 DC 0 AC 2mA 90\nR1 out 0 1k\n.ac lin 1 10 10\n.end\n",
        "ac-current-phase",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!(result.ac_samples[0].values[0].real.abs() < 1e-12);
    assert!((result.ac_samples[0].values[0].imag + 2.).abs() < 1e-12);
}

#[test]
fn ngspice_ac_qualified_resistor_is_used_for_bounded_ac_import() {
    let request = import_ngspice_request(
        "title\nV1 in 0 DC 1 AC 1\nR1 in out 5k\nR2 out 0 5k ac=15k\n.ac lin 1 10 10\n.end\n",
        "ac-resistor",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!((result.ac_samples[0].values[1].real - 0.75).abs() < 1e-12);
}

#[test]
fn ngspice_ac_qualified_capacitor_is_used_for_bounded_ac_import() {
    let request = import_ngspice_request(
        "title\nV1 in 0 DC 1 AC 1\nR1 in out 1k\nC1 out 0 1u ac=2u\n.ac lin 1 159.15494309189535 159.15494309189535\n.end\n",
        "ac-capacitor",
    )
    .unwrap();
    let value = &circuit_simulator::api::run(&request).unwrap().ac_samples[0].values[1];
    assert!((value.real - 0.2).abs() < 1e-12);
    assert!((value.imag + 0.4).abs() < 1e-12);
}

#[test]
fn ngspice_ac_qualified_inductor_is_used_for_bounded_ac_import() {
    let request = import_ngspice_request(
        "title\nV1 in 0 DC 0 AC 1\nR1 in out 1\nL1 out 0 1m ac=2m\n.ac lin 1 159.15494309189535 159.15494309189535\n.end\n",
        "ac-inductor",
    )
    .unwrap();
    let value = &circuit_simulator::api::run(&request).unwrap().ac_samples[0].values[1];
    assert!((value.real - 0.8).abs() < 1e-12);
    assert!((value.imag - 0.4).abs() < 1e-12);
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
    assert!(
        result
            .ac_samples
            .iter()
            .all(|sample| (sample.values[1].real - 0.75).abs() < 1e-12)
    );
}

#[test]
fn ngspice_decade_ac_sweeps_lower_to_logarithmic_grids() {
    let request = import_ngspice_request(
        "title\nV1 out 0 DC 0 AC 1\nR1 out 0 1k\n.ac dec 10 1 10\n.end\n",
        "ac-dec",
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::Ac { frequencies_hz, .. } => {
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
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::Ac { frequencies_hz, .. } => {
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
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::Ac { frequencies_hz, .. } => {
            assert_eq!(frequencies_hz.len(), 3);
            assert!((frequencies_hz[1] - 2_f64.sqrt()).abs() < 1e-12);
        }
        _ => panic!("expected AC analysis"),
    }
}

#[test]
fn ngspice_g_card_imports_with_controlled_current_polarity() {
    let circuit =
        parse_ngspice_basic("title\nV1 in 0 2\nG1 out 0 in 0 0.5\nR1 out 0 1\n.end\n").unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[1] + 1.).abs() < 1e-12);
}

#[test]
fn ngspice_g_card_participates_in_bounded_ac_analysis() {
    let request = import_ngspice_request(
        "title\nV1 in 0 DC 0 AC 1\nG1 out 0 in 0 2m\nR1 out 0 1k\n.ac lin 1 10 10\n.end\n",
        "g-ac",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!((result.ac_samples[0].values[1].real + 2.).abs() < 1e-12);
}

#[test]
fn ngspice_g_card_rejects_missing_control_terminal() {
    let error = parse_ngspice_basic("title\nG1 out 0 in 1m\n.end\n").unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}

#[test]
fn ngspice_e_card_rejects_missing_control_terminal() {
    let error = parse_ngspice_basic("title\nE1 out 0 in 2\n.end\n").unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}

#[test]
fn ngspice_e_card_imports_with_controlled_voltage_polarity() {
    let circuit =
        parse_ngspice_basic("title\nV1 in 0 2\nE1 out 0 in 0 -3\nR1 out 0 1\n.end\n").unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[1] + 6.).abs() < 1e-12);
}

#[test]
fn ngspice_f_card_imports_with_named_branch_current_polarity() {
    let circuit = parse_ngspice_basic(
        "title\nV1 in 0 2\nRCONTROL in 0 1\nF1 out 0 V1 0.5\nROUT out 0 1\n.end\n",
    )
    .unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[1] - 1.).abs() < 1e-12);
}

#[test]
fn ngspice_h_card_imports_with_named_branch_current_polarity() {
    let circuit = parse_ngspice_basic(
        "title\nV1 in 0 2\nRCONTROL in 0 1\nH1 out 0 V1 3\nROUT out 0 1\n.end\n",
    )
    .unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[1] + 6.).abs() < 1e-12);
}

#[test]
fn ngspice_f_card_participates_in_bounded_ac_analysis() {
    let request = import_ngspice_request(
        "title\nV1 in 0 DC 0 AC 1\nRCONTROL in 0 1\nF1 out 0 V1 0.5\nROUT out 0 1\n.ac lin 1 10 10\n.end\n",
        "f-ac",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!((result.ac_samples[0].values[1].real - 0.5).abs() < 1e-12);
}

#[test]
fn ngspice_h_card_participates_in_bounded_ac_analysis() {
    let request = import_ngspice_request(
        "title\nV1 in 0 DC 0 AC 1\nRCONTROL in 0 1\nH1 out 0 V1 3\nROUT out 0 1\n.ac lin 1 10 10\n.end\n",
        "h-ac",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!((result.ac_samples[0].values[1].real + 3.).abs() < 1e-12);
}

#[test]
fn ngspice_e_card_participates_in_bounded_ac_analysis() {
    let request = import_ngspice_request(
        "title\nV1 in 0 DC 0 AC 1\nE1 out 0 in 0 -3\nR1 out 0 1k\n.ac lin 1 10 10\n.end\n",
        "e-ac",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!((result.ac_samples[0].values[1].real + 3.).abs() < 1e-12);
}

#[test]
fn ngspice_pulse_source_lowers_to_transient_override() {
    let request = import_ngspice_request(
        "title\nV1 out 0 pulse 0 2 0 1m 1m 2m 6m\nR1 out 0 1k\n.tran 1m 6m\n.end\n",
        "pulse-import",
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert_eq!(options.sources.len(), 1);
            assert_eq!(options.sources[0].id, "V1");
        }
        _ => panic!("expected configured transient"),
    }
}

#[test]
fn ngspice_compact_pulse_source_lowers_to_transient_override() {
    let request = import_ngspice_request(
        "title\nV1 out 0 PULSE(0,2,0,1m,1m,2m,6m)\nR1 out 0 1k\n.tran 1m 6m\n.end\n",
        "pulse-compact",
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert_eq!(options.sources.len(), 1);
            assert_eq!(options.sources[0].waveform.at(2e-3).unwrap(), 2.);
        }
        _ => panic!("expected configured transient"),
    }
}

#[test]
fn ngspice_compact_pulse_current_source_preserves_polarity() {
    let request = import_ngspice_request(
        "title\nI1 out 0 PULSE(0,2m,0,0,0,1m,2m)\nR1 out 0 1k\n.tran 0.5m 0.5m\n.end\n",
        "pulse-current",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!((result.samples.last().unwrap().values[0] + 2.).abs() < 1e-12);
}

#[test]
fn ngspice_pulse_rejects_phase_duration_beyond_period_when_executed() {
    let request = import_ngspice_request(
        "title\nV1 out 0 pulse 0 1 0 1m 1m 2m 3m\nR1 out 0 1k\n.tran 1m 3m\n.end\n",
        "pulse-invalid",
    )
    .unwrap();
    let error = circuit_simulator::api::run(&request).unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}

#[test]
fn ngspice_compact_sine_source_lowers_to_transient_override() {
    let request = import_ngspice_request(
        "title\nV1 out 0 SIN(1,2,1k)\nR1 out 0 1k\n.tran 1m 2m\n.end\n",
        "sine-import",
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert_eq!(options.sources.len(), 1);
            assert!((options.sources[0].waveform.at(0.).unwrap() - 1.).abs() < 1e-12);
        }
        _ => panic!("expected configured transient"),
    }
}

#[test]
fn ngspice_whitespace_sine_source_lowers_to_transient_override() {
    let request = import_ngspice_request(
        "title\nV1 out 0 SIN 1 2 1k\nR1 out 0 1k\n.tran 1m 2m\n.end\n",
        "sine-spaced-import",
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert!((options.sources[0].waveform.at(0.).unwrap() - 1.).abs() < 1e-12);
        }
        _ => panic!("expected configured transient analysis"),
    }
}

#[test]
fn ngspice_delayed_damped_sine_lowers_with_delay_breakpoint_and_phase() {
    let request = import_ngspice_request(
        "title\nV1 out 0 SIN 0 2 1k 1m 100 90\nR1 out 0 1k\n.tran 1m 2m\n.end\n",
        "sine-damped-import",
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert_eq!(options.sources[0].waveform.breakpoints(), vec![1e-3]);
            assert_eq!(options.sources[0].waveform.at(0.).unwrap(), 0.);
            assert!((options.sources[0].waveform.at(1e-3).unwrap() - 2.).abs() < 1e-12);
        }
        _ => panic!("expected configured transient analysis"),
    }
}

#[test]
fn ngspice_compact_sine_current_source_preserves_polarity() {
    let request = import_ngspice_request(
        "title\nI1 out 0 SIN(0,2m,1k)\nR1 out 0 1k\n.tran 0.25m 0.25m\n.end\n",
        "sine-current",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!((result.samples.last().unwrap().values[0] + 2.).abs() < 1e-12);
}

#[test]
fn ngspice_compact_sine_rejects_nonpositive_frequency_when_executed() {
    let request = import_ngspice_request(
        "title\nV1 out 0 SIN(0,1,0)\nR1 out 0 1k\n.tran 1m 2m\n.end\n",
        "sine-invalid",
    )
    .unwrap();
    let error = circuit_simulator::api::run(&request).unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}

#[test]
fn ngspice_compact_pwl_source_lowers_to_transient_override() {
    let request = import_ngspice_request(
        "title\nV1 out 0 PWL(0,0,1m,2)\nR1 out 0 1k\n.tran 1m 2m\n.end\n",
        "pwl-import",
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert!((options.sources[0].waveform.at(0.5e-3).unwrap() - 1.).abs() < 1e-12);
        }
        _ => panic!("expected configured transient"),
    }
}

#[test]
fn ngspice_whitespace_pwl_source_lowers_to_transient_override() {
    let request = import_ngspice_request(
        "title\nV1 out 0 PWL 0 0 1m 2\nR1 out 0 1k\n.tran 1m 2m\n.end\n",
        "pwl-spaced-import",
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert!((options.sources[0].waveform.at(0.5e-3).unwrap() - 1.).abs() < 1e-12);
            assert_eq!(options.sources[0].waveform.breakpoints(), vec![0., 1e-3]);
        }
        _ => panic!("expected configured transient analysis"),
    }
}

#[test]
fn ngspice_compact_pwl_current_source_preserves_polarity() {
    let request = import_ngspice_request(
        "title\nI1 out 0 PWL(0,0,1m,2m)\nR1 out 0 1k\n.tran 1m 1m\n.end\n",
        "pwl-current",
    )
    .unwrap();
    let result = circuit_simulator::api::run(&request).unwrap();
    assert!((result.samples.last().unwrap().values[0] + 2.).abs() < 1e-12);
}

#[test]
fn ngspice_compact_pwl_rejects_nonmonotonic_times_when_executed() {
    let request = import_ngspice_request(
        "title\nV1 out 0 PWL(1m,0,0,2)\nR1 out 0 1k\n.tran 1m 2m\n.end\n",
        "pwl-invalid",
    )
    .unwrap();
    let error = circuit_simulator::api::run(&request).unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}

#[test]
fn pulse_waveform_has_exact_plateaus_and_rejects_invalid_periods() {
    let pulse = Waveform::Pulse {
        low: 0.,
        high: 2.,
        delay_s: 1.,
        rise_s: 1.,
        fall_s: 1.,
        width_s: 2.,
        period_s: 6.,
    };
    assert_eq!(pulse.at(0.).unwrap(), 0.);
    assert_eq!(pulse.at(2.).unwrap(), 2.);
    assert_eq!(pulse.at(4.).unwrap(), 2.);
    assert_eq!(pulse.at(5.).unwrap(), 0.);
    assert!(
        Waveform::Pulse {
            low: 0.,
            high: 2.,
            delay_s: 1.,
            rise_s: 1.,
            fall_s: 1.,
            width_s: 2.,
            period_s: 0.,
        }
        .validate()
        .is_err()
    );
}

#[test]
fn exponential_waveform_has_spice_rise_and_return_semantics() {
    let waveform = Waveform::Exponential {
        initial: 0.,
        pulsed: 2.,
        rise_delay_s: 1.,
        rise_tau_s: 1.,
        fall_delay_s: 4.,
        fall_tau_s: 1.,
    };
    assert_eq!(waveform.at(0.).unwrap(), 0.);
    assert!((waveform.at(2.).unwrap() - 2. * (1. - (-1_f64).exp())).abs() < 1e-12);
    assert!(waveform.at(5.).unwrap() < waveform.at(4.).unwrap());
    assert!(waveform.at(20.).unwrap().abs() < 1e-6);
    assert_eq!(waveform.breakpoints(), vec![1., 4.]);
    assert!(
        Waveform::Exponential {
            initial: 0.,
            pulsed: 1.,
            rise_delay_s: 2.,
            rise_tau_s: 1.,
            fall_delay_s: 1.,
            fall_tau_s: 1.,
        }
        .validate()
        .is_err()
    );
}

#[test]
fn ngspice_compact_exponential_source_lowers_to_transient_override() {
    let request = import_ngspice_request(
        "title\nV1 out 0 EXP(0,2,1m,1m,4m,1m)\nR1 out 0 1k\n.tran 1m 6m\n.end\n",
        "exp-import",
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert_eq!(options.sources.len(), 1);
            assert!(
                (options.sources[0].waveform.at(2e-3).unwrap() - 2. * (1. - (-1_f64).exp())).abs()
                    < 1e-12
            );
        }
        _ => panic!("expected configured transient analysis"),
    }
}

#[test]
fn ngspice_whitespace_exponential_source_lowers_to_transient_override() {
    let request = import_ngspice_request(
        "title\nV1 out 0 EXP 0 2 1m 1m 4m 1m\nR1 out 0 1k\n.tran 1m 6m\n.end\n",
        "exp-spaced-import",
    )
    .unwrap();
    match request.analysis {
        circuit_simulator::api::Analysis::TransientConfigured { options } => {
            assert!(
                (options.sources[0].waveform.at(2e-3).unwrap() - 2. * (1. - (-1_f64).exp())).abs()
                    < 1e-12
            );
        }
        _ => panic!("expected configured transient analysis"),
    }
}

#[test]
fn ngspice_compact_exponential_rejects_invalid_fall_order_when_executed() {
    let request = import_ngspice_request(
        "title\nV1 out 0 EXP(0,2,4m,1m,1m,1m)\nR1 out 0 1k\n.tran 1m 6m\n.end\n",
        "exp-invalid",
    )
    .unwrap();
    let error = circuit_simulator::api::run(&request).unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}

/// Compact DC assignment and inline comments are common deck conveniences.
/// Waveform tails deliberately remain outside this importer rather than being
/// accepted with incorrect constant-source semantics.
#[test]
fn ngspice_compact_dc_and_inline_comment_import() {
    let circuit =
        parse_ngspice_basic("title\nV1 out 0 DC=2 ; source bias\nR1 out 0 4k ; load\n.op\n.end\n")
            .unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[0] - 2.).abs() < 1e-12);
    assert!((values[1] + 5e-4).abs() < 1e-12);
}

#[test]
fn netlist_ground_aliases_normalize_to_the_reference_node() {
    let circuit = parse_ngspice_basic("title\nV1 out gnd 2\nR1 out ground 1k\n.end\n").unwrap();
    assert_eq!(circuit.nodes, vec!["0", "out"]);
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[0] - 2.).abs() < 1e-12);
}

#[test]
fn netlist_identifiers_have_documented_stable_case_normalization() {
    let circuit = parse_ngspice_basic("title\nvBias Nout 0 1\nrLoad nOUT 0 1k\n.end\n").unwrap();
    assert_eq!(circuit.nodes, vec!["0", "nout"]);
    assert_eq!(circuit.elements[0].id(), "VBIAS");
    assert_eq!(circuit.elements[1].id(), "RLOAD");
}

#[test]
fn bounded_ngspice_lowering_is_reproducible_for_the_same_deck() {
    let deck =
        "title\nV1 out gnd DC 0 AC 1 90\nR1 out 0 1k\nC1 out 0 1u ac=2u\n.ac dec 10 1 10\n.end\n";
    let first = import_ngspice_request(deck, "deterministic-import").unwrap();
    let second = import_ngspice_request(deck, "deterministic-import").unwrap();
    assert_eq!(
        serde_json::to_vec(&first).unwrap(),
        serde_json::to_vec(&second).unwrap()
    );
    assert_eq!(
        circuit_simulator::api::run(&first).unwrap().request_sha256,
        circuit_simulator::api::run(&second).unwrap().request_sha256
    );
}

#[test]
fn ngspice_bounded_diode_model_lowers_named_diode_parameters() {
    let circuit = parse_ngspice_basic(
        "title\n.model DMOD D (IS=2e-14 N=2)\nV1 in 0 0.5\nR1 in out 1k\nD1 out 0 DMOD\n.end\n",
    )
    .unwrap();
    let diode = circuit
        .elements
        .iter()
        .find_map(|element| match element {
            circuit_simulator::circuit::Element::Diode {
                saturation_amps,
                thermal_volts,
                ..
            } => Some((*saturation_amps, *thermal_volts)),
            _ => None,
        })
        .unwrap();
    assert_eq!(diode.0, 2e-14);
    assert!((diode.1 - 2. * 8.617_333_262_145e-5 * 300.).abs() < 1e-15);
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!(values.iter().all(|value| value.is_finite()));
}

#[test]
fn ngspice_bounded_diode_model_accepts_compact_comma_parameters() {
    let circuit = parse_ngspice_basic(
        "title\n.model DMOD D (IS=3e-14,N=1.5)\nD1 out 0 DMOD\nR1 out 0 1k\n.end\n",
    )
    .unwrap();
    let diode = circuit
        .elements
        .iter()
        .find_map(|element| match element {
            circuit_simulator::circuit::Element::Diode {
                saturation_amps,
                thermal_volts,
                ..
            } => Some((*saturation_amps, *thermal_volts)),
            _ => None,
        })
        .unwrap();
    assert_eq!(diode.0, 3e-14);
    assert!((diode.1 - 1.5 * 8.617_333_262_145e-5 * 300.).abs() < 1e-15);
}

#[test]
fn ngspice_bounded_diode_model_defaults_and_name_lookup_are_case_insensitive() {
    let circuit =
        parse_ngspice_basic("title\n.model dMoD d\nD1 out 0 DMOD\nR1 out 0 1k\n.end\n").unwrap();
    let diode = circuit
        .elements
        .iter()
        .find_map(|element| match element {
            circuit_simulator::circuit::Element::Diode {
                saturation_amps,
                thermal_volts,
                ..
            } => Some((*saturation_amps, *thermal_volts)),
            _ => None,
        })
        .unwrap();
    assert_eq!(diode.0, 1e-14);
    assert!((diode.1 - 8.617_333_262_145e-5 * 300.).abs() < 1e-15);
}

#[test]
fn ngspice_bounded_temperature_scales_named_diode_thermal_voltage() {
    let circuit = parse_ngspice_basic(
        "title\n.temp 126.85\n.model DMOD D (IS=1e-14 N=1)\nD1 out 0 DMOD\nR1 out 0 1k\n.end\n",
    )
    .unwrap();
    let thermal = circuit
        .elements
        .iter()
        .find_map(|element| match element {
            circuit_simulator::circuit::Element::Diode { thermal_volts, .. } => {
                Some(*thermal_volts)
            }
            _ => None,
        })
        .unwrap();
    assert!((thermal - 8.617_333_262_145e-5 * 400.).abs() < 1e-15);
    assert!(parse_ngspice_basic("title\n.temp 20\n.temp 30\n.end\n").is_err());
}

#[test]
fn ngspice_bounded_diode_model_rejects_unknown_parameters_and_names() {
    assert!(parse_ngspice_basic("title\n.model DMOD D (RS=1)\n.end\n").is_err());
    assert!(parse_ngspice_basic("title\nD1 out 0 MISSING\nR1 out 0 1k\n.end\n").is_err());
}

#[test]
fn ngspice_continuation_record_is_folded_before_bounded_lowering() {
    let circuit =
        parse_ngspice_basic("title\nV1 out 0 DC\n+ 2V ; value continuation\nR1 out 0 1k\n.end\n")
            .unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[0] - 2.).abs() < 1e-12);
}

#[test]
fn ngspice_orphan_continuation_is_rejected() {
    let error = parse_ngspice_basic("title\n+ 2V\n.end\n").unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}

#[test]
fn ngspice_bounded_output_directives_are_validated_before_import_only_lowering() {
    let circuit = parse_ngspice_basic(
        "title\nV1 out 0 1\nR1 out 0 1k\n.print tran v(out) i(V1)\n.plot ac v(out)\n.probe v(out)\n.end\n",
    )
    .unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[0] - 1.).abs() < 1e-12);
    assert!(parse_ngspice_basic("title\n.print ac\n.end\n").is_err());
    assert!(parse_ngspice_basic("title\n.probe\n.end\n").is_err());
}

#[test]
fn ngspice_noacct_option_is_explicitly_accepted_and_other_options_rejected() {
    assert!(parse_ngspice_basic("title\n.options noacct\nV1 out 0 1\nR1 out 0 1k\n.end\n").is_ok());
    assert!(parse_ngspice_basic("title\n.options reltol=1e-3\n.end\n").is_err());
}

#[test]
fn ngspice_explicit_dc_source_units_are_normalized() {
    let circuit = parse_ngspice_basic("title\nV1 out 0 DC 2V\nR1 out 0 4k\n.end\n").unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[0] - 2.).abs() < 1e-12);
}

#[test]
fn ngspice_explicit_dc_current_units_are_normalized() {
    let circuit = parse_ngspice_basic("title\nI1 out 0 DC 2mA\nR1 out 0 1k\n.end\n").unwrap();
    let values = operating_point(&compile(&circuit).unwrap(), &DenseLu).unwrap();
    assert!((values[0] + 2.).abs() < 1e-12);
}

#[test]
fn ngspice_waveform_tail_is_not_silently_imported() {
    let error =
        parse_ngspice_basic("title\nV1 out 0 DC=2 SIN(0 1 1k)\nR1 out 0 4k\n.end\n").unwrap_err();
    assert_eq!(error.code, ErrorCode::InvalidInput);
}
