use circuit_simulator::{
    api::Request,
    netlist::import_ngspice_request,
    sparse::CscMatrix,
    waveform::{Point, Waveform},
};
#[test]
fn seeded_sparse_layout_fuzz_is_bounded_and_panic_free() {
    let mut state = 0x535041525345_u64;
    for _ in 0..2000 {
        state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
        let n = (state as usize) % 12;
        let length = ((state >> 12) as usize) % 32;
        let pointers: Vec<_> = (0..n + 1)
            .map(|i| ((state >> (i % 16)) as usize) % 40)
            .collect();
        let rows: Vec<_> = (0..length).map(|i| i % 16).collect();
        let values = vec![1.; length];
        assert!(
            std::panic::catch_unwind(|| {
                let _ = CscMatrix::new(n, pointers, rows, values);
            })
            .is_ok()
        );
    }
}
#[test]
fn waveform_and_new_request_mutation_fuzz_do_not_panic() {
    for i in 0..256 {
        let waveform = Waveform::Pwl {
            points: vec![
                Point {
                    time_s: i as f64,
                    value: 1.,
                },
                Point {
                    time_s: (255 - i) as f64,
                    value: -1.,
                },
            ],
        };
        assert!(
            std::panic::catch_unwind(|| {
                let _ = waveform.at(1.);
            })
            .is_ok()
        );
    }
    let original = include_bytes!("../examples/adaptive-rc.json");
    for i in 0..original.len() {
        let mut bytes = original.to_vec();
        bytes[i] ^= 0x7f;
        assert!(
            std::panic::catch_unwind(|| {
                let _ = serde_json::from_slice::<Request>(&bytes);
            })
            .is_ok()
        );
    }
}

/// Fixed mutations target the bounded deck lowerer, including waveform forms
/// that have nested punctuation and are therefore poorly covered by JSON fuzzing.
#[test]
fn seeded_ngspice_waveform_deck_mutations_do_not_panic() {
    let seeds = [
        "title\nV1 out 0 PULSE(0,2,0,1m,1m,2m,6m)\nR1 out 0 1k\n.tran 1m 6m\n.end\n",
        "title\nI1 out 0 SIN 0 2m 1k\nR1 out 0 1k\n.tran 1m 2m\n.end\n",
        "title\nV1 out 0 PWL 0 0 1m 2\nR1 out 0 1k\n.tran 1m 2m\n.end\n",
        "title\nV1 out 0 EXP(0,2,1m,1m,4m,1m)\nR1 out 0 1k\n.tran 1m 6m\n.end\n",
        "title\nV1 out 0 DC 0 AC 1 90\nR1 out 0 1k\n.ac lin 1 10 10\n.end\n",
        "title\n.temp 26.85\n.model DMOD D (IS=1e-14,N=1.5)\nV1 in 0 0.5\nR1 in out 1k\nD1 out 0 DMOD\n.end\n",
    ];
    let mut state = 0x4e475350494345_u64;
    for seed in seeds {
        for _ in 0..256 {
            state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
            let mut bytes = seed.as_bytes().to_vec();
            let index = (state as usize) % bytes.len();
            bytes[index] ^= (state >> 32) as u8;
            let text = String::from_utf8_lossy(&bytes);
            assert!(std::panic::catch_unwind(|| {
                let _ = import_ngspice_request(&text, "seeded-waveform-fuzz");
            }).is_ok());
        }
    }
}
