use circuit_simulator::api::{Request,run};
#[test]fn unknown_top_level_fields_rejected(){let text=include_str!("../examples/divider.json").replace("\"case_id\":", "\"extra\":true,\"case_id\":");assert!(serde_json::from_str::<Request>(&text).is_err());}
#[test]fn incompatible_version_and_backend_rejected(){let mut r:Request=serde_json::from_str(include_str!("../examples/divider.json")).unwrap();r.schema_version="999".into();assert!(run(&r).is_err());r.schema_version="1".into();r.backend="gpu".into();assert!(run(&r).is_err());}
#[test]fn malformed_and_unknown_element_fields_rejected(){let text=include_str!("../examples/divider.json").replace("\"ohms\":", "\"oops\":4,\"ohms\":");assert!(serde_json::from_str::<Request>(&text).is_err());}

