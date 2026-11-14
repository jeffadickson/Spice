//! Bounded diagnostic for the standard-library contract behind issue47's survivor.
//! This is not a durability/crash proof or a replacement for sync_all in publish.
use std::{
    fs::{self, File},
    io::Write,
    path::Path,
};

fn main() {
    assert!(
        cfg!(windows) || cfg!(unix),
        "Only Windows/Unix File semantics are assessed"
    );
    let output = std::env::args()
        .nth(1)
        .expect("existing output directory required");
    let output = Path::new(&output);
    assert!(output.is_dir());
    for size in [0, 1, 4096, 65536] {
        let path = output.join(format!("flush-probe-{}-{size}.bin", std::process::id()));
        let bytes: Vec<u8> = (0..size).map(|n| (n % 251) as u8).collect();
        let mut file = File::create_new(&path).unwrap();
        file.write_all(&bytes).unwrap();
        // An independent handle already sees all bytes before flush. The mutant
        // replaces only flush with Ok(()), not sync_all or the actual writes.
        assert_eq!(fs::read(&path).unwrap(), bytes);
        file.flush().unwrap();
        assert_eq!(fs::read(&path).unwrap(), bytes);
        file.sync_all().unwrap();
        drop(file);
        assert_eq!(fs::read(&path).unwrap(), bytes);
        fs::remove_file(path).unwrap();
    }
    println!(
        "{{\"cases\":4,\"pre_flush_visibility\":true,\"post_flush_unchanged\":true,\"sync_all_retained\":true}}"
    );
}
