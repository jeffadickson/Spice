//! Manufacture states whose conservation and constitutive constraints differ.
use circuit_simulator::{audit::{self, AuditOptions, AuditReport, Check, CheckStatus},
    circuit::{Circuit, Element}, expression::Expr, ids::NodeId as N, mos1::Mos1Model};

fn check<'a>(report: &'a AuditReport, id: &str) -> &'a Check {
    report.checks.iter().find(|c|c.id==id).unwrap_or_else(||panic!("missing {id}"))
}
fn kcl_passes(report: &AuditReport) {
    assert!(report.checks.iter().filter(|c|c.id.starts_with("kcl:")).all(|c|c.status==CheckStatus::Passed));
}

#[test]
fn dc_inductor_constraint_is_present_and_rejects_a_conservation_consistent_voltage() {
    let circuit=Circuit {nodes:vec!["0".into(),"a".into(),"b".into()],elements:vec![
        Element::Voltage{id:"V".into(),p:N(1),n:N(0),volts:1.},
        Element::Inductor{id:"L".into(),p:N(1),n:N(2),henries:0.25},
        Element::Resistor{id:"R".into(),p:N(2),n:N(0),ohms:1.},
    ]};
    for (b,current,expected) in [(1.,1.,CheckStatus::Passed),(0.5,0.5,CheckStatus::Failed)] {
        let report=audit::dc(&circuit,&[1.,b,-current,current],AuditOptions::default(),None).unwrap();
        kcl_passes(&report);
        assert_eq!(check(&report,"power_balance").status,CheckStatus::Passed);
        assert_eq!(check(&report,"constraint:L").status,expected);
    }
}

#[test]
fn ccvs_constraint_uses_the_signed_nonzero_sensing_current() {
    let circuit=Circuit {nodes:vec!["0".into(),"sense".into(),"out".into()],elements:vec![
        Element::Voltage{id:"V".into(),p:N(1),n:N(0),volts:2.},
        Element::Resistor{id:"RS".into(),p:N(1),n:N(0),ohms:4.},
        Element::Ccvs{id:"H".into(),p:N(2),n:N(0),control_id:"V".into(),ohms:-3.},
        Element::Resistor{id:"RL".into(),p:N(2),n:N(0),ohms:3.},
    ]};
    // V supplies 0.5 A, giving H=-3*(-0.5)=+1.5 V.
    for (out,expected) in [(1.5,CheckStatus::Passed),(0.75,CheckStatus::Failed)] {
        let report=audit::dc(&circuit,&[2.,out,-0.5,-out/3.],AuditOptions::default(),None).unwrap();
        kcl_passes(&report);
        assert_eq!(check(&report,"power_balance").status,CheckStatus::Passed);
        assert_eq!(check(&report,"constraint:H").status,expected);
    }
}

#[test]
fn behavioral_voltage_constraint_and_relative_bound_use_differential_magnitudes() {
    let options=AuditOptions::default();
    for offset in [-2.,0.,3.] {
        for drive in [-0.5,0.75,1.5] {
            let cn=1.25+offset;let cp=cn+drive;let out=2.*drive;
            let circuit=Circuit {nodes:["0","out","cp","cn"].map(str::to_owned).to_vec(),elements:vec![
                Element::Voltage{id:"VP".into(),p:N(2),n:N(0),volts:cp},
                Element::Voltage{id:"VN".into(),p:N(3),n:N(0),volts:cn},
                Element::BehavioralVoltage{id:"E".into(),p:N(1),n:N(0),cp:N(2),cn:N(3),
                    expression:Expr::Multiply(Box::new(Expr::Constant(2.)),Box::new(Expr::Input))},
                Element::Resistor{id:"RL".into(),p:N(1),n:N(0),ohms:3.},
            ]};
            let report=audit::dc(&circuit,&[out,cp,cn,0.,0.,-out/3.],options,None).unwrap();
            assert!(!report.has_failures());
            let constraint=check(&report,"constraint:E");
            let expected_bound=options.voltage_absolute_v+options.relative*2.*out.abs();
            assert!((constraint.tolerance.unwrap()-expected_bound).abs()<expected_bound*1e-12);
            let wrong=out+0.25;
            let report=audit::dc(&circuit,&[wrong,cp,cn,0.,0.,-wrong/3.],options,None).unwrap();
            kcl_passes(&report);
            assert_eq!(check(&report,"constraint:E").status,CheckStatus::Failed);
        }
    }
}

#[test]
fn mos_charge_energy_qualification_is_not_a_fabricated_numeric_pass() {
    for oxide in [0.,3e-9] {
        let model=Mos1Model{p_channel:false,oxide_farads:oxide,threshold_volts:1.,
            beta_amps_per_volt_squared:2e-4,body_effect_sqrt_volts:0.,surface_potential_volts:0.6,
            channel_length_modulation_per_volt:0.};
        let mut elements:Vec<_>=[("VD",0.5),("VG",2.),("VS",0.),("VB",0.)].into_iter().enumerate()
            .map(|(i,(id,volts))|Element::Voltage{id:id.into(),p:N(i+1),n:N(0),volts}).collect();
        elements.push(Element::Mos1Channel{id:"M".into(),p:N(1),n:N(3),gate:N(2),bulk:N(4),model});
        let circuit=Circuit{nodes:["0","d","g","s","b"].map(str::to_owned).to_vec(),elements};
        // Triode current beta*(Vov*Vds-Vds^2/2)=75 uA. A stationary
        // step has no displacement; the model's energy qualification is distinct.
        let state=[0.5,2.,0.,0.,-7.5e-5,0.,7.5e-5,0.];
        for h in [2e-4,7e-4] {
            let report=audit::backward_euler_step(&circuit,&state,&state,h,AuditOptions::default()).unwrap();
            kcl_passes(&report);
            let energy=check(&report,"be_energy_balance");
            if oxide==0. {assert_eq!(energy.status,CheckStatus::Passed);}
            else {
                assert_eq!(energy.status,CheckStatus::NotChecked);
                assert!(energy.discrepancy.is_none() && energy.tolerance.is_none() && energy.reason.is_some());
            }
        }
    }
}

#[test]
fn nonunit_gmin_power_bound_counts_source_load_and_shunt_magnitudes() {
    let options=AuditOptions::default();
    let circuit=Circuit{nodes:vec!["0".into(),"a".into()],elements:vec![
        Element::Voltage{id:"V".into(),p:N(1),n:N(0),volts:2.},
        Element::Resistor{id:"R".into(),p:N(1),n:N(0),ohms:2.},
    ]};
    for gmin in [0.125,0.25,0.75] {
        let report=audit::dc(&circuit,&[2.,-(1.+2.*gmin)],options,Some(gmin)).unwrap();
        let power=check(&report,"power_balance");
        assert_eq!(power.status,CheckStatus::Passed);
        // Absorbed powers: source=-(2+4G), load=2, shunt=4G watts.
        // Their signed sum is zero; their absolute sum is 4+8G, not 4.
        let expected=options.power_absolute_w+options.relative*(4.+8.*gmin);
        assert!((power.tolerance.unwrap()-expected).abs()<expected*1e-12);
    }
}
