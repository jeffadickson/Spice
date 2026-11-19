//! Native current/charge audit wiring. Provider physics has separate ngspice
//! goldens in bsim330.rs; these manufactured states challenge terminal mapping,
//! displacement signs and tolerance scales without consuming solver stamps.
use circuit_simulator::{audit::{self, AuditOptions, AuditReport, CheckStatus}, circuit::{Circuit, Element}, ids::NodeId};

const PARAMETERS: [f64;11]=[1.,3.5e-9,2.4e17,5e16,0.15,1e-6,3.5e-7,1.5e-6,1.5e-6,1.5e-12,1.5e-12];

fn fixture(bias:[f64;4], currents:[f64;4])->(Circuit,Vec<f64>) {
    let mut circuit:Circuit=serde_json::from_str(r#"{"nodes":["0","d","g","s","b"],"elements":[{"kind":"bsim330","id":"M","p":1,"gate":2,"n":3,"bulk":4,"model":{"polarity":1,"tox":3.5e-9,"nch":2.4e17,"nsub":5e16,"vth0":0.15,"w":1e-6,"l":3.5e-7,"pd":1.5e-6,"ps":1.5e-6,"ad":1.5e-12,"as":1.5e-12}}]}"#).unwrap();
    let mut sources:Vec<_>=bias.into_iter().enumerate().map(|(i,volts)|Element::Voltage {
        id:format!("V{i}"),p:NodeId(i+1),n:NodeId(0),volts
    }).collect();
    sources.append(&mut circuit.elements);circuit.elements=sources;
    let mut state=bias.to_vec();state.extend(currents.map(|i|-i));(circuit,state)
}

fn conserved(report:&AuditReport,currents:[f64;4]) {
    let options=AuditOptions::default();
    let kcl:Vec<_>=report.checks.iter().filter(|c|c.id.starts_with("kcl:")).collect();
    assert_eq!(kcl.len(),5);
    for (i,name) in ["0","d","g","s","b"].into_iter().enumerate() {
        let c=kcl.iter().find(|c|c.id==format!("kcl:{name}")).unwrap();
        assert_eq!(c.status,CheckStatus::Passed,"{} {:?}",c.id,c.discrepancy);
        let scale=if i==0 {currents.iter().map(|i|i.abs()).sum()}else{2.*currents[i-1].abs()};
        let expected=options.current_absolute_a+options.relative*scale;
        assert!((c.tolerance.unwrap()-expected).abs()<expected*1e-12,"{} bound",c.id);
    }
}

#[test]
#[ignore="requires explicitly qualified native BSIM3 provider and model"]
fn native_terminal_current_and_dynamic_charge_audits() {
    // Translation makes source-terminal power observable too. Ideal gate and
    // near-zero bulk conduction remain an explicit coverage limitation.
    for offset in [0.,0.15] {
        let bias=[0.8+offset,0.8+offset,offset,offset];
        let values=spice_bsim330_host::evaluate(PARAMETERS,bias).unwrap();
        // Independent historical ngspice DC golden, not a provider-derived zero.
        assert!((values[0]-7.34294748e-5).abs()<2e-12);
        let currents=std::array::from_fn(|i|values[i]);
        let (circuit,state)=fixture(bias,currents);
        let report=audit::dc(&circuit,&state,AuditOptions::default(),None).unwrap();
        conserved(&report,currents);
        assert!(!report.has_failures());
        assert_eq!(report.checks.iter().find(|c|c.id=="power_balance").unwrap().status,CheckStatus::Passed);
    }
    for h in [1e-10,3e-10] {
        let before=[0.8,0.7999,0.,0.];let after=[0.8,0.8001,0.,0.];
        let old=spice_bsim330_host::evaluate(PARAMETERS,before).unwrap();
        let new=spice_bsim330_host::evaluate(PARAMETERS,after).unwrap();
        assert!((old[5]-3.860642374545776e-15).abs()<1e-23);
        assert!((new[5]-3.861555817043642e-15).abs()<1e-23);
        let total=std::array::from_fn(|i|new[i]+(new[i+4]-old[i+4])/h);
        let (circuit,state)=fixture(after,total);
        let (_,previous)=fixture(before,std::array::from_fn(|i|old[i]));
        let report=audit::backward_euler_step(&circuit,&previous,&state,h,AuditOptions::default()).unwrap();
        conserved(&report,total);
        let energy=report.checks.iter().find(|c|c.id=="be_energy_balance").unwrap();
        assert_eq!(energy.status,CheckStatus::NotChecked);
        assert!(energy.discrepancy.is_none()&&energy.tolerance.is_none()&&energy.reason.is_some());
        for terminal in 0..4 {
            let mut wrong=state.clone();wrong[4+terminal]+=1e-6;
            let report=audit::backward_euler_step(&circuit,&previous,&wrong,h,AuditOptions::default()).unwrap();
            assert!(report.has_failures(),"undetected terminal {terminal} corruption");
        }
    }
}
