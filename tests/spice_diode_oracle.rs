//! Separate-process ngspice reference; ignored unless explicitly invoked with an
//! existing oracle executable and artifact parent. No corpus control is executed.
use circuit_simulator::api::{Request, run};
#[test]
#[ignore = "requires SPICE_NGSPICE and SPICE_ORACLE_ARTIFACT_ROOT"]
fn nonzero_depletion_transport_dc_ac_transient_against_ngspice() {
    let oracle = std::env::var("SPICE_NGSPICE").expect("existing oracle path");
    let parent = std::path::PathBuf::from(
        std::env::var("SPICE_ORACLE_ARTIFACT_ROOT").expect("artifact parent"),
    );
    assert!(parent.is_dir());
    let dir = parent.join(format!("diode-oracle-{}", std::process::id()));
    std::fs::create_dir(&dir).unwrap();
    let path = |name: &str| dir.join(name).to_string_lossy().replace('\\', "/");
    let vt = 1.38064852e-23 / 1.6021766208e-19 * 300.15;
    let deck = format!(
        "Junction oracle\nV1 in 0 .5 AC 1\nR1 in out 1k\nD1 out 0 dm\n.model dm D(IS=1e-14 N=1 CJO=2p VJ=.8 M=.5 FC=.5 TT=1n)\n.options method=gear maxord=1 reltol=1e-9 abstol=1e-15 vntol=1e-12\n.control\nset wr_singlescale\nset wr_vecnames\nop\nwrdata {} v(out) i(V1)\nac lin 1 1000000 1000000\nwrdata {} v(out) i(V1)\nreset\nset init\ntran 1p 10n uic\nwrdata {} v(out) i(V1)\nquit\n.endc\n.ic v(out)=0\n.end\n",
        path("op.txt"),
        path("ac.txt"),
        path("tran.txt")
    );
    std::fs::write(dir.join("oracle.cir"), deck).unwrap();
    let output = std::process::Command::new(oracle)
        .args(["-b", "-o", &path("oracle.log"), &path("oracle.cir")])
        .output()
        .unwrap();
    assert!(output.status.success(), "{:?}", output);
    let read = |name: &str| -> Vec<Vec<f64>> {
        std::fs::read_to_string(dir.join(name))
            .unwrap()
            .lines()
            .skip(1)
            .filter(|s| !s.trim().is_empty())
            .map(|s| s.split_whitespace().map(|n| n.parse().unwrap()).collect())
            .collect()
    };
    let op = read("op.txt");
    let ac = read("ac.txt");
    let tr = read("tran.txt");
    assert!(tr.len() > 100);
    for backend in ["lu", "faer"] {
        let request = |analysis: serde_json::Value| -> Request {
            serde_json::from_value(serde_json::json!({"schema_version":"1","case_id":"diode-oracle","backend":backend,"circuit":{"nodes":["0","in","out"],"elements":[{"kind":"voltage","id":"V1","p":1,"n":0,"volts":0.5},{"kind":"resistor","id":"R1","p":1,"n":2,"ohms":1000.},{"kind":"spice_diode","id":"D1","p":2,"n":0,"model":{"saturation_amps":1e-14,"thermal_volts":vt,"junction_farads":2e-12,"junction_volts":0.8,"grading":0.5,"forward_coefficient":0.5,"transit_seconds":1e-9}}]},"analysis":analysis})).unwrap()
        };
        let dc = run(&request(serde_json::json!({"kind":"operating_point"}))).unwrap();
        std::fs::write(
            dir.join(format!("native-{backend}-op.json")),
            serde_json::to_vec_pretty(&dc).unwrap(),
        )
        .unwrap();
        assert!((dc.samples[0].values[1] - op[0][1]).abs() < 2e-8);
        let total_dc = dc.signals.iter().position(|s| s.id == "i(D1)").unwrap();
        assert!((dc.samples[0].values[total_dc] + op[0][2]).abs() < 2e-11);
        let native_ac=run(&request(serde_json::json!({"kind":"ac","frequencies_hz":[1e6],"source_phasors":[{"id":"V1","real":1.,"imag":0.}]}))).unwrap();
        let c = &native_ac.ac_samples[0].values[1];
        std::fs::write(
            dir.join(format!("native-{backend}-ac.json")),
            serde_json::to_vec_pretty(&native_ac).unwrap(),
        )
        .unwrap();
        assert!((c.real - ac[0][1]).abs() < 2e-6);
        assert!((c.imag - ac[0][2]).abs() < 2e-6);
        let total_ac = native_ac
            .signals
            .iter()
            .position(|s| s.id == "i(D1)")
            .unwrap();
        let ci = &native_ac.ac_samples[0].values[total_ac];
        assert!((ci.real + ac[0][3]).abs() < 2e-9);
        assert!((ci.imag + ac[0][4]).abs() < 2e-9);
        let native_tr=run(&request(serde_json::json!({"kind":"transient","options":{"stop_s":1e-8,"step_s":1e-11,"max_steps":1001}}))).unwrap();
        std::fs::write(
            dir.join(format!("native-{backend}-transient.json")),
            serde_json::to_vec_pretty(&native_tr).unwrap(),
        )
        .unwrap();
        for t in [1e-9, 5e-9, 1e-8] {
            let sample = native_tr
                .samples
                .iter()
                .min_by(|a, b| (a.time_s - t).abs().total_cmp(&(b.time_s - t).abs()))
                .unwrap();
            let total_tr = native_tr
                .signals
                .iter()
                .position(|s| s.id == "i(D1)")
                .unwrap();
            let reference = tr
                .iter()
                .min_by(|a, b| {
                    (a[0] - sample.time_s)
                        .abs()
                        .total_cmp(&(b[0] - sample.time_s).abs())
                })
                .unwrap();
            assert!(
                (sample.values[1] - reference[1]).abs() < 0.002,
                "{backend} {t}: {} vs {}",
                sample.values[1],
                reference[1]
            );
            assert!((sample.values[total_tr] + reference[2]).abs() < 2e-6);
        }
    }
    println!("oracle artifacts {}", dir.display());
}
