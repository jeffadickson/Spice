//! Accepted coordinates must survive the managed/native JSON boundary exactly.
#[test]
fn decimal_time_coordinates_roundtrip_to_the_standard_parser_value() {
    for text in ["1.0840005611200061e-10","8.2799999999999987e-8","-1.0840005611200061e-10","1e-9","0.0","-0.0"] {
        let expected=text.parse::<f64>().unwrap();
        let actual: f64=serde_json::from_str(text).unwrap();
        assert_eq!(actual.to_bits(),expected.to_bits(),"{text}");
        let serialized=serde_json::to_string(&expected).unwrap();
        let restored:f64=serde_json::from_str(&serialized).unwrap();
        assert_eq!(restored.to_bits(),expected.to_bits(),"{serialized}");
    }
}
