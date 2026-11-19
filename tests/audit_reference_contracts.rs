//! Manufactured physical states test the independent value-only audit path.
use circuit_simulator::{audit::{self, AuditOptions}, circuit::{Circuit, Element},
    error::ErrorCode, expression::Expr, ids::NodeId as N};

fn fixture(expression: Expr, input: f64, resistance: f64, offset: f64) -> (Circuit, Vec<f64>) {
    let cn = 1.25 + offset;
    let cp = cn + input;
    let circuit = Circuit {nodes:["0","out","cp","cn"].map(str::to_owned).to_vec(), elements:vec![
        Element::Voltage{id:"VO".into(),p:N(1),n:N(0),volts:1.},
        Element::Voltage{id:"VP".into(),p:N(2),n:N(0),volts:cp},
        Element::Voltage{id:"VN".into(),p:N(3),n:N(0),volts:cn},
        Element::BehavioralResistor{id:"R".into(),p:N(1),n:N(0),cp:N(2),cn:N(3),expression},
        Element::Vccs{id:"G".into(),p:N(1),n:N(0),cp:N(2),cn:N(3),siemens:0.125},
    ]};
    // 1 V load: resistor current plus controlled current must be supplied by VO.
    let state = vec![1.,cp,cn,-(1./resistance+0.125*input),0.,0.];
    (circuit,state)
}

#[test]
fn every_reference_operator_and_nonzero_control_terminal_obeys_independent_values() {
    use std::f64::consts::{PI,E};
    let cases = vec![
        (Expr::Sin(Box::new(Expr::Input)),PI/6.,0.5),
        (Expr::Cos(Box::new(Expr::Input)),PI/3.,0.5),
        (Expr::Tan(Box::new(Expr::Input)),PI/4.,1.),
        (Expr::Atan(Box::new(Expr::Input)),1.,PI/4.),
        (Expr::Sqrt(Box::new(Expr::Input)),4.,2.),
        (Expr::Floor(Box::new(Expr::Input)),2.75,2.),
        (Expr::Ceil(Box::new(Expr::Input)),2.25,3.),
        (Expr::Truncate(Box::new(Expr::Input)),2.75,2.),
        (Expr::Abs(Box::new(Expr::Input)),-2.5,2.5),
        (Expr::Asin(Box::new(Expr::Input)),0.5,PI/6.),
        (Expr::Acos(Box::new(Expr::Input)),0.5,PI/3.),
        (Expr::Exp(Box::new(Expr::Input)),2_f64.ln(),2.),
        (Expr::Log(Box::new(Expr::Input)),E,1.),
        (Expr::Input,1.5,1.5), (Expr::Constant(2.),0.75,2.),
        (Expr::Add(Box::new(Expr::Input),Box::new(Expr::Constant(2.))),0.75,2.75),
        (Expr::Multiply(Box::new(Expr::Input),Box::new(Expr::Constant(3.))),0.75,2.25),
        (Expr::Divide(Box::new(Expr::Input),Box::new(Expr::Constant(2.))),0.75,0.375),
    ];
    for (expression,input,expected) in cases {
        for offset in [-2.,0.,3.] {
            let (circuit,mut state) = fixture(expression.clone(),input,expected,offset);
            assert!(!audit::dc(&circuit,&state,AuditOptions::default(),None).unwrap().has_failures(),"{expression:?}");
            state[3] += 0.01;
            assert!(audit::dc(&circuit,&state,AuditOptions::default(),None).unwrap().has_failures());
        }
    }
}

fn chain(depth: usize) -> Expr {
    (0..depth).fold(Expr::Input,|expr,_|Expr::Abs(Box::new(expr)))
}
fn balanced(depth: usize) -> Expr {
    if depth==0 {Expr::Constant(1./256.)} else {
        Expr::Add(Box::new(balanced(depth-1)),Box::new(balanced(depth-1)))
    }
}
#[test]
fn public_validation_and_reference_evaluation_agree_at_exact_structure_limits() {
    let max_nodes = Expr::Abs(Box::new(balanced(9))); // 1023 + 1 nodes, depth 10.
    for (expr,input,resistance) in [(chain(64),1.75,1.75),(max_nodes.clone(),0.75,2.)] {
        let (circuit,state)=fixture(expr,input,resistance,0.);
        circuit.validate().unwrap();
        assert!(!audit::dc(&circuit,&state,AuditOptions::default(),None).unwrap().has_failures());
    }
    for expr in [chain(65),Expr::Abs(Box::new(max_nodes))] {
        let (circuit,state)=fixture(expr,0.75,1.,0.);
        // Public validation dominates the reference's defensive traversal guard.
        assert_eq!(circuit.validate().unwrap_err().code,ErrorCode::InvalidInput);
        assert_eq!(audit::dc(&circuit,&state,AuditOptions::default(),None).unwrap_err().code,ErrorCode::InvalidInput);
    }
}

#[test]
fn reference_domains_fail_without_substituting_a_zero_or_passing_audit() {
    for expr in [Expr::Log(Box::new(Expr::Input)),
        Expr::Divide(Box::new(Expr::Constant(1.)),Box::new(Expr::Constant(0.)))] {
        let (circuit,state)=fixture(expr,-1.,1.,0.);
        assert_eq!(audit::dc(&circuit,&state,AuditOptions::default(),None).unwrap_err().code,ErrorCode::InvalidInput);
    }
}

#[test]
fn explicit_nonunit_gmin_is_added_to_physical_node_currents() {
    let circuit=Circuit {nodes:vec!["0".into(),"a".into()],elements:vec![
        Element::Voltage{id:"V".into(),p:N(1),n:N(0),volts:2.},
        Element::Resistor{id:"R".into(),p:N(1),n:N(0),ohms:2.},
    ]};
    for gmin in [0.125,0.25,0.75] {
        let state=[2.,-(1.+2.*gmin)];
        assert!(!audit::dc(&circuit,&state,AuditOptions::default(),Some(gmin)).unwrap().has_failures());
        assert!(audit::dc(&circuit,&[2.,-1.],AuditOptions::default(),Some(gmin)).unwrap().has_failures());
    }
}

#[test]
fn voltage_scaled_cccs_uses_signed_differential_control_and_sensed_branch_current() {
    for offset in [-2.,0.,3.] {
        for control in [-0.5,0.25,1.5] {
            let cn=1.25+offset; let cp=cn+control;
            let circuit=Circuit {nodes:["0","out","cp","cn","sense"].map(str::to_owned).to_vec(),elements:vec![
                Element::Voltage{id:"VO".into(),p:N(1),n:N(0),volts:1.},
                Element::Voltage{id:"VP".into(),p:N(2),n:N(0),volts:cp},
                Element::Voltage{id:"VN".into(),p:N(3),n:N(0),volts:cn},
                Element::Voltage{id:"VS".into(),p:N(4),n:N(0),volts:1.},
                Element::Resistor{id:"RS".into(),p:N(4),n:N(0),ohms:4.},
                Element::VoltageScaledCccs{id:"F".into(),p:N(1),n:N(0),cp:N(2),cn:N(3),
                    control_id:"VS".into(),gain:-2.,voltage_coefficient:0.5,factor_offset:0.75},
            ]};
            // VS supplies a 4-ohm load at 1 V, so its signed branch current
            // is -0.25 A. F therefore draws 0.5*(0.75+0.5*control) A.
            let current=0.5*(0.75+0.5*control);
            let mut state=vec![1.,cp,cn,1.,-current,0.,0.,-0.25];
            assert!(!audit::dc(&circuit,&state,AuditOptions::default(),None).unwrap().has_failures());
            state[4]+=0.01;
            assert!(audit::dc(&circuit,&state,AuditOptions::default(),None).unwrap().has_failures());
        }
    }
}
