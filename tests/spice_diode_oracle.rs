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

#[test]
#[ignore = "requires SPICE_NGSPICE and SPICE_ORACLE_ARTIFACT_ROOT"]
fn nongrounded_diode_currents_agree_with_ngspice_under_common_mode_offsets() {
    let oracle=std::env::var("SPICE_NGSPICE").expect("existing oracle");
    let parent=std::path::PathBuf::from(std::env::var("SPICE_ORACLE_ARTIFACT_ROOT").unwrap());
    assert!(parent.is_dir());
    let root=parent.join(format!("floating-diode-oracle-{}",std::process::id()));
    std::fs::create_dir(&root).unwrap();
    let vt=1.38064852e-23/1.6021766208e-19*300.15;
    let (mut dc_v,mut dc_i,mut ac_v,mut ac_i,mut tr_v,mut tr_i)=(0_f64,0_f64,0_f64,0_f64,0_f64,0_f64);
    for (case,common) in [-0.4,0.2,1.].into_iter().enumerate() {
        let dir=root.join(case.to_string());std::fs::create_dir(&dir).unwrap();
        let path=|name:&str|dir.join(name).to_string_lossy().replace('\\',"/");
        // Translate every circuit node by VCM without changing the physical
        // differential excitation or comparison bounds. OP startup and a bounded
        // differential ramp avoid the unrelated ideal-VCM UIC startup failure.
        let deck=format!("Floating junction oracle\nVCM ref 0 {common}\nV1 in ref DC .5 AC 1 PWL(0 .5 1n .5 2n .55 5n .55 6n .45 10n .45)\nR1 in out 1k\nD1 out ref dm\n.model dm D(IS=1e-14 N=1 CJO=2p VJ=.8 M=.5 FC=.5 TT=1n)\n.options method=gear maxord=1 reltol=1e-9 abstol=1e-15 vntol=1e-12\n.control\nset wr_singlescale\nset wr_vecnames\nop\nwrdata {} v(out,ref) i(V1)\nac lin 1 1000000 1000000\nwrdata {} v(out,ref) i(V1)\nreset\ntran 1p 10n\nwrdata {} v(out,ref) i(V1)\nquit\n.endc\n.end\n",path("op.txt"),path("ac.txt"),path("tran.txt"));
        std::fs::write(dir.join("oracle.cir"),deck).unwrap();
        let output=std::process::Command::new(&oracle).args(["-b","-o",&path("oracle.log"),&path("oracle.cir")]).output().unwrap();
        assert!(output.status.success(),"ngspice case{case}: {output:?}");
        let read=|name:&str|->Vec<Vec<f64>> {
            std::fs::read_to_string(dir.join(name)).unwrap().lines().skip(1)
                .filter(|line|!line.trim().is_empty()).map(|line|line.split_whitespace()
                .map(|value|value.parse::<f64>().unwrap()).collect()).collect()
        };
        let op=read("op.txt");let ac=read("ac.txt");let transient=read("tran.txt");
        assert_eq!(op.len(),1);assert_eq!(op[0].len(),3);
        assert_eq!(ac.len(),1);assert_eq!(ac[0].len(),5);
        assert!(transient.len()>100);assert!(transient.iter().all(|row|row.len()==3 && row.iter().all(|v|v.is_finite())));
        assert!(transient.last().unwrap()[0]>=1e-8*(1.-1e-10),"ngspice truncated transient case{case}; inspect oracle.log");
        for backend in ["lu","faer"] {
            let request=|analysis:serde_json::Value|->Request {
                serde_json::from_value(serde_json::json!({"schema_version":"1","case_id":"floating-oracle","backend":backend,
                  "circuit":{"nodes":["0","in","out","ref"],"elements":[
                    {"kind":"voltage","id":"VCM","p":3,"n":0,"volts":common},
                    {"kind":"voltage","id":"V1","p":1,"n":3,"volts":0.5},
                    {"kind":"resistor","id":"R1","p":1,"n":2,"ohms":1000.},
                    {"kind":"spice_diode","id":"D1","p":2,"n":3,"model":{
                       "saturation_amps":1e-14,"thermal_volts":vt,"junction_farads":2e-12,
                       "junction_volts":0.8,"grading":0.5,"forward_coefficient":0.5,"transit_seconds":1e-9}}
                  ]},"analysis":analysis})).unwrap()
            };
            let dc=run(&request(serde_json::json!({"kind":"operating_point"}))).unwrap();
            let current=|r:&circuit_simulator::api::Manifest|r.signals.iter().position(|s|s.id=="i(D1)").unwrap();
            dc_v=dc_v.max((dc.samples[0].values[1]-dc.samples[0].values[2]-op[0][1]).abs());
            assert!((dc.samples[0].values[1]-dc.samples[0].values[2]-op[0][1]).abs()<2e-8);
            dc_i=dc_i.max((dc.samples[0].values[current(&dc)]+op[0][2]).abs());
            assert!((dc.samples[0].values[current(&dc)]+op[0][2]).abs()<2e-11);
            let native_ac=run(&request(serde_json::json!({"kind":"ac","frequencies_hz":[1e6],
                "source_phasors":[{"id":"V1","real":1.,"imag":0.}]}))).unwrap();
            assert_eq!(native_ac.ac_samples.len(),1);
            assert_eq!(native_ac.ac_samples[0].frequency_hz,1e6);
            let a=&native_ac.ac_samples[0].values;
            ac_v=ac_v.max((a[1].real-a[2].real-ac[0][1]).abs()).max((a[1].imag-a[2].imag-ac[0][2]).abs());
            ac_i=ac_i.max((a[current(&native_ac)].real+ac[0][3]).abs()).max((a[current(&native_ac)].imag+ac[0][4]).abs());
            assert!((a[1].real-a[2].real-ac[0][1]).abs()<2e-6);
            assert!((a[1].imag-a[2].imag-ac[0][2]).abs()<2e-6);
            assert!((a[current(&native_ac)].real+ac[0][3]).abs()<2e-9);
            assert!((a[current(&native_ac)].imag+ac[0][4]).abs()<2e-9);
            let native_tr=run(&request(serde_json::json!({"kind":"transient_configured","options":{
                "stop_s":1e-8,"max_step_s":1e-11,"max_steps":2000,"method":"backward_euler",
                "sources":[{"id":"V1","waveform":{"kind":"pwl","points":[
                    {"time_s":0.,"value":0.5},{"time_s":1e-9,"value":0.5},
                    {"time_s":2e-9,"value":0.55},{"time_s":5e-9,"value":0.55},
                    {"time_s":6e-9,"value":0.45},{"time_s":1e-8,"value":0.45}]}}]}}))).unwrap();
            assert!(native_tr.samples.last().unwrap().time_s>=1e-8*(1.-1e-10),
                "native transient did not reach the requested stop");
            let total=current(&native_tr);
            for time in [1e-9,3e-9,5e-9,7e-9,1e-8] {
                let sample=native_tr.samples.iter().min_by(|a,b|(a.time_s-time).abs().total_cmp(&(b.time_s-time).abs())).unwrap();
                let reference=transient.iter().min_by(|a,b|(a[0]-sample.time_s).abs().total_cmp(&(b[0]-sample.time_s).abs())).unwrap();
                // Native points are requested grid/corner times. ngspice's
                // maximum reference step is1ps; never compare a remote point
                // merely because it is the nearest surviving sample.
                assert!((sample.time_s-time).abs()<=1e-16,"native requested-time coverage");
                assert!((reference[0]-time).abs()<=1e-12,"reference requested-time coverage");
                tr_v=tr_v.max((sample.values[1]-sample.values[2]-reference[1]).abs());
                tr_i=tr_i.max((sample.values[total]+reference[2]).abs());
                assert!((sample.values[1]-sample.values[2]-reference[1]).abs()<0.002,"{backend},case{case},t{time}");
                assert!((sample.values[total]+reference[2]).abs()<2e-6,"{backend},case{case},t{time}: {} vs{}",sample.values[total],-reference[2]);
            }
            for (name,result) in [("op",dc),("ac",native_ac),("transient",native_tr)] {
                std::fs::write(dir.join(format!("native-{backend}-{name}.json")),serde_json::to_vec_pretty(&result).unwrap()).unwrap();
            }
        }
    }
    std::fs::write(root.join("comparison-summary.json"),serde_json::to_vec_pretty(&serde_json::json!({
        "common_mode_volts":[-0.4,0.2,1.],"providers":["lu","faer"],"dc_comparisons":6,"ac_comparisons":6,"selected_transient_comparisons":30,
        "max_abs_dc_volts":dc_v,"max_abs_dc_amps":dc_i,"max_abs_ac_component_volts":ac_v,"max_abs_ac_component_amps":ac_i,
        "max_abs_transient_volts":tr_v,"max_abs_transient_amps":tr_i,
        "bounds":{"dc_volts":2e-8,"dc_amps":2e-11,"ac_component_volts":2e-6,"ac_component_amps":2e-9,"transient_volts":0.002,"transient_amps":2e-6}})).unwrap()).unwrap();
    println!("floating oracle artifacts {}",root.display());
}
