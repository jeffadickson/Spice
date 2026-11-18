//! Imported analysis metadata must reach execution rather than fall back to defaults.
use circuit_simulator::{api::{self,Analysis},netlist::import_ngspice_request};

#[test]
fn imported_transient_step_and_stop_control_actual_constant_source_history() {
    // One PWL point defines a constant 2V source. Both default config values
    // differ from these requested values, so silently dropping a field fails.
    for (directive,maximum,stop) in [(".tran 1u 2u",1e-6,2e-6),(".tran 1u 5u 0 2u",2e-6,5e-6)] {
        let request=import_ngspice_request(&format!("one point\nV1 a 0 pwl(0,2)\nR1 a 0 1k\n{directive}\n.end"),"imported-transient").unwrap();
        let Analysis::TransientConfigured{options}=&request.analysis else {panic!("requested configured analysis was lost")};
        assert!((options.max_step_s-maximum).abs()<1e-18);
        assert!((options.stop_s-stop).abs()<1e-18);
        let result=api::run(&request).unwrap();
        let voltage=result.signals.iter().position(|s|s.id=="v(a)").expect("missing node voltage");
        assert_eq!(result.samples.first().unwrap().time_s,0.);
        assert!((result.samples.last().unwrap().time_s-stop).abs()<1e-18);
        assert!(result.samples.len()>=3);
        for pair in result.samples.windows(2){
            assert!(pair[1].time_s>pair[0].time_s);
            assert!(pair[1].time_s-pair[0].time_s<=maximum+1e-18);
        }
        for sample in result.samples {assert!((sample.values[voltage]-2.).abs()<1e-12);}
    }
}
