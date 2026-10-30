//! Nested monotone bisection is independent of MNA/Newton and its Jacobian.
use circuit_simulator::{
    backend::{DenseLu, DenseQr},
    backend_faer::FaerSparseLu,
    circuit::{Circuit, Element},
    compile::compile,
    ids::NodeId,
    linalg::LinearSolver,
    newton::{ContinuationOptions, GminContinuationOptions, NewtonOptions, solve_from},
};

fn ladder(drive: f64, scale: f64) -> Circuit {
    let mut elements = vec![
        Element::Voltage {
            id: "V1".into(),
            p: NodeId(1),
            n: NodeId(0),
            volts: drive,
        },
        Element::Resistor {
            id: "R1".into(),
            p: NodeId(1),
            n: NodeId(2),
            ohms: 1000. * scale,
        },
        Element::Resistor {
            id: "R2".into(),
            p: NodeId(2),
            n: NodeId(3),
            ohms: 3300. * scale,
        },
    ];
    for (node, isat, vt) in [
        (2, 1e-12, 0.025864925786328215),
        (3, 1e-15, 0.03879738867949232),
    ] {
        for reverse in [false, true] {
            elements.push(Element::Diode {
                id: format!("D{node}{reverse}"),
                p: NodeId(if reverse { 0 } else { node }),
                n: NodeId(if reverse { node } else { 0 }),
                saturation_amps: isat,
                thermal_volts: vt,
            });
        }
    }
    Circuit {
        nodes: ["0", "in", "a", "b"].map(String::from).to_vec(),
        elements,
    }
}

fn bisect(mut low: f64, mut high: f64, f: impl Fn(f64) -> f64) -> f64 {
    assert!(
        f(low) <= 0. && f(high) >= 0.,
        "Oracle root must be bracketed"
    );
    for _ in 0..100 {
        let mid = (low + high) * 0.5;
        if f(mid) > 0. { high = mid } else { low = mid }
    }
    (low + high) * 0.5
}

fn oracle(drive: f64, scale: f64) -> (f64, f64) {
    let inner = |a: f64| {
        bisect(a.min(0.), a.max(0.), |b| {
            (b - a) / (3300. * scale) + 2e-15 * (b / 0.03879738867949232).sinh()
        })
    };
    let a = bisect(-2., 2., |a| {
        (a - drive) / (1000. * scale)
            + (a - inner(a)) / (3300. * scale)
            + 2e-12 * (a / 0.025864925786328215).sinh()
    });
    (a, inner(a))
}

fn qualify(drives: &[f64], scales: &[f64]) {
    for &drive in drives {
        for &scale in scales {
            let plan = compile(&ladder(drive, scale)).unwrap();
            let (a, b) = oracle(drive, scale);
            for provider in [&DenseLu as &dyn LinearSolver, &DenseQr, &FaerSparseLu] {
                for gmin in [false, true] {
                    let options = NewtonOptions {
                        max_iterations: 256,
                        current_absolute_tolerance: 1e-11,
                        continuation: (!gmin).then_some(ContinuationOptions {
                            initial_step: 0.125,
                            minimum_step: 1e-6,
                            max_stages: 128,
                        }),
                        gmin_continuation: gmin.then_some(GminContinuationOptions {
                            initial_siemens: 1e-3,
                            minimum_siemens: 1e-4,
                            reduction_factor: 0.1,
                            max_stages: 16,
                        }),
                        ..NewtonOptions::default()
                    };
                    let (x, report) = solve_from(
                        &plan,
                        provider,
                        options,
                        vec![0., -0.3 * drive.signum(), -0.3 * drive.signum(), 0.],
                    )
                    .unwrap_or_else(|e| {
                        panic!(
                            "drive={drive} scale={scale} {} gmin={gmin}: {e:?}",
                            provider.name()
                        )
                    });
                    assert!((x[0] - drive).abs() < 1e-9);
                    assert!((x[1] - a).abs() < 2e-8 && (x[2] - b).abs() < 2e-8);
                    let incoming = (drive - x[1]) / (1000. * scale);
                    let coupling = (x[1] - x[2]) / (3300. * scale);
                    assert!((x[3] + incoming).abs() < 1e-11);
                    assert!(
                        (incoming - coupling - 2e-12 * (x[1] / 0.025864925786328215).sinh()).abs()
                            < 2e-11
                    );
                    assert!((coupling - 2e-15 * (x[2] / 0.03879738867949232).sinh()).abs() < 2e-11);
                    let power = drive * x[3]
                        + (drive - x[1]) * incoming
                        + (x[1] - x[2]) * coupling
                        + x[1] * 2e-12 * (x[1] / 0.025864925786328215).sinh()
                        + x[2] * 2e-15 * (x[2] / 0.03879738867949232).sinh();
                    assert!(
                        power.abs() < 1e-9,
                        "Physical absorbed power balance: {power} W"
                    );
                    assert_eq!(report.gmin_siemens, None);
                    if gmin {
                        let r = report.gmin_continuation.unwrap();
                        assert!(r.final_unregularized);
                        assert_eq!(r.stages.last().unwrap().gmin_siemens, 1e-4);
                    } else {
                        assert_eq!(
                            report
                                .continuation
                                .unwrap()
                                .stages
                                .last()
                                .unwrap()
                                .source_scale,
                            1.
                        );
                    }
                }
            }
        }
    }
}

#[test]
fn coupled_clamps_recover_signed_large_drive_without_retained_gmin() {
    // 5 drives x 3 impedance decades x 3 providers x 2 continuation policies.
    qualify(&[-30., -3., 0.03, 3., 30.], &[0.01, 1., 100.]);
}

proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {cases:32,
        rng_seed:proptest::test_runner::RngSeed::Fixed(2026103005), ..Default::default()})]
    #[test]
    fn seeded_coupled_clamp_property(drive in -30_f64..30., exponent in -2_f64..2.) {
        qualify(&[drive], &[10_f64.powf(exponent)]);
    }
}

#[test]
fn unfinished_gmin_schedule_is_not_reported_as_physical_success() {
    let plan = compile(&ladder(30., 1.)).unwrap();
    let options = NewtonOptions {
        max_iterations: 256,
        current_absolute_tolerance: 1e-11,
        gmin_continuation: Some(GminContinuationOptions {
            initial_siemens: 1e-3,
            minimum_siemens: 1e-4,
            reduction_factor: 0.1,
            max_stages: 1,
        }),
        ..NewtonOptions::default()
    };
    for provider in [&DenseLu as &dyn LinearSolver, &DenseQr, &FaerSparseLu] {
        let error = solve_from(&plan, provider, options, vec![0.; 4]).unwrap_err();
        assert_eq!(
            error.code,
            circuit_simulator::error::ErrorCode::NumericalFailure
        );
        assert!(error.message.contains("stage budget exhausted"));
    }
}
