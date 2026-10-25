use crate::{
    api::Manifest,
    error::{ErrorCode, SimError, SimResult},
};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    fs::{self, File},
    io::{Read, Write},
    path::{Path, PathBuf},
};
const LIMIT: u64 = 64 * 1024 * 1024;
#[derive(Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Commit {
    schema_version: String,
    complete: bool,
    path: String,
    bytes: u64,
    sha256: String,
}
fn io(error: impl std::fmt::Display) -> SimError {
    SimError::new(ErrorCode::IoError, error.to_string())
}
struct LimitedFile {
    file: File,
    bytes: u64,
    hash: Sha256,
    exhausted: bool,
}
impl Write for LimitedFile {
    fn write(&mut self, buffer: &[u8]) -> std::io::Result<usize> {
        if buffer.len() as u64 > LIMIT - self.bytes {
            self.exhausted = true;
            return Err(std::io::Error::other("artifact byte budget exceeded"));
        }
        let n = self.file.write(buffer)?;
        self.bytes += n as u64;
        self.hash.update(&buffer[..n]);
        Ok(n)
    }
    fn flush(&mut self) -> std::io::Result<()> {
        self.file.flush()
    }
}
/// Trusted local root only. A newly created run directory prevents overwriting prior results.
/// The final manifest is the commit point; incomplete directories are never successful artifacts.
pub fn publish(root: &Path, run_id: &str, result: &Manifest) -> SimResult<PathBuf> {
    if run_id.is_empty()
        || run_id.len() > 128
        || !run_id
            .bytes()
            .all(|b| b.is_ascii_alphanumeric() || b == b'-' || b == b'_')
    {
        return Err(SimError::invalid("invalid artifact run ID"));
    }
    if !result.complete || result.status != "succeeded" || result.schema_version!="1" {
        return Err(SimError::invalid(
            "publication requires a complete successful result",
        ));
    }
    fs::create_dir_all(root).map_err(io)?;
    let directory = root.join(run_id);
    fs::create_dir(&directory).map_err(io)?;
    let file = File::create_new(directory.join("result.json.tmp")).map_err(io)?;
    let mut writer = LimitedFile {
        file,
        bytes: 0,
        hash: Sha256::new(),
        exhausted: false,
    };
    if let Err(error) = serde_json::to_writer(&mut writer, result) {
        return Err(if writer.exhausted {
            SimError::new(
                ErrorCode::ResourceExhausted,
                "artifact byte budget exceeded",
            )
        } else {
            io(error)
        });
    }
    writer.flush().map_err(io)?;
    writer.file.sync_all().map_err(io)?;
    let commit = Commit {
        schema_version: "1".into(),
        complete: true,
        path: "result.json".into(),
        bytes: writer.bytes,
        sha256: format!("{:x}", writer.hash.finalize()),
    };
    drop(writer.file);
    fs::rename(
        directory.join("result.json.tmp"),
        directory.join("result.json"),
    )
    .map_err(io)?;
    let mut marker = File::create_new(directory.join("manifest.json.tmp")).map_err(io)?;
    serde_json::to_writer(&mut marker, &commit).map_err(io)?;
    marker.flush().map_err(io)?;
    marker.sync_all().map_err(io)?;
    drop(marker);
    fs::rename(
        directory.join("manifest.json.tmp"),
        directory.join("manifest.json"),
    )
    .map_err(io)?;
    Ok(directory)
}
/// Integrity checks are not signatures or sandboxing; storage is a trusted local directory.
pub fn read_verified(directory: &Path) -> SimResult<serde_json::Value> {
    let marker = File::open(directory.join("manifest.json")).map_err(io)?;
    if marker.metadata().map_err(io)?.len()>65536{return Err(SimError::invalid("artifact marker exceeds limit"));}
    let commit: Commit = serde_json::from_reader(marker.take(65537)).map_err(io)?;
    if commit.schema_version != "1"
        || !commit.complete
        || commit.path != "result.json"
        || commit.bytes > LIMIT
    {
        return Err(SimError::invalid("invalid artifact commit manifest"));
    }
    let mut bytes = Vec::new();
    File::open(directory.join("result.json"))
        .map_err(io)?
        .take(LIMIT + 1)
        .read_to_end(&mut bytes)
        .map_err(io)?;
    if bytes.len() as u64 != commit.bytes
        || format!("{:x}", Sha256::digest(&bytes)) != commit.sha256
    {
        return Err(SimError::invalid("artifact integrity mismatch"));
    }
    let value: serde_json::Value = serde_json::from_slice(&bytes).map_err(io)?;
    if value["complete"] != true || value["status"] != "succeeded" || value["schema_version"]!="1" {
        return Err(SimError::invalid("artifact result not complete"));
    }
    Ok(value)
}
