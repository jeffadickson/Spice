//! Independent evidence-selection and malformed-refinement contracts.
use circuit_simulator::{audit::{AuditReport,Check,CheckStatus,compare_refinement},
    transient::Sample,error::ErrorCode};
use proptest::{prelude::*,test_runner::{Config,RngSeed,TestRunner}};

fn wave(times:&[f64])->Vec<Sample> {times.iter().map(|&time_s|Sample{time_s,values:vec![1.]}).collect()}

#[test]
fn a_failed_check_missing_either_metric_outranks_finite_evidence() {
    for (d,t) in [(f64::INFINITY,0.1),(2.,f64::NAN),(f64::INFINITY,f64::NAN)] {
        let missing=Check::measured("same",d,t,"A");
        assert_eq!(missing.status,CheckStatus::Failed);
        for reverse in [false,true] {
            let finite=Check::measured("same",3.,1.,"A");
            let (first,next)=if reverse{(missing.clone(),finite)}else{(finite,missing.clone())};
            let mut report=AuditReport{checks:vec![first]};
            report.merge_worst(AuditReport{checks:vec![next]});
            assert_eq!(report.checks.len(),1);
            let actual=&report.checks[0];
            assert_eq!(actual.discrepancy,missing.discrepancy);
            assert_eq!(actual.tolerance,missing.tolerance);
            assert_eq!(actual.reason,missing.reason);
        }
    }
}

#[test]
fn normalized_severity_matches_exact_rational_cross_products() {
    let config=Config{cases:128,rng_seed:RngSeed::Fixed(2026111907),failure_persistence:None,..Config::default()};
    TestRunner::new(config).run(&(-100_i32..100,1_i32..10,-100_i32..100,1_i32..10),|(a,ta,b,tb)| {
        // Bounded integers give an exact oracle without duplicating division,
        // clamping or the floating-point severity implementation.
        let (expected_d,expected_t)=if a.abs()*tb>=b.abs()*ta{(a,ta)}else{(b,tb)};
        let mut report=AuditReport{checks:vec![Check::measured("x",a as f64,ta as f64,"V")]};
        report.merge_worst(AuditReport{checks:vec![Check::measured("x",b as f64,tb as f64,"V")]});
        prop_assert_eq!(report.checks[0].discrepancy,Some(expected_d as f64));
        prop_assert_eq!(report.checks[0].tolerance,Some(expected_t as f64));
        Ok(())
    }).unwrap();
}

#[test]
fn equal_normalized_severity_retains_the_first_evidence_and_distinct_ids() {
    let mut first=Check::measured("same",2.,1.,"A");first.reason=Some("first accepted time".into());
    let mut later=Check::measured("same",4.,2.,"A");later.reason=Some("later accepted time".into());
    let mut report=AuditReport{checks:vec![first]};
    report.merge_worst(AuditReport{checks:vec![later,Check::measured("different",0.,1.,"V")]});
    assert_eq!(report.checks.len(),2);
    assert_eq!(report.checks[0].discrepancy,Some(2.));
    assert_eq!(report.checks[0].tolerance,Some(1.));
    assert_eq!(report.checks[0].reason.as_deref(),Some("first accepted time"));
    assert_eq!(report.checks[1].id,"different");
}

#[test]
fn refinement_bounds_units_and_empty_input_are_independent_admission_checks() {
    let coarse=wave(&[0.,2.]);let fine=wave(&[0.,1.,2.]);
    for bad in [0.,-1.,f64::INFINITY,f64::NEG_INFINITY,f64::NAN] {
        for (absolute,relative) in [(bad,0.1),(0.1,bad)] {
            assert!(matches!(compare_refinement(&coarse,&fine,0,absolute,relative,"V"),Err(e) if e.code==ErrorCode::InvalidInput));
        }
    }
    for unit in ["","W","J","v","a"] {
        assert!(matches!(compare_refinement(&coarse,&fine,0,0.1,0.1,unit),Err(e) if e.code==ErrorCode::InvalidInput));
    }
    assert!(compare_refinement(&[],&fine,0,0.1,0.1,"V").is_err());
    assert!(compare_refinement(&coarse,&coarse,0,0.1,0.1,"V").is_err());
}

#[test]
fn malformed_shared_axes_and_selected_signals_are_refused() {
    // Shared malformed points avoid an incidental later no-match rejection
    // hiding a missing nonnegative or finite-axis admission check.
    for (coarse,fine) in [
        (wave(&[-1.,2.]),wave(&[-1.,0.,2.])),
        (wave(&[0.,f64::INFINITY]),wave(&[0.,1.,f64::INFINITY])),
        (wave(&[f64::NAN,2.]),wave(&[f64::NAN,1.,2.])),
        (wave(&[0.,2.]),wave(&[0.,0.,2.])),
        (wave(&[0.,2.]),wave(&[0.,2.,1.])),
    ] { assert!(matches!(compare_refinement(&coarse,&fine,0,0.1,0.1,"A"),Err(e) if e.code==ErrorCode::InvalidInput)); }
    let coarse=wave(&[0.,2.]);
    for value in [f64::NAN,f64::INFINITY,f64::NEG_INFINITY] {
        let mut fine=wave(&[0.,1.,2.]);fine[1].values[0]=value;
        assert!(compare_refinement(&coarse,&fine,0,0.1,0.1,"A").is_err());
    }
    let mut fine=wave(&[0.,1.,2.]);fine[1].values.clear();
    assert!(compare_refinement(&coarse,&fine,0,0.1,0.1,"A").is_err());
}

#[test]
fn refinement_selects_normalized_shared_point_change_without_global_accuracy_claims() {
    let coarse=vec![Sample{time_s:0.,values:vec![1.]},Sample{time_s:2.,values:vec![10.]}];
    let fine=vec![Sample{time_s:0.,values:vec![0.]},Sample{time_s:1.,values:vec![99.]},Sample{time_s:2.,values:vec![8.]}];
    for unit in ["V","A"] {
        let c=compare_refinement(&coarse,&fine,0,0.1,0.1,unit).unwrap();
        // At t=0 ratio=1/.2=5; at t=2 ratio=2/1.1<2. Larger absolute
        // discrepancy or unmatched fine midpoint must not replace the first.
        assert_eq!(c.discrepancy,Some(1.));assert_eq!(c.tolerance,Some(0.2));
        assert_eq!(c.status,CheckStatus::Failed);assert_eq!(c.unit,unit);
        assert!(c.reason.as_ref().unwrap().contains("time_s=0"));
        assert!(c.reason.as_ref().unwrap().contains("not a global error bound"));
    }
}
