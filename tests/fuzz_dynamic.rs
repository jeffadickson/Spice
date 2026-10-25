use circuit_simulator::{
    api::Request,
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
