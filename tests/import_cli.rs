//! CLI adapter must preserve the shared importer contract, not grow another parser.
use std::io::Write;
use std::process::{Command, Stdio};
fn import(text: &str) -> (bool, serde_json::Value) {
    let mut child = Command::new(env!("CARGO_BIN_EXE_circuit-simulator"))
        .arg("--import-ngspice")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
        .unwrap();
    child
        .stdin
        .take()
        .unwrap()
        .write_all(text.as_bytes())
        .unwrap();
    let output = child.wait_with_output().unwrap();
    (
        output.status.success(),
        serde_json::from_slice(&output.stdout).unwrap(),
    )
}
#[test]
fn import_command_returns_typed_request_without_simulating() {
    let (ok, json) = import("arbitrary title\nV1 in 0 1\nR1 in 0 1000\n.op\n.end\n");
    assert!(ok, "{json}");
    assert_eq!(json["analysis"]["kind"], "operating_point");
    assert_eq!(json["circuit"]["nodes"].as_array().unwrap().len(), 2);
    assert!(json.get("samples").is_none());
}
#[test]
fn import_command_retains_structured_rejection() {
    let (ok, json) = import("title\n.control\nshell echo never-execute\n.endc\n.end\n");
    assert!(!ok);
    assert_eq!(json["status"], "failed");
    assert!(!json["error"]["message"].as_str().unwrap().is_empty());
}
