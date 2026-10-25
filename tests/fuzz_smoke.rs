use circuit_simulator::{api::Request, netlist::parse};
/// Reproducible bounded input fuzzing, not coverage-guided libFuzzer qualification.
#[test]
fn seeded_arbitrary_inputs_do_not_panic() {
    let mut seed = 0x46555a5a_u64;
    for _ in 0..2000 {
        seed = seed.wrapping_mul(6364136223846793005).wrapping_add(1);
        let size = (seed as usize) % 512;
        let mut bytes = Vec::with_capacity(size);
        for _ in 0..size {
            seed = seed.wrapping_mul(6364136223846793005).wrapping_add(1);
            bytes.push((seed >> 32) as u8);
        }
        let text = String::from_utf8_lossy(&bytes);
        assert!(
            std::panic::catch_unwind(|| {
                let _ = parse(&text);
                let _ = serde_json::from_str::<Request>(&text);
            })
            .is_ok()
        );
    }
}
#[test]
fn mutated_valid_corpus_does_not_panic() {
    let original = include_bytes!("../examples/divider.json");
    for index in 0..original.len() {
        for mask in [1, 0x7f, 0xff] {
            let mut bytes = original.to_vec();
            bytes[index] ^= mask;
            assert!(
                std::panic::catch_unwind(|| {
                    let _ = serde_json::from_slice::<Request>(&bytes);
                })
                .is_ok()
            );
        }
    }
}
