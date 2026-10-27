//! Independent frequency-domain equations for signed flux and controlled sources.
use circuit_simulator::{
    ac::ac_with_source_phasors, api::AcSourcePhasor, backend::DenseLu, backend_faer::FaerSparseLu,
    compile::compile, linalg::LinearSolver, netlist::parse,
};
use num_complex::Complex64;

fn complex(value: &circuit_simulator::ac::ComplexValue) -> Complex64 {
    Complex64::new(value.real, value.imag)
}

#[test]
fn series_rlc_voltage_and_signed_branch_currents_match_impedance() {
    let plan = compile(
        &parse("title\nV1 in 0 1\nR1 in mid 100\nL1 mid out 10m\nC1 out 0 1u\n.end").unwrap(),
    )
    .unwrap();
    let source = AcSourcePhasor {
        id: "V1".into(),
        real: 1.,
        imag: 0.5,
    };
    let frequencies = [
        100.,
        1. / (std::f64::consts::TAU * (0.01_f64 * 1e-6).sqrt()),
        10_000.,
    ];
    for provider in [&DenseLu as &dyn LinearSolver, &FaerSparseLu] {
        let samples =
            ac_with_source_phasors(&plan, &frequencies, std::slice::from_ref(&source), provider)
                .unwrap();
        for sample in samples {
            let omega = std::f64::consts::TAU * sample.frequency_hz;
            let current =
                Complex64::new(1., 0.5) / Complex64::new(100., omega * 0.01 - 1. / (omega * 1e-6));
            let capacitor = current / Complex64::new(0., omega * 1e-6);
            assert!((complex(&sample.values[2]) - capacitor).norm() < 1e-11);
            assert!(
                (complex(&sample.values[plan.branch_index("V1").unwrap()]) + current).norm()
                    < 1e-12
            );
            assert!(
                (complex(&sample.values[plan.branch_index("L1").unwrap()]) - current).norm()
                    < 1e-12
            );
        }
    }
}

#[test]
fn all_four_controlled_source_families_preserve_complex_gain_and_polarity() {
    for (device, numerator, reactive) in [
        ("E1 out 0 in 0 -2.5", -2.5, false),
        ("G1 out 0 in 0 0.002", -2., true),
        ("F1 out 0 V1 3", 3., true),
        ("H1 out 0 V1 1000", -1., false),
    ] {
        let text = format!(
            "title\nV1 in 0 1\nRIN in 0 1k\n{device}\nRLOAD out 0 1k\nCLOAD out 0 1u\n.end"
        );
        let plan = compile(&parse(&text).unwrap()).unwrap();
        let source = AcSourcePhasor {
            id: "V1".into(),
            real: -0.75,
            imag: 1.25,
        };
        for provider in [&DenseLu as &dyn LinearSolver, &FaerSparseLu] {
            for sample in ac_with_source_phasors(
                &plan,
                &[10., 1000.],
                std::slice::from_ref(&source),
                provider,
            )
            .unwrap()
            {
                let denominator = if reactive {
                    Complex64::new(1., std::f64::consts::TAU * sample.frequency_hz * 0.001)
                } else {
                    Complex64::new(1., 0.)
                };
                let expected = Complex64::new(-0.75, 1.25) * numerator / denominator;
                assert!(
                    (complex(&sample.values[1]) - expected).norm() < 1e-11,
                    "{device}"
                );
            }
        }
    }
}
