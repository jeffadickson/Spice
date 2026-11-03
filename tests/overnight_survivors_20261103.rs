//! Regression requirements for the three November 3 mutation survivors.
use circuit_simulator::{
    api::{self, Request},
    artifact,
    audit::{self, AuditOptions},
    circuit::{Circuit, Element},
    ids::NodeId as N,
};

#[test]
fn run_id_boundary_and_invalid_alphabet_preserve_storage() {
    let request: Request = serde_json::from_str(include_str!("../examples/divider.json")).unwrap();
    let result = api::run(&request).unwrap();
    let parent = std::env::var_os("SPICE_SURVIVOR_ARTIFACT_ROOT")
        .map(std::path::PathBuf::from)
        .unwrap_or_else(std::env::temp_dir);
    assert!(parent.is_dir());
    let root = parent.join(format!("publication-{}", std::process::id()));
    for length in [129, 130, 255] {
        assert!(artifact::publish(&root, &"a".repeat(length), &result).is_err());
        assert!(!root.exists(), "invalid input must not create storage");
    }
    for id in ["", "../escape", "with space", "é", "a/b", "a\\b"] {
        assert!(artifact::publish(&root, id, &result).is_err());
        assert!(!root.exists());
    }
    for length in [1, 127, 128] {
        let path = artifact::publish(&root, &"a".repeat(length), &result).unwrap();
        assert_eq!(artifact::read_verified(&path).unwrap()["complete"], true);
    }
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn direct_gmin_audit_includes_one_siemens_and_rejects_larger_or_nonfinite() {
    // A 2 A supply balances the 1 S artificial shunt at 2 V, with no solver involved.
    let c = Circuit {
        nodes: vec!["0".into(), "out".into()],
        elements: vec![Element::Current {
            id: "supply".into(),
            p: N(0),
            n: N(1),
            amps: 2.,
        }],
    };
    assert!(
        !audit::dc(&c, &[2.], AuditOptions::default(), Some(1.))
            .unwrap()
            .has_failures()
    );
    for g in [
        f64::from_bits(1f64.to_bits() + 1),
        1.01,
        2.,
        0.,
        -1.,
        f64::NAN,
        f64::INFINITY,
        f64::NEG_INFINITY,
    ] {
        assert!(
            audit::dc(&c, &[2.], AuditOptions::default(), Some(g)).is_err(),
            "g={g}"
        );
    }
    assert!(
        audit::dc(&c, &[1.], AuditOptions::default(), Some(1.))
            .unwrap()
            .has_failures()
    );
}

fn switch_state(common: f64, output: f64) -> (Circuit, Vec<f64>) {
    // Midpoint control 0.1 V gives geometric-mean resistance sqrt(100*10000)=1000 ohm.
    // Nonzero CN makes division finite and observably different from subtraction.
    let i = output / 1000.;
    let c = Circuit {
        nodes: vec!["0".into(), "out".into(), "cp".into(), "cn".into()],
        elements: vec![
            Element::PspiceSwitch {
                id: "s".into(),
                p: N(1),
                n: N(0),
                cp: N(2),
                cn: N(3),
                ron: 100.,
                roff: 10000.,
                von: 0.2,
                voff: 0.,
            },
            Element::Current {
                id: "supply".into(),
                p: N(0),
                n: N(1),
                amps: i,
            },
        ],
    };
    (c, vec![output, common + 0.1, common])
}
#[test]
fn differential_switch_audit_checks_nonzero_common_mode_and_wrong_current() {
    let (c, state) = switch_state(0.5, 2.);
    assert!(
        !audit::dc(&c, &state, AuditOptions::default(), None)
            .unwrap()
            .has_failures()
    );
    let mut wrong = state;
    wrong[0] = 1.;
    assert!(
        audit::dc(&c, &wrong, AuditOptions::default(), None)
            .unwrap()
            .has_failures()
    );
}
proptest::proptest! {
    #![proptest_config(proptest::test_runner::Config {cases:128, rng_seed:proptest::test_runner::RngSeed::Fixed(2026110303), ..Default::default()})]
    #[test]
    fn midpoint_switch_physics_is_invariant_under_control_common_mode(common in 0.3f64..2., output in -10f64..10.) {
        let (c,state)=switch_state(common,output);
        proptest::prop_assert!(!audit::dc(&c,&state,AuditOptions::default(),None).unwrap().has_failures());
    }
}
