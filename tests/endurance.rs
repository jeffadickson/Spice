//! Bounded seeded endurance batches. The C# supervisor owns wall-clock limits;
//! this ignored test owns per-case replay and saves parser panic counterexamples.
use circuit_simulator::{
    api::Request,
    audit::{AuditOptions, dc},
    backend::DenseLu,
    compile::compile,
    dc::operating_point,
    model::DiodeModel,
    netlist::{import_ngspice_request, parse},
    sparse::CscMatrix,
};
use std::{
    panic::{AssertUnwindSafe, catch_unwind},
    path::PathBuf,
};
const STEP: u64 = 0x9e3779b97f4a7c15;
fn next(s: &mut u64) -> u64 {
    *s = s.wrapping_mul(6364136223846793005).wrapping_add(1);
    *s
}
fn unit(s: &mut u64) -> f64 {
    (next(s) >> 11) as f64 / (1u64 << 53) as f64
}
fn parser_input(seed: u64) -> Vec<u8> {
    let corpus: [&[u8]; 4] = [
        include_bytes!("../examples/divider.json"),
        b"title\nV1 a 0 1\nR1 a b 1k\nC1 b 0 1u\n.tran 1u 1m\n.end\n",
        b"title\n.model DM D(IS=1e-12,N=1)\nV1 a 0 1\nR1 a b 1k\nD1 b 0 DM\n.end\n",
        b"title\nV1 a 0 AC 1 90\nR1 a 0 1k\n.ac lin 3 1 100\n.end\n",
    ];
    let mut s = seed;
    let mut bytes = corpus[(next(&mut s) % 4) as usize].to_vec();
    for _ in 0..1 + next(&mut s) % 8 {
        let index = (next(&mut s) as usize) % bytes.len();
        bytes[index] ^= (next(&mut s) >> 32) as u8;
    }
    bytes
}
fn parsers(bytes: &[u8]) {
    let text = String::from_utf8_lossy(bytes);
    let _ = parse(&text);
    let _ = import_ngspice_request(&text, "endurance");
    if let Ok(request) = serde_json::from_slice::<Request>(bytes) {
        let _ = request.circuit.validate();
    }
}
fn case(seed: u64) {
    let mut s = seed;
    match seed % 4 {
        0 => parsers(&parser_input(seed)),
        1 => {
            // Independent divider solution spans six resistance decades and both polarities.
            let r1 = 10f64.powf(6. * unit(&mut s));
            let r2 = 10f64.powf(6. * unit(&mut s));
            let v = -100. + 200. * unit(&mut s);
            let c = parse(&format!("title\nV1 a 0 {v}\nR1 a b {r1}\nR2 b 0 {r2}")).unwrap();
            let p = compile(&c).unwrap();
            let x = operating_point(&p, &DenseLu).unwrap();
            assert!((x[1] - v * r2 / (r1 + r2)).abs() < 1e-7);
            assert!(
                !dc(&c, &x, AuditOptions::default(), None)
                    .unwrap()
                    .has_failures()
            );
            let wrong = [x[0], x[1], x[2] + 1.];
            assert!(
                dc(&c, &wrong, AuditOptions::default(), None)
                    .unwrap()
                    .has_failures()
            );
        }
        2 => {
            let model = DiodeModel {
                saturation_amps: 10f64.powf(-15. + 6. * unit(&mut s)),
                thermal_volts: 0.02 + 0.02 * unit(&mut s),
            };
            let v = -0.1 + 0.6 * unit(&mut s);
            let h = 1e-6;
            let analytic = model.linearize(v).unwrap().conductance_siemens;
            let numerical = (model.linearize(v + h).unwrap().current_amps
                - model.linearize(v - h).unwrap().current_amps)
                / (2. * h);
            assert!((analytic - numerical).abs() < 1e-12 + 1e-6 * analytic.abs());
        }
        _ => {
            let n = (next(&mut s) % 12) as usize;
            let len = (next(&mut s) % 32) as usize;
            let pointers = (0..=n).map(|_| (next(&mut s) % 40) as usize).collect();
            let rows = (0..len).map(|_| (next(&mut s) % 16) as usize).collect();
            let values = (0..len).map(|_| f64::from_bits(next(&mut s))).collect();
            let _ = CscMatrix::new(n, pointers, rows, values);
        }
    }
}
/// Manual replay: SPICE_CAMPAIGN_SEED=<decimal>, SPICE_CAMPAIGN_CASES=1,
/// cargo test --test endurance -- --ignored --exact seeded_batch --nocapture.
#[test]
#[ignore = "bounded batches are launched by scripts/Endurance.cs"]
fn seeded_batch() {
    let seed = std::env::var("SPICE_CAMPAIGN_SEED")
        .unwrap_or_else(|_| "1".into())
        .parse::<u64>()
        .expect("invalid seed");
    let count = std::env::var("SPICE_CAMPAIGN_CASES")
        .unwrap_or_else(|_| "1024".into())
        .parse::<u64>()
        .expect("invalid case budget");
    assert!((1..=100_000).contains(&count));
    for index in 0..count {
        let current = seed.wrapping_add(index.wrapping_mul(STEP));
        if catch_unwind(AssertUnwindSafe(|| case(current))).is_err() {
            if let Ok(path) = std::env::var("SPICE_CAMPAIGN_FAILURES") {
                let root = PathBuf::from(path);
                std::fs::create_dir_all(&root).unwrap();
                std::fs::write(root.join("failure.json"),serde_json::to_vec_pretty(&serde_json::json!({"seed":current.to_string(),"kind":current%4,"batch_index":index,"replay_cases":1})).unwrap()).unwrap();
                if current % 4 == 0 {
                    let original = parser_input(current);
                    std::fs::write(root.join("original.bin"), &original).unwrap();
                    // Bounded deletion minimization; only accept reductions reproducing a parser panic.
                    let mut minimal = original;
                    let mut attempts = 0;
                    let mut at = 0;
                    while at < minimal.len() && attempts < 256 {
                        let mut trial = minimal.clone();
                        trial.remove(at);
                        attempts += 1;
                        if catch_unwind(AssertUnwindSafe(|| parsers(&trial))).is_err() {
                            minimal = trial;
                        } else {
                            at += 1;
                        }
                    }
                    std::fs::write(root.join("minimized.bin"), minimal).unwrap();
                }
            }
            panic!(
                "endurance failure: replay seed={current}, cases=1, kind={}",
                current % 4
            );
        }
    }
    println!("ENDURANCE cases={count} seed={seed}");
}
